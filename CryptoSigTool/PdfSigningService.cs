using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;

namespace CryptoSigTool;

internal sealed class PdfSigningService
{
    private readonly CryptoProService _cryptoPro;

    public PdfSigningService(CryptoProService cryptoPro)
    {
        _cryptoPro = cryptoPro;
    }

    public static bool EnhancedCadesAvailable => CadesComPdfSigner.IsAvailable;

    public async Task SignAsync(PdfSignatureRequest request)
    {
        if (!File.Exists(request.InputPath)) throw new FileNotFoundException("Исходный PDF не найден.", request.InputPath);
        if (Path.GetExtension(request.InputPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase) is false)
            throw new InvalidOperationException("Для визуальной подписи требуется файл PDF.");
        if (string.Equals(Path.GetFullPath(request.InputPath), Path.GetFullPath(request.OutputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Подписанный PDF необходимо сохранить в новый файл.");
        var hasExistingSignature = PdfSignatureVerifier.HasEmbeddedSignature(request.InputPath);
        if ((request.AddTimestamp || request.AddEvidence) && !Uri.TryCreate(request.TspAddress, UriKind.Absolute, out _))
            throw new InvalidOperationException("Укажите корректный адрес службы TSP.");

        using var certificate = CertificateLoader.Load(request.Certificate);
        using var document = PdfReader.Open(
            request.InputPath,
            hasExistingSignature ? PdfDocumentOpenMode.Import : PdfDocumentOpenMode.Modify);
        if (request.PageIndex < 0 || request.PageIndex >= document.PageCount)
            throw new ArgumentOutOfRangeException(nameof(request.PageIndex), "Выбранная страница отсутствует в PDF.");

        if (hasExistingSignature)
        {
            await SignIncrementallyAsync(request, certificate, document.Pages[request.PageIndex].Width.Point, document.Pages[request.PageIndex].Height.Point);
            return;
        }

        IDigitalSigner signer = request.AddTimestamp || request.AddEvidence
            ? new CadesComPdfSigner(request.Certificate, request.TspAddress!, request.AddEvidence)
            : new CryptoProPdfSigner(_cryptoPro, request.Certificate, request.Algorithm);

        var page = document.Pages[request.PageIndex];
        var normalized = Normalize(request.NormalizedRectangle);
        var width = page.Width.Point * normalized.Width;
        var height = page.Height.Point * normalized.Height;
        var x = page.Width.Point * normalized.X;
        var yFromBottom = page.Height.Point * (1 - normalized.Y - normalized.Height);
        var rectangle = request.Stamp.Visualize63Fz
            ? new XRect(x, yFromBottom, width, height)
            : new XRect(0, 0, 0, 0);

        var options = new DigitalSignatureOptions
        {
            AppName = "CryptoSigTool",
            ContactInfo = request.Certificate.DisplayName,
            Location = request.Stamp.Location,
            Reason = request.Stamp.Reason,
            PageIndex = request.PageIndex,
            Rectangle = rectangle,
            AppearanceHandler = request.Stamp.Visualize63Fz
                ? new Fz63SignatureAppearance(certificate, request.Stamp)
                : null
        };
        DigitalSignatureHandler.ForDocument(document, signer, options);

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!;
        Directory.CreateDirectory(outputDirectory);
        var tempOutput = Path.Combine(outputDirectory, $".{Path.GetFileName(request.OutputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await document.SaveAsync(tempOutput);
            File.Move(tempOutput, request.OutputPath, true);
        }
        finally
        {
            try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }
        }
    }

    private async Task SignIncrementallyAsync(
        PdfSignatureRequest request,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        double pageWidth,
        double pageHeight)
    {
        var sourcePdf = await File.ReadAllBytesAsync(request.InputPath);
        var normalized = Normalize(request.NormalizedRectangle);
        var width = pageWidth * normalized.Width;
        var height = pageHeight * normalized.Height;
        var x = pageWidth * normalized.X;
        var y = pageHeight * (1 - normalized.Y - normalized.Height);
        var signatureNumber = PdfSignatureVerifier.CountEmbeddedSignatures(request.InputPath) + 1;
        var options = new OfficeIMO.Pdf.PdfExternalSignatureOptions
        {
            FieldName = $"CryptoSigToolSignature{signatureNumber}",
            Name = request.Certificate.DisplayName,
            ContactInfo = request.Certificate.DisplayName,
            Reason = request.Stamp.Reason,
            Location = request.Stamp.Location,
            SigningTime = new DateTimeOffset(request.Stamp.SigningTime),
            ReservedSignatureContentsBytes = request.AddEvidence ? 512 * 1024 : request.AddTimestamp ? 128 * 1024 : 64 * 1024,
            SubFilter = request.AddTimestamp || request.AddEvidence
                ? OfficeIMO.Pdf.PdfExternalSignatureSubFilter.CadesDetached
                : OfficeIMO.Pdf.PdfExternalSignatureSubFilter.DetachedCms
        };

        byte[]? appearanceBytes = null;
        if (request.Stamp.Visualize63Fz)
        {
            options.VisibleAppearance = new OfficeIMO.Pdf.PdfVisibleSignatureAppearanceOptions
            {
                PageNumber = request.PageIndex + 1,
                X = x,
                Y = y,
                Width = width,
                Height = height
            };
            appearanceBytes = PdfStampVectorRenderer.Render(certificate, request.Stamp, width, height);
        }

        (OfficeIMO.Pdf.PdfExternalSignaturePreparation Preparation, byte[] PreparedPdf) prepared;
        try
        {
            prepared = IncrementalPdfSignatureBuilder.Prepare(
                sourcePdf,
                options,
                appearanceBytes);
        }
        catch (OfficeIMO.Pdf.PdfMutationBlockedException ex)
        {
            throw new InvalidOperationException(
                "В PDF запрещено безопасное добавление следующей подписи. Возможные причины: политика DocMDP, блокировка полей подписи, шифрование или ограниченные права документа.",
                ex);
        }
        var signedContent = IncrementalPdfSignatureBuilder.GetSignedContent(
            prepared.PreparedPdf,
            prepared.Preparation.ByteRangeValues);
        IDigitalSigner signer = request.AddTimestamp || request.AddEvidence
            ? new CadesComPdfSigner(request.Certificate, request.TspAddress!, request.AddEvidence)
            : new CryptoProPdfSigner(_cryptoPro, request.Certificate, request.Algorithm);
        byte[] signature;
        using (var content = new MemoryStream(signedContent, writable: false))
            signature = await signer.GetSignatureAsync(content);

        var completed = IncrementalPdfSignatureBuilder.Complete(
            sourcePdf,
            prepared.PreparedPdf,
            prepared.Preparation,
            signature);
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!;
        Directory.CreateDirectory(outputDirectory);
        var tempOutput = Path.Combine(outputDirectory, $".{Path.GetFileName(request.OutputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tempOutput, completed);
            File.Move(tempOutput, request.OutputPath, true);
        }
        finally
        {
            try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }
        }
    }

    private static RectangleF Normalize(RectangleF value)
    {
        var left = Math.Clamp(Math.Min(value.Left, value.Right), 0, 1);
        var top = Math.Clamp(Math.Min(value.Top, value.Bottom), 0, 1);
        var right = Math.Clamp(Math.Max(value.Left, value.Right), 0, 1);
        var bottom = Math.Clamp(Math.Max(value.Top, value.Bottom), 0, 1);
        if (right - left < 0.02 || bottom - top < 0.02)
            throw new InvalidOperationException("Выделите на странице область достаточного размера для штампа подписи.");
        return RectangleF.FromLTRB(left, top, right, bottom);
    }
}
