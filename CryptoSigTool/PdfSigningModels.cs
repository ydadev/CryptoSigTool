using System.Security.Cryptography.X509Certificates;

namespace CryptoSigTool;

internal sealed record PdfStampSettings(
    bool Visualize63Fz,
    bool ShowSigningDate,
    string? LogoPath,
    string Reason,
    string Location,
    DateTime SigningTime,
    PdfStampDesign Design = PdfStampDesign.AcrobatBlack);

internal sealed record PdfSignatureRequest(
    string InputPath,
    string OutputPath,
    CertificateItem Certificate,
    string Algorithm,
    int PageIndex,
    RectangleF NormalizedRectangle,
    PdfStampSettings Stamp,
    bool AddTimestamp,
    bool AddEvidence,
    string? TspAddress);

internal static class CertificateLoader
{
    public static X509Certificate2 Load(CertificateItem item)
    {
        using var store = new X509Store(StoreName.My, item.StoreLocation);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, item.Thumbprint, false);
        if (matches.Count == 0)
            throw new InvalidOperationException($"Сертификат {item.Thumbprint} не найден в хранилище {item.StoreLocation}\\My.");
        return new X509Certificate2(matches[0]);
    }
}
