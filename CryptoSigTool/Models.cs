using System.Security.Cryptography.X509Certificates;

namespace CryptoSigTool;

internal sealed record CertificateItem(
    string DisplayName,
    string Thumbprint,
    StoreLocation StoreLocation,
    DateTime NotAfter,
    bool HasPrivateKey)
{
    public override string ToString()
    {
        var store = StoreLocation == StoreLocation.CurrentUser ? "пользователь" : "компьютер";
        var key = HasPrivateKey ? "ключ доступен" : "нет закрытого ключа";
        return $"{DisplayName} — до {NotAfter:dd.MM.yyyy} — {store}, {key}";
    }
}

internal sealed record ProcessResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

internal sealed record ContainerCertificateItem(
    string DisplayName,
    string ContainerName,
    string KeyType,
    string Thumbprint,
    DateTime NotAfter,
    string Subject)
{
    public string KeyTypeDisplay => KeyType == "signature" ? "Ключ подписи" : "Ключ обмена";
}

internal sealed record UserPersonalCertificateItem(
    string DisplayName,
    string Thumbprint,
    DateTime NotBefore,
    DateTime NotAfter,
    string Subject,
    string Issuer,
    bool HasPrivateKey)
{
    public bool IsExpired => NotAfter < DateTime.Now;
    public bool IsNotYetValid => NotBefore > DateTime.Now;
    public bool IsValidNow => !IsExpired && !IsNotYetValid;
    public string Status => IsExpired ? "Истекла" : IsNotYetValid ? "Ещё не действует" : "Действующая";
}

internal sealed record SignatureInspection(
    bool Detached,
    string ContentType,
    int CertificateCount,
    IReadOnlyList<SignerDetails> Signers);

internal sealed record SignerDetails(
    int Number,
    string DisplayName,
    string Subject,
    string Issuer,
    string SerialNumber,
    string Thumbprint,
    DateTime? CertificateNotBefore,
    DateTime? CertificateNotAfter,
    DateTimeOffset? SigningTime,
    bool HasTimestampToken,
    DateTimeOffset? TimestampTime,
    string DigestAlgorithm,
    string SignatureAlgorithm,
    string MessageDigest,
    string SignerIdentifier);
