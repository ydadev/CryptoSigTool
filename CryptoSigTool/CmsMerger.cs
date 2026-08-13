using System.Text;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace CryptoSigTool;

internal static class CmsMerger
{
    private sealed record Tlv(byte Tag, byte[] Encoded, byte[] Value);

    private sealed record SignedDataParts(
        byte[] ContentType,
        byte[] Version,
        List<byte[]> DigestAlgorithms,
        byte[] EContentType,
        byte[]? Content,
        List<byte[]> Certificates,
        List<byte[]> Crls,
        List<byte[]> SignerInfos);

    public static (int Signers, int Certificates, bool Detached) Merge(
        IEnumerable<string> inputPaths,
        string outputPath,
        bool base64Output,
        string? attachedContentOverride = null)
    {
        var paths = inputPaths.ToArray();
        if (paths.Length < 2)
            throw new ArgumentException("Нужно выбрать не менее двух файлов подписи.");

        var all = paths.Select(path => ParseSignedData(ReadCms(path))).ToArray();
        var first = all[0];
        foreach (var current in all.Skip(1))
        {
            if (!first.ContentType.SequenceEqual(current.ContentType))
                throw new InvalidDataException("Типы CMS-контейнеров различаются.");
            if (!first.EContentType.SequenceEqual(current.EContentType))
                throw new InvalidDataException("Типы подписанного содержимого различаются.");
            if ((first.Content is null) != (current.Content is null))
                throw new InvalidDataException("Нельзя объединить отсоединённую и вложенную подписи.");
            if (first.Content is not null && !first.Content.SequenceEqual(current.Content!))
                throw new InvalidDataException("В контейнерах находятся разные подписанные данные.");
        }

        var version = MaxVersion(all.Select(x => x.Version));
        var algorithms = UniqueSorted(all.SelectMany(x => x.DigestAlgorithms));
        var certificates = UniqueSorted(all.SelectMany(x => x.Certificates));
        var crls = UniqueSorted(all.SelectMany(x => x.Crls));
        var signers = UniqueSorted(all.SelectMany(x => x.SignerInfos));

        var finalContent = attachedContentOverride is null ? first.Content : File.ReadAllBytes(attachedContentOverride);
        var encapParts = new List<byte[]> { first.EContentType };
        if (finalContent is not null)
            encapParts.Add(Wrap(0xA0, Wrap(0x04, finalContent)));

        var signedParts = new List<byte[]>
        {
            version,
            Wrap(0x31, Join(algorithms)),
            Wrap(0x30, Join(encapParts))
        };
        if (certificates.Count > 0)
            signedParts.Add(Wrap(0xA0, Join(certificates)));
        if (crls.Count > 0)
            signedParts.Add(Wrap(0xA1, Join(crls)));
        signedParts.Add(Wrap(0x31, Join(signers)));

        var result = Wrap(0x30, Join(new[]
        {
            first.ContentType,
            Wrap(0xA0, Wrap(0x30, Join(signedParts)))
        }));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (base64Output)
        {
            var text = "-----BEGIN PKCS7-----\r\n" +
                       InsertLineBreaks(Convert.ToBase64String(result)) +
                       "\r\n-----END PKCS7-----\r\n";
            File.WriteAllText(outputPath, text, new UTF8Encoding(false));
        }
        else
        {
            File.WriteAllBytes(outputPath, result);
        }

        return (signers.Count, certificates.Count, finalContent is null);
    }

    public static bool IsBase64File(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length > 0 && bytes[0] != 0x30;
    }

    public static int GetSignerCount(string path) => ParseSignedData(ReadCms(path)).SignerInfos.Count;

    public static SignatureInspection Inspect(string path)
    {
        var parsed = ParseSignedData(ReadCms(path));
        var certificates = parsed.Certificates
            .Select(TryReadCertificate)
            .Where(x => x is not null)
            .Cast<X509Certificate2>()
            .ToArray();
        var signers = parsed.SignerInfos
            .Select((value, index) => InspectSigner(value, index + 1, certificates))
            .ToArray();
        return new SignatureInspection(
            parsed.Content is null,
            FriendlyOid(ReadOidFromEncoded(parsed.EContentType)),
            certificates.Length,
            signers);
    }

    private static byte[] ReadCms(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 0 && bytes[0] == 0x30)
            return bytes;

        var text = Encoding.ASCII.GetString(bytes)
            .Replace("-----BEGIN PKCS7-----", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-----END PKCS7-----", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-----BEGIN CMS-----", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-----END CMS-----", "", StringComparison.OrdinalIgnoreCase);
        var compact = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        try { return Convert.FromBase64String(compact); }
        catch (FormatException ex) { throw new InvalidDataException($"Файл «{Path.GetFileName(path)}» не является DER или Base64 CMS/PKCS#7.", ex); }
    }

    private static SignedDataParts ParseSignedData(byte[] data)
    {
        var (outer, end) = ReadTlv(data, 0);
        if (end != data.Length || outer.Tag != 0x30)
            throw new InvalidDataException("Файл не является одним контейнером CMS ContentInfo.");
        var outerItems = Children(outer.Value);
        if (outerItems.Count != 2 || outerItems[0].Tag != 0x06 || outerItems[1].Tag != 0xA0)
            throw new InvalidDataException("Неподдерживаемая структура CMS ContentInfo.");
        var explicitItems = Children(outerItems[1].Value);
        if (explicitItems.Count != 1 || explicitItems[0].Tag != 0x30)
            throw new InvalidDataException("Не найден контейнер SignedData.");
        var items = Children(explicitItems[0].Value);
        if (items.Count < 4 || items[0].Tag != 0x02 || items[1].Tag != 0x31 || items[2].Tag != 0x30)
            throw new InvalidDataException("Неподдерживаемая структура SignedData.");

        var encap = Children(items[2].Value);
        if (encap.Count is < 1 or > 2 || encap[0].Tag != 0x06)
            throw new InvalidDataException("Неподдерживаемая структура EncapsulatedContentInfo.");
        byte[]? content = null;
        if (encap.Count == 2)
        {
            if (encap[1].Tag != 0xA0)
                throw new InvalidDataException("Неверное поле вложенного содержимого CMS.");
            var contentItems = Children(encap[1].Value);
            if (contentItems.Count != 1)
                throw new InvalidDataException("Неподдерживаемое вложенное содержимое CMS.");
            content = ReadOctets(contentItems[0]);
        }

        var index = 3;
        var certs = new List<byte[]>();
        var crls = new List<byte[]>();
        if (index < items.Count && items[index].Tag == 0xA0)
        {
            certs.AddRange(Children(items[index].Value).Select(x => x.Encoded));
            index++;
        }
        if (index < items.Count && items[index].Tag == 0xA1)
        {
            crls.AddRange(Children(items[index].Value).Select(x => x.Encoded));
            index++;
        }
        if (index != items.Count - 1 || items[index].Tag != 0x31)
            throw new InvalidDataException("Не найден набор подписантов SignerInfos.");

        return new SignedDataParts(
            outerItems[0].Encoded,
            items[0].Encoded,
            Children(items[1].Value).Select(x => x.Encoded).ToList(),
            encap[0].Encoded,
            content,
            certs,
            crls,
            Children(items[index].Value).Select(x => x.Encoded).ToList());
    }

    private static SignerDetails InspectSigner(byte[] encoded, int number, IReadOnlyList<X509Certificate2> certificates)
    {
        var (signer, end) = ReadTlv(encoded, 0);
        if (end != encoded.Length || signer.Tag != 0x30)
            throw new InvalidDataException("Некорректный SignerInfo.");
        var fields = Children(signer.Value);
        if (fields.Count < 5 || fields[0].Tag != 0x02)
            throw new InvalidDataException("Неподдерживаемая структура SignerInfo.");

        var sid = fields[1];
        var digestOid = ReadAlgorithmOid(fields[2]);
        var index = 3;
        Tlv? signedAttributes = null;
        if (fields[index].Tag == 0xA0)
            signedAttributes = fields[index++];
        if (index + 1 >= fields.Count)
            throw new InvalidDataException("В SignerInfo отсутствуют алгоритм или значение подписи.");
        var signatureOid = ReadAlgorithmOid(fields[index++]);
        index++; // signatureValue
        Tlv? unsignedAttributes = index < fields.Count && fields[index].Tag == 0xA1 ? fields[index] : null;

        var serial = "";
        var signerIdentifier = "";
        byte[]? subjectKeyIdentifier = null;
        if (sid.Tag == 0x30)
        {
            var sidFields = Children(sid.Value);
            if (sidFields.Count == 2 && sidFields[1].Tag == 0x02)
            {
                serial = Convert.ToHexString(TrimLeadingZero(sidFields[1].Value));
                signerIdentifier = "Серийный номер: " + serial;
            }
        }
        else if (sid.Tag == 0x80)
        {
            subjectKeyIdentifier = sid.Value;
            signerIdentifier = "Идентификатор ключа: " + Convert.ToHexString(sid.Value);
        }

        var certificate = certificates.FirstOrDefault(cert =>
            (!string.IsNullOrEmpty(serial) && SerialMatches(cert, serial)) ||
            (subjectKeyIdentifier is not null && SubjectKeyIdentifierMatches(cert, subjectKeyIdentifier)));

        var attributes = signedAttributes is null ? new Dictionary<string, List<Tlv>>() : ReadAttributes(signedAttributes.Value);
        var signingTime = TryReadSigningTime(attributes);
        var messageDigest = TryReadMessageDigest(attributes);
        var unsigned = unsignedAttributes is null ? new Dictionary<string, List<Tlv>>() : ReadAttributes(unsignedAttributes.Value);
        var hasTimestamp = unsigned.ContainsKey("1.2.840.113549.1.9.16.2.14");
        var timestampTime = TryReadTimestampTime(unsigned);

        return new SignerDetails(
            number,
            certificate is null ? "Сертификат подписанта не найден в контейнере" : GetSignerDisplayName(certificate),
            certificate?.Subject ?? "—",
            certificate?.Issuer ?? "—",
            certificate?.SerialNumber ?? (string.IsNullOrEmpty(serial) ? "—" : serial),
            certificate?.Thumbprint ?? "—",
            certificate?.NotBefore,
            certificate?.NotAfter,
            signingTime,
            hasTimestamp,
            timestampTime,
            FriendlyOid(digestOid),
            FriendlyOid(signatureOid),
            messageDigest,
            string.IsNullOrEmpty(signerIdentifier) ? "—" : signerIdentifier);
    }

    private static Dictionary<string, List<Tlv>> ReadAttributes(byte[] implicitSetValue)
    {
        var result = new Dictionary<string, List<Tlv>>();
        foreach (var attribute in Children(implicitSetValue))
        {
            if (attribute.Tag != 0x30) continue;
            var fields = Children(attribute.Value);
            if (fields.Count != 2 || fields[0].Tag != 0x06 || fields[1].Tag != 0x31) continue;
            result[ReadOid(fields[0].Value)] = Children(fields[1].Value);
        }
        return result;
    }

    private static DateTimeOffset? TryReadSigningTime(IReadOnlyDictionary<string, List<Tlv>> attributes)
    {
        if (!attributes.TryGetValue("1.2.840.113549.1.9.5", out var values) || values.Count == 0)
            return null;
        return TryReadAsnTime(values[0]);
    }

    private static DateTimeOffset? TryReadTimestampTime(IReadOnlyDictionary<string, List<Tlv>> attributes)
    {
        if (!attributes.TryGetValue("1.2.840.113549.1.9.16.2.14", out var values) || values.Count == 0)
            return null;
        try
        {
            var token = ParseSignedData(values[0].Encoded);
            if (token.Content is null) return null;
            var (tstInfo, end) = ReadTlv(token.Content, 0);
            if (end != token.Content.Length || tstInfo.Tag != 0x30) return null;
            var fields = Children(tstInfo.Value);
            return fields.Count > 4 ? TryReadAsnTime(fields[4]) : null;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? TryReadAsnTime(Tlv value)
    {
        var text = Encoding.ASCII.GetString(value.Value);
        var formats = value.Tag == 0x17
            ? new[] { "yyMMddHHmmss'Z'", "yyMMddHHmm'Z'", "yyMMddHHmmsszzz", "yyMMddHHmmzzz" }
            : new[] { "yyyyMMddHHmmss'Z'", "yyyyMMddHHmmss.FFFFFFF'Z'", "yyyyMMddHHmm'Z'", "yyyyMMddHHmmsszzz", "yyyyMMddHHmmzzz" };
        if (DateTimeOffset.TryParseExact(text, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;
        return null;
    }

    private static string TryReadMessageDigest(IReadOnlyDictionary<string, List<Tlv>> attributes)
    {
        if (!attributes.TryGetValue("1.2.840.113549.1.9.4", out var values) || values.Count == 0 || values[0].Tag != 0x04)
            return "—";
        return Convert.ToHexString(values[0].Value);
    }

    private static string ReadAlgorithmOid(Tlv algorithm)
    {
        if (algorithm.Tag != 0x30) return "";
        var fields = Children(algorithm.Value);
        return fields.Count > 0 && fields[0].Tag == 0x06 ? ReadOid(fields[0].Value) : "";
    }

    private static string ReadOidFromEncoded(byte[] encoded)
    {
        var (value, end) = ReadTlv(encoded, 0);
        return end == encoded.Length && value.Tag == 0x06 ? ReadOid(value.Value) : "";
    }

    private static string ReadOid(byte[] value)
    {
        if (value.Length == 0) return "";
        var parts = new List<long> { Math.Min(2, value[0] / 40), value[0] < 80 ? value[0] % 40 : value[0] - 80 };
        long current = 0;
        foreach (var part in value.Skip(1))
        {
            current = checked((current << 7) | (uint)(part & 0x7F));
            if ((part & 0x80) == 0)
            {
                parts.Add(current);
                current = 0;
            }
        }
        return string.Join('.', parts);
    }

    private static string FriendlyOid(string oid) => oid switch
    {
        "1.2.840.113549.1.7.1" => "Данные PKCS#7 (1.2.840.113549.1.7.1)",
        "1.2.643.7.1.1.2.2" => "ГОСТ Р 34.11-2012, 256 бит (1.2.643.7.1.1.2.2)",
        "1.2.643.7.1.1.2.3" => "ГОСТ Р 34.11-2012, 512 бит (1.2.643.7.1.1.2.3)",
        "1.2.643.2.2.9" => "ГОСТ Р 34.11-94 (1.2.643.2.2.9)",
        "1.3.14.3.2.26" => "SHA-1 (1.3.14.3.2.26)",
        "2.16.840.1.101.3.4.2.1" => "SHA-256 (2.16.840.1.101.3.4.2.1)",
        "2.16.840.1.101.3.4.2.2" => "SHA-384 (2.16.840.1.101.3.4.2.2)",
        "2.16.840.1.101.3.4.2.3" => "SHA-512 (2.16.840.1.101.3.4.2.3)",
        "1.2.643.7.1.1.3.2" => "ГОСТ Р 34.10-2012, 256 бит (1.2.643.7.1.1.3.2)",
        "1.2.643.7.1.1.3.3" => "ГОСТ Р 34.10-2012, 512 бит (1.2.643.7.1.1.3.3)",
        "1.2.643.7.1.1.1.1" => "ГОСТ Р 34.10-2012, 256 бит (1.2.643.7.1.1.1.1)",
        "1.2.643.7.1.1.1.2" => "ГОСТ Р 34.10-2012, 512 бит (1.2.643.7.1.1.1.2)",
        "1.2.643.2.2.3" => "ГОСТ Р 34.10-2001 (1.2.643.2.2.3)",
        "1.2.643.2.2.19" => "ГОСТ Р 34.10-2001 (1.2.643.2.2.19)",
        "1.2.840.113549.1.1.11" => "RSA с SHA-256 (1.2.840.113549.1.1.11)",
        "1.2.840.113549.1.1.5" => "RSA с SHA-1 (1.2.840.113549.1.1.5)",
        _ => string.IsNullOrWhiteSpace(oid) ? "Не указан" : oid
    };

    private static X509Certificate2? TryReadCertificate(byte[] encoded)
    {
        try { return new X509Certificate2(encoded); }
        catch { return null; }
    }

    private static string GetSignerDisplayName(X509Certificate2 certificate)
    {
        var surname = GetDnValue(certificate.Subject, "SN");
        var givenName = GetDnValue(certificate.Subject, "G");
        if (!string.IsNullOrWhiteSpace(surname) || !string.IsNullOrWhiteSpace(givenName))
        {
            var rawName = string.Join(' ', new[] { surname, givenName }.Where(x => !string.IsNullOrWhiteSpace(x))).ToLower(new CultureInfo("ru-RU"));
            var name = new CultureInfo("ru-RU").TextInfo.ToTitleCase(rawName);
            var organization = certificate.GetNameInfo(X509NameType.SimpleName, false);
            return string.IsNullOrWhiteSpace(organization) || organization.Equals(name, StringComparison.OrdinalIgnoreCase)
                ? name
                : $"{name} — {organization}";
        }
        return certificate.GetNameInfo(X509NameType.SimpleName, false);
    }

    private static string GetDnValue(string distinguishedName, string key)
    {
        var pattern = "(?:^|,\\s*)" + Regex.Escape(key) + "=(?:\"(?<quoted>[^\"]*)\"|(?<plain>[^,]*))";
        var match = Regex.Match(distinguishedName, pattern, RegexOptions.IgnoreCase);
        return match.Success ? (match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value).Trim() : "";
    }

    private static bool SerialMatches(X509Certificate2 certificate, string serial)
    {
        var normalized = serial.TrimStart('0');
        var property = certificate.SerialNumber.TrimStart('0');
        var raw = Convert.ToHexString(certificate.GetSerialNumber()).TrimStart('0');
        var reversed = Convert.ToHexString(certificate.GetSerialNumber().Reverse().ToArray()).TrimStart('0');
        return normalized.Equals(property, StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(raw, StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(reversed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SubjectKeyIdentifierMatches(X509Certificate2 certificate, byte[] expected)
    {
        var extension = certificate.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault();
        return extension is not null && string.Equals(extension.SubjectKeyIdentifier, Convert.ToHexString(expected), StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] TrimLeadingZero(byte[] value) => value.Length > 1 && value[0] == 0 ? value[1..] : value;

    private static byte[] ReadOctets(Tlv value)
    {
        if (value.Tag == 0x04)
            return value.Value;
        if (value.Tag == 0x24)
            return Join(Children(value.Value).Select(ReadOctets));
        throw new InvalidDataException("Вложенное содержимое CMS не является OCTET STRING.");
    }

    private static List<Tlv> Children(byte[] value)
    {
        var result = new List<Tlv>();
        var offset = 0;
        while (offset < value.Length)
        {
            var (item, next) = ReadTlv(value, offset);
            result.Add(item);
            offset = next;
        }
        return result;
    }

    private static (Tlv Item, int Next) ReadTlv(byte[] data, int offset)
    {
        if (offset >= data.Length)
            throw new InvalidDataException("Неожиданный конец ASN.1.");
        var start = offset;
        var tag = data[offset++];
        if ((tag & 0x1F) == 0x1F)
            throw new InvalidDataException("ASN.1-тег высокой формы не поддерживается.");
        if (offset >= data.Length)
            throw new InvalidDataException("В ASN.1 отсутствует длина.");
        var first = data[offset++];
        if (first == 0x80)
        {
            if ((tag & 0x20) == 0)
                throw new InvalidDataException("Неопределённая длина у примитивного ASN.1-значения.");
            var valueStart = offset;
            var cursor = offset;
            while (true)
            {
                if (cursor + 2 > data.Length)
                    throw new InvalidDataException("Не найден маркер конца BER.");
                if (data[cursor] == 0 && data[cursor + 1] == 0)
                {
                    var encoded = data[start..(cursor + 2)];
                    return (new Tlv(tag, encoded, data[valueStart..cursor]), cursor + 2);
                }
                (_, cursor) = ReadTlv(data, cursor);
            }
        }

        int length;
        if (first < 0x80)
        {
            length = first;
        }
        else
        {
            var count = first & 0x7F;
            if (count > 4 || offset + count > data.Length)
                throw new InvalidDataException("Некорректная длина ASN.1.");
            length = 0;
            for (var i = 0; i < count; i++)
                length = checked((length << 8) | data[offset++]);
        }
        var end = checked(offset + length);
        if (end > data.Length)
            throw new InvalidDataException("ASN.1-значение обрезано.");
        return (new Tlv(tag, data[start..end], data[offset..end]), end);
    }

    private static List<byte[]> UniqueSorted(IEnumerable<byte[]> values)
    {
        var comparer = ByteArrayComparer.Instance;
        return values.Distinct(comparer).OrderBy(x => x, comparer).ToList();
    }

    private static byte[] MaxVersion(IEnumerable<byte[]> versions) =>
        versions.OrderBy(ReadSmallInteger).Last();

    private static int ReadSmallInteger(byte[] encoded)
    {
        var (item, end) = ReadTlv(encoded, 0);
        if (end != encoded.Length || item.Tag != 0x02 || item.Value.Length is 0 or > 4 || (item.Value[0] & 0x80) != 0)
            throw new InvalidDataException("Некорректная версия SignedData.");
        var value = 0;
        foreach (var part in item.Value) value = checked((value << 8) | part);
        return value;
    }

    private static byte[] Wrap(byte tag, byte[] value) => Join(new[] { new[] { tag }, LengthBytes(value.Length), value });

    private static byte[] LengthBytes(int length)
    {
        if (length < 0x80) return new[] { (byte)length };
        var parts = new List<byte>();
        for (var current = length; current > 0; current >>= 8)
            parts.Add((byte)(current & 0xFF));
        parts.Reverse();
        return new[] { (byte)(0x80 | parts.Count) }.Concat(parts).ToArray();
    }

    private static byte[] Join(IEnumerable<byte[]> values)
    {
        var arrays = values.ToArray();
        var result = new byte[arrays.Sum(x => x.Length)];
        var offset = 0;
        foreach (var value in arrays)
        {
            Buffer.BlockCopy(value, 0, result, offset, value.Length);
            offset += value.Length;
        }
        return result;
    }

    private static string InsertLineBreaks(string value) =>
        string.Join("\r\n", Enumerable.Range(0, (value.Length + 63) / 64).Select(i => value.Substring(i * 64, Math.Min(64, value.Length - i * 64))));

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>, IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();
        public bool Equals(byte[]? x, byte[]? y) => ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));
        public int GetHashCode(byte[] obj)
        {
            var hash = new HashCode();
            foreach (var b in obj) hash.Add(b);
            return hash.ToHashCode();
        }
        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                var c = x[i].CompareTo(y[i]);
                if (c != 0) return c;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
