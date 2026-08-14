using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.RegularExpressions;

namespace CryptoSigTool;

internal sealed record PdfEmbeddedSignatureVerification(int SignerCount, string DigestAlgorithm, string SignatureAlgorithm);

internal static partial class PdfSignatureVerifier
{
    private sealed record ExtractedSignature(byte[] Content, byte[] Signature);

    public static bool HasEmbeddedSignature(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return ByteRangeRegex().IsMatch(Encoding.Latin1.GetString(bytes));
    }

    public static int CountEmbeddedSignatures(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return ByteRangeRegex().Matches(Encoding.Latin1.GetString(bytes)).Count;
    }

    public static PdfEmbeddedSignatureVerification VerifyFirst(string path)
    {
        var extracted = ExtractFirst(path);
        var cms = Decode(extracted);
        cms.CheckSignature(verifySignatureOnly: true);
        return Describe(cms);
    }

    public static async Task<PdfEmbeddedSignatureVerification> VerifyFirstAsync(
        string path,
        CryptoProService cryptoPro,
        CancellationToken cancellationToken = default)
    {
        var extracted = ExtractFirst(path);
        return await VerifyExtractedAsync(extracted, cryptoPro, cancellationToken);
    }

    public static async Task<IReadOnlyList<PdfEmbeddedSignatureVerification>> VerifyAllAsync(
        string path,
        CryptoProService cryptoPro,
        CancellationToken cancellationToken = default)
    {
        var pdf = File.ReadAllBytes(path);
        var matches = ByteRangeRegex().Matches(Encoding.Latin1.GetString(pdf));
        if (matches.Count == 0)
            throw new InvalidDataException("В PDF не найдено встроенных подписей ByteRange.");
        var result = new List<PdfEmbeddedSignatureVerification>(matches.Count);
        foreach (Match match in matches)
            result.Add(await VerifyExtractedAsync(Extract(pdf, match), cryptoPro, cancellationToken));
        return result;
    }

    private static async Task<PdfEmbeddedSignatureVerification> VerifyExtractedAsync(
        ExtractedSignature extracted,
        CryptoProService cryptoPro,
        CancellationToken cancellationToken)
    {
        SignedCms? cms = null;
        try
        {
            cms = Decode(extracted);
        }
        catch (CryptographicException)
        {
            // Some .NET installations cannot decode CMS certificates with GOST keys.
        }

        if (cms is not null && !RequiresCryptoPro(cms))
        {
            try
            {
                cms.CheckSignature(verifySignatureOnly: true);
                return Describe(cms);
            }
            catch (CryptographicException) when (cryptoPro.IsInstalled)
            {
                // Let the installed provider verify algorithms unavailable to SignedCms.
            }
        }

        if (!cryptoPro.IsInstalled)
            throw new InvalidOperationException("Для проверки этой PDF-подписи требуется установленный CryptoPro CSP.");

        var inspection = await VerifyWithCryptoProAsync(extracted, cryptoPro, cancellationToken);
        if (cms is not null)
            return Describe(cms);

        var signer = inspection.Signers.FirstOrDefault();
        return new PdfEmbeddedSignatureVerification(
            inspection.Signers.Count,
            signer?.DigestAlgorithm ?? "не определён",
            signer?.SignatureAlgorithm ?? "не определён");
    }

    private static ExtractedSignature ExtractFirst(string path)
    {
        var pdf = File.ReadAllBytes(path);
        var text = Encoding.Latin1.GetString(pdf);
        var byteRange = ByteRangeRegex().Match(text);
        if (!byteRange.Success) throw new InvalidDataException("В PDF не найден диапазон встроенной подписи ByteRange.");

        return Extract(pdf, byteRange);
    }

    private static ExtractedSignature Extract(byte[] pdf, Match byteRange)
    {

        var firstOffset = ParseRange(byteRange.Groups[1].Value);
        var firstLength = ParseRange(byteRange.Groups[2].Value);
        var secondOffset = ParseRange(byteRange.Groups[3].Value);
        var secondLength = ParseRange(byteRange.Groups[4].Value);
        ValidateRange(firstOffset, firstLength, pdf.Length);
        ValidateRange(secondOffset, secondLength, pdf.Length);

        var content = new byte[checked(firstLength + secondLength)];
        Buffer.BlockCopy(pdf, firstOffset, content, 0, firstLength);
        Buffer.BlockCopy(pdf, secondOffset, content, firstLength, secondLength);

        var signatureStart = checked(firstOffset + firstLength);
        var signatureEnd = secondOffset;
        if (signatureEnd <= signatureStart || signatureEnd > pdf.Length)
            throw new InvalidDataException("Некорректная область Contents в PDF-подписи.");
        var excluded = Encoding.ASCII.GetString(pdf, signatureStart, signatureEnd - signatureStart);
        var hex = NonHexRegex().Replace(excluded, "");
        if (hex.Length == 0 || (hex.Length & 1) != 0)
            throw new InvalidDataException("В PDF не найден корректный контейнер Contents встроенной подписи.");
        var paddedSignature = Convert.FromHexString(hex);
        var signatureLength = GetDerLength(paddedSignature);
        var signature = paddedSignature.AsSpan(0, signatureLength).ToArray();

        return new ExtractedSignature(content, signature);
    }

    private static SignedCms Decode(ExtractedSignature extracted)
    {
        var cms = new SignedCms(new ContentInfo(extracted.Content), detached: true);
        cms.Decode(extracted.Signature);
        return cms;
    }

    private static PdfEmbeddedSignatureVerification Describe(SignedCms cms)
    {
        var signer = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0] : null;
        return new PdfEmbeddedSignatureVerification(
            cms.SignerInfos.Count,
            signer?.DigestAlgorithm?.Value ?? "не определён",
            signer?.SignatureAlgorithm?.Value ?? "не определён");
    }

    private static bool RequiresCryptoPro(SignedCms cms)
    {
        foreach (SignerInfo signer in cms.SignerInfos)
        {
            if (IsGostOid(signer.DigestAlgorithm?.Value) ||
                IsGostOid(signer.SignatureAlgorithm?.Value) ||
                IsGostOid(signer.Certificate?.PublicKey?.Oid?.Value))
                return true;
        }
        return false;
    }

    private static bool IsGostOid(string? oid) =>
        oid?.StartsWith("1.2.643.", StringComparison.Ordinal) == true;

    private static async Task<SignatureInspection> VerifyWithCryptoProAsync(
        ExtractedSignature extracted,
        CryptoProService cryptoPro,
        CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CryptoSigTool", "pdf-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var contentPath = Path.Combine(tempRoot, "pdf-byte-range.bin");
        var signaturePath = Path.Combine(tempRoot, "signature.p7s");
        try
        {
            await File.WriteAllBytesAsync(contentPath, extracted.Content, cancellationToken);
            await File.WriteAllBytesAsync(signaturePath, extracted.Signature, cancellationToken);
            var result = await cryptoPro.VerifyDetachedAsync(contentPath, signaturePath, cancellationToken);
            if (!result.Success)
                throw new CryptographicException("CryptoPro не подтвердил встроенную PDF-подпись.\r\n\r\n" + result.Output.Trim());
            return CmsMerger.Inspect(signaturePath);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static int ParseRange(string value)
    {
        if (!long.TryParse(value, out var parsed) || parsed < 0 || parsed > int.MaxValue)
            throw new InvalidDataException("PDF содержит неподдерживаемый размер ByteRange.");
        return (int)parsed;
    }

    private static void ValidateRange(int offset, int length, int total)
    {
        if (offset < 0 || length < 0 || offset > total || length > total - offset)
            throw new InvalidDataException("PDF содержит некорректный ByteRange.");
    }

    private static int GetDerLength(byte[] data)
    {
        if (data.Length < 2 || data[0] != 0x30) throw new InvalidDataException("Contents не является контейнером CMS DER.");
        var firstLength = data[1];
        if ((firstLength & 0x80) == 0) return checked(2 + firstLength);
        var count = firstLength & 0x7F;
        if (count is 0 or > 4 || data.Length < 2 + count) throw new InvalidDataException("Некорректная длина CMS DER.");
        var length = 0;
        for (var index = 0; index < count; index++) length = checked((length << 8) | data[2 + index]);
        var total = checked(2 + count + length);
        if (total > data.Length) throw new InvalidDataException("Контейнер CMS обрезан.");
        return total;
    }

    [GeneratedRegex(@"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex ByteRangeRegex();

    [GeneratedRegex(@"[^0-9A-Fa-f]", RegexOptions.CultureInvariant)]
    private static partial Regex NonHexRegex();
}
