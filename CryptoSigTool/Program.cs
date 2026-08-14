namespace CryptoSigTool;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0].Equals("--pdf-sign-smoke", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var rsa = System.Security.Cryptography.RSA.Create(2048);
                var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                    "CN=CryptoSigTool PDF Smoke Test",
                    rsa,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                using var certificate = request.CreateSelfSigned(DateTimeOffset.Now.AddMinutes(-1), DateTimeOffset.Now.AddDays(1));
                using var document = new PdfSharp.Pdf.PdfDocument();
                var page = document.AddPage();
                using (var graphics = PdfSharp.Drawing.XGraphics.FromPdfPage(page))
                {
                    var font = new PdfSharp.Drawing.XFont("Arial", 15);
                    graphics.DrawString("Проверка встроенной PDF-подписи CryptoSigTool", font, PdfSharp.Drawing.XBrushes.Black,
                        new PdfSharp.Drawing.XRect(50, 60, page.Width.Point - 100, 30), PdfSharp.Drawing.XStringFormats.TopLeft);
                }

                var stamp = new PdfStampSettings(true, false, null, "Проверка", "Москва", DateTime.Now);
                var options = new PdfSharp.Pdf.Signatures.DigitalSignatureOptions
                {
                    AppName = "CryptoSigTool",
                    PageIndex = 0,
                    Rectangle = new PdfSharp.Drawing.XRect(50, 50, 300, 115),
                    AppearanceHandler = new Fz63SignatureAppearance(certificate, stamp),
                    Reason = stamp.Reason,
                    Location = stamp.Location
                };
                PdfSharp.Pdf.Signatures.DigitalSignatureHandler.ForDocument(
                    document,
                    new RangedStreamSmokeSigner(certificate),
                    options);
                document.SaveAsync(args[1]).GetAwaiter().GetResult();
                var verification = PdfSignatureVerifier.VerifyFirst(args[1]);
                Console.WriteLine($"OK signed-pdf bytes={new FileInfo(args[1]).Length} signers={verification.SignerCount} digest={verification.DigestAlgorithm}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 3 && args[0].Equals("--pdf-render-smoke", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var viewer = new PdfViewerService();
                viewer.OpenAsync(args[1]).GetAwaiter().GetResult();
                using var image = viewer.RenderPageAsync(0, 1200).GetAwaiter().GetResult();
                image.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine($"OK pdf pages={viewer.PageCount} image={image.Width}x{image.Height}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length is >= 3 and <= 6 && args[0].Equals("--pdf-incremental-smoke", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var source = File.ReadAllBytes(args[1]);
                using var rsa = System.Security.Cryptography.RSA.Create(2048);
                var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                    "CN=ООО ОРСИС-АГРО",
                    rsa,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                using var publicCertificate = request.Create(
                    request.SubjectName,
                    System.Security.Cryptography.X509Certificates.X509SignatureGenerator.CreateForRSA(
                        rsa,
                        System.Security.Cryptography.RSASignaturePadding.Pkcs1),
                    DateTimeOffset.Now.AddMinutes(-1),
                    DateTimeOffset.Now.AddDays(1),
                    Convert.FromHexString("03BF27C5005CB4CF9D4474D953C246B9B13A1E82"));
                using var certificate = System.Security.Cryptography.X509Certificates.RSACertificateExtensions.CopyWithPrivateKey(
                    publicCertificate,
                    rsa);
                var design = args.Length == 4 && Enum.TryParse<PdfStampDesign>(args[3], true, out var selectedDesign)
                    ? selectedDesign
                    : PdfStampDesign.AcrobatBlack;
                string? logoPath = null;
                var showDate = false;
                if (args.Length >= 5)
                {
                    if (bool.TryParse(args[4], out var parsedShowDate)) showDate = parsedShowDate;
                    else logoPath = args[4];
                }
                if (args.Length == 6 && bool.TryParse(args[5], out var parsedShowDateWithLogo))
                    showDate = parsedShowDateWithLogo;
                var stamp = new PdfStampSettings(true, showDate, logoPath, "Проверка второй подписи", "Москва", DateTime.Now, design);
                const double stampWidth = 300;
                const double stampHeight = 115;
                var appearance = PdfStampVectorRenderer.Render(certificate, stamp, stampWidth, stampHeight);
                var options = new OfficeIMO.Pdf.PdfExternalSignatureOptions
                {
                    FieldName = "CryptoSigToolIncrementalSmoke" + Guid.NewGuid().ToString("N"),
                    Name = "CryptoSigTool Incremental PDF Smoke Test",
                    Reason = stamp.Reason,
                    Location = stamp.Location,
                    ReservedSignatureContentsBytes = 64 * 1024,
                    VisibleAppearance = new OfficeIMO.Pdf.PdfVisibleSignatureAppearanceOptions
                    {
                        PageNumber = 1,
                        X = 50,
                        Y = 50,
                        Width = stampWidth,
                        Height = stampHeight
                    }
                };
                var prepared = IncrementalPdfSignatureBuilder.Prepare(
                    source, options, appearance);
                var signedContent = IncrementalPdfSignatureBuilder.GetSignedContent(prepared.PreparedPdf, prepared.Preparation.ByteRangeValues);
                var signer = new PdfSharp.Pdf.Signatures.PdfSharpDefaultSigner(
                    certificate,
                    PdfSharp.Pdf.Signatures.PdfMessageDigestType.SHA256,
                    null);
                byte[] signature;
                using (var content = new MemoryStream(signedContent, writable: false))
                    signature = signer.GetSignatureAsync(content).GetAwaiter().GetResult();
                var completed = IncrementalPdfSignatureBuilder.Complete(source, prepared.PreparedPdf, prepared.Preparation, signature);
                File.WriteAllBytes(args[2], completed);
                var verifications = PdfSignatureVerifier.VerifyAllAsync(args[2], new CryptoProService()).GetAwaiter().GetResult();
                var appended = completed.AsSpan(source.Length);
                var containsInlineImage = appended.IndexOf("BI /W"u8) >= 0;
                Console.WriteLine($"OK incremental signatures={verifications.Count} prefix={completed.AsSpan(0, source.Length).SequenceEqual(source)} vectorText=true rasterLogo={containsInlineImage} design={design} bytes={completed.Length}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 2 && args[0].Equals("--pdf-verify", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var verifications = PdfSignatureVerifier.VerifyAllAsync(args[1], new CryptoProService()).GetAwaiter().GetResult();
                for (var index = 0; index < verifications.Count; index++)
                {
                    var verification = verifications[index];
                    Console.WriteLine($"signature={index + 1} signers={verification.SignerCount} digest={verification.DigestAlgorithm} algorithm={verification.SignatureAlgorithm}");
                }
                Console.WriteLine($"OK signatures={verifications.Count}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 3 && args[0].Equals("--pdf-remove-signatures-smoke", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var before = PdfSignatureRemovalService.Inspect(args[1]);
                var result = PdfSignatureRemovalService.RemoveAll(args[1], args[2]);
                var after = PdfSignatureRemovalService.Inspect(args[2]);
                Console.WriteLine($"OK removed={result.RemovedSignatures} before={before.Signatures.Count} after={after.Signatures.Count} pages={result.PageCount} bytes={result.OutputBytes}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 1 && args[0].Equals("--ui-smoke", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            using var form = new MainForm();
            form.CreateControl();
            Console.WriteLine($"OK ui tabs={form.TabOrderForTest} pdfDateDefault={form.PdfShowDateDefaultForTest}");
            return 0;
        }

        if (args.Length == 2 && args[0].Equals("--inspect", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var result = CmsMerger.Inspect(args[1]);
                Console.WriteLine($"detached={result.Detached} content={result.ContentType} certificates={result.CertificateCount} signers={result.Signers.Count}");
                foreach (var signer in result.Signers)
                    Console.WriteLine($"#{signer.Number} {signer.DisplayName} | time={signer.SigningTime:O} | digest={signer.DigestAlgorithm} | signature={signer.SignatureAlgorithm} | timestamp={signer.TimestampTime:O}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 1 && args[0].Equals("--scan-containers", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var cryptoPro = new CryptoProService();
                var result = cryptoPro.GetMissingContainerCertificatesAsync(Console.WriteLine).GetAwaiter().GetResult();
                Console.WriteLine($"missing={result.Count}");
                foreach (var certificate in result)
                    Console.WriteLine($"{certificate.DisplayName} | {certificate.KeyTypeDisplay} | {certificate.ContainerName} | {certificate.Thumbprint}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 1 && args[0].Equals("--certificate-store-smoke", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var certificates = new CryptoProService().GetCurrentUserPersonalCertificates();
                Console.WriteLine($"OK certificates={certificates.Count} valid={certificates.Count(x => x.IsValidNow)} expired={certificates.Count(x => x.IsExpired)} notYetValid={certificates.Count(x => x.IsNotYetValid)}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 4 && (args[0].Equals("--merge", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--merge64", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var result = CmsMerger.Merge(new[] { args[2], args[3] }, args[1], args[0].Equals("--merge64", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"OK signers={result.Signers} certificates={result.Certificates} detached={result.Detached}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 4 && args[0].Equals("--attach", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var result = CmsMerger.Merge(new[] { args[3], args[3] }, args[1], false, args[2]);
                Console.WriteLine($"OK signers={result.Signers} certificates={result.Certificates} detached={result.Detached}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}

internal sealed class RangedStreamSmokeSigner : PdfSharp.Pdf.Signatures.IDigitalSigner
{
    private readonly PdfSharp.Pdf.Signatures.PdfSharpDefaultSigner _signer;

    public RangedStreamSmokeSigner(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        _signer = new PdfSharp.Pdf.Signatures.PdfSharpDefaultSigner(
            certificate,
            PdfSharp.Pdf.Signatures.PdfMessageDigestType.SHA256,
            null);
    }

    public string CertificateName => _signer.CertificateName;

    public Task<int> GetSignatureSizeAsync() => _signer.GetSignatureSizeAsync();

    public async Task<byte[]> GetSignatureAsync(Stream stream)
    {
        // PDFsharp supplies a RangedStream here. Its CanSeek getter throws even though
        // Position is supported, which is the exact contract used by production signers.
        using var copy = new MemoryStream();
        await PdfSignatureStream.CopySequentiallyAsync(stream, copy);
        copy.Position = 0;
        return await _signer.GetSignatureAsync(copy);
    }
}
