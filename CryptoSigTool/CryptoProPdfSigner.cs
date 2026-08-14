using PdfSharp.Pdf.Signatures;

namespace CryptoSigTool;

internal sealed class CryptoProPdfSigner : IDigitalSigner
{
    private const int ReservedSignatureSize = 64 * 1024;
    private readonly CryptoProService _cryptoPro;
    private readonly CertificateItem _certificate;
    private readonly string _algorithm;

    public CryptoProPdfSigner(CryptoProService cryptoPro, CertificateItem certificate, string algorithm)
    {
        _cryptoPro = cryptoPro;
        _certificate = certificate;
        _algorithm = algorithm;
    }

    public string CertificateName => _certificate.DisplayName;

    public Task<int> GetSignatureSizeAsync() => Task.FromResult(ReservedSignatureSize);

    public async Task<byte[]> GetSignatureAsync(Stream stream)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CryptoSigTool", "pdf-sign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var contentPath = Path.Combine(tempRoot, "pdf-byte-range.bin");
        var signaturePath = Path.Combine(tempRoot, "signature.p7s");
        try
        {
            await using (var output = new FileStream(contentPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await PdfSignatureStream.CopySequentiallyAsync(stream, output);
            }

            var result = await _cryptoPro.SignAsync(
                contentPath,
                signaturePath,
                _certificate,
                _algorithm,
                detached: true,
                base64: false);
            if (!result.Success)
                throw new InvalidOperationException("CryptoPro не смог сформировать PDF-подпись.\r\n\r\n" + result.Output.Trim());

            var signature = await File.ReadAllBytesAsync(signaturePath);
            if (signature.Length > ReservedSignatureSize)
                throw new InvalidDataException($"Размер подписи {signature.Length} байт превышает зарезервированные {ReservedSignatureSize} байт.");
            return signature;
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }
}
