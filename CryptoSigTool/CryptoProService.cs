using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CryptoSigTool;

internal sealed class CryptoProService
{
    public string? ToolPath { get; }
    public string? CertMgrPath { get; }
    public bool IsInstalled => ToolPath is not null && CertMgrPath is not null;

    public CryptoProService()
    {
        ToolPath = FindTool();
        CertMgrPath = FindExecutable("certmgr.exe");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<List<ContainerCertificateItem>> GetMissingContainerCertificatesAsync(
        Action<string>? progress = null,
        CancellationToken token = default)
    {
        if (ToolPath is null) throw new InvalidOperationException("CryptoPro csptest.exe не найден.");
        var enumeration = await RunExecutableAsync(ToolPath,
            new[] { "-keyset", "-enum_containers", "-fqcn", "-verifycontext", "-silent" }, token);
        if (!enumeration.Success)
            throw new InvalidOperationException("Не удалось получить контейнеры CryptoPro.\r\n" + enumeration.Output.Trim());

        var containers = enumeration.Output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.StartsWith(@"\\.\", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var installed = GetCurrentUserThumbprints();
        var result = new List<ContainerCertificateItem>();
        var tempRoot = Path.Combine(Path.GetTempPath(), "CryptoSigTool", "cert-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            for (var containerIndex = 0; containerIndex < containers.Length; containerIndex++)
            {
                var container = containers[containerIndex];
                progress?.Invoke($"Контейнер {containerIndex + 1} из {containers.Length}: {container}");
                foreach (var keyType in new[] { "exchange", "signature" })
                {
                    var certPath = Path.Combine(tempRoot, $"{containerIndex}-{keyType}.cer");
                    var exported = await RunExecutableAsync(ToolPath,
                        new[] { "-keyset", "-expcert", certPath, "-container", container, "-keytype", keyType, "-silent" }, token);
                    if (!exported.Success || !File.Exists(certPath)) continue;
                    try
                    {
                        using var certificate = new X509Certificate2(certPath);
                        if (!installed.Contains(certificate.Thumbprint))
                        {
                            var name = certificate.GetNameInfo(X509NameType.SimpleName, false);
                            if (string.IsNullOrWhiteSpace(name)) name = certificate.Subject;
                            result.Add(new ContainerCertificateItem(
                                name,
                                container,
                                keyType,
                                certificate.Thumbprint,
                                certificate.NotAfter,
                                certificate.Subject));
                        }
                        break;
                    }
                    catch
                    {
                        // Ignore a malformed or unsupported certificate and continue scanning.
                    }
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }

        return result
            .GroupBy(x => x.Thumbprint, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.ContainerName)
            .ToList();
    }

    public Task<ProcessResult> InstallContainerCertificateAsync(
        ContainerCertificateItem item,
        CancellationToken token = default)
    {
        if (CertMgrPath is null)
            return Task.FromResult(new ProcessResult(-1, "CryptoPro certmgr.exe не найден."));
        var args = new List<string>
        {
            "-install", "-store", "uMy", "-container", item.ContainerName, "-certificate", "-silent"
        };
        if (item.KeyType == "signature") args.Add("-at_signature");
        return RunExecutableAsync(CertMgrPath, args, token);
    }

    public bool IsCurrentUserCertificateInstalled(string thumbprint) =>
        GetCurrentUserThumbprints().Contains(thumbprint);

    public List<UserPersonalCertificateItem> GetCurrentUserPersonalCertificates()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        return store.Certificates
            .Cast<X509Certificate2>()
            .Select(certificate =>
            {
                var name = certificate.GetNameInfo(X509NameType.SimpleName, false);
                if (string.IsNullOrWhiteSpace(name)) name = certificate.Subject;
                return new UserPersonalCertificateItem(
                    name,
                    certificate.Thumbprint,
                    certificate.NotBefore,
                    certificate.NotAfter,
                    certificate.Subject,
                    certificate.Issuer,
                    certificate.HasPrivateKey);
            })
            .GroupBy(certificate => certificate.Thumbprint, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(certificate => certificate.IsExpired ? 0 : certificate.IsNotYetValid ? 1 : 2)
            .ThenBy(certificate => certificate.NotAfter)
            .ThenBy(certificate => certificate.DisplayName)
            .ToList();
    }

    public int RemoveCurrentUserPersonalCertificate(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            throw new ArgumentException("Не указан отпечаток сертификата.", nameof(thumbprint));

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false)
            .Cast<X509Certificate2>()
            .ToArray();
        foreach (var certificate in matches)
            store.Remove(certificate);
        return matches.Length;
    }

    public List<CertificateItem> GetCertificates()
    {
        var result = new List<CertificateItem>();
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                foreach (var cert in store.Certificates)
                {
                    var name = cert.GetNameInfo(X509NameType.SimpleName, false);
                    if (string.IsNullOrWhiteSpace(name)) name = cert.Subject;
                    result.Add(new CertificateItem(name, cert.Thumbprint, location, cert.NotAfter, cert.HasPrivateKey));
                }
            }
            catch
            {
                // A locked machine store must not prevent use of the user store.
            }
        }
        return result
            .GroupBy(x => $"{x.StoreLocation}:{x.Thumbprint}")
            .Select(x => x.First())
            .Where(x => x.HasPrivateKey && x.NotAfter > DateTime.Now)
            .OrderByDescending(x => x.HasPrivateKey)
            .ThenBy(x => x.DisplayName)
            .ToList();
    }

    public Task<ProcessResult> VerifyDetachedAsync(string contentPath, string signaturePath, CancellationToken token = default)
    {
        var args = new List<string> { "-sfsign", "-verify", "-detached", "-in", contentPath, "-signature", signaturePath };
        if (CmsMerger.IsBase64File(signaturePath)) args.Add("-base64");
        args.Add("-silent");
        return RunAsync(args, token);
    }

    public Task<ProcessResult> VerifyAttachedAsync(string signaturePath, CancellationToken token = default)
    {
        var args = new List<string> { "-sfsign", "-verify", "-in", signaturePath };
        if (CmsMerger.IsBase64File(signaturePath)) args.Add("-base64");
        args.Add("-silent");
        return RunAsync(args, token);
    }

    public Task<ProcessResult> SignAsync(
        string inputPath,
        string outputPath,
        CertificateItem certificate,
        string algorithm,
        bool detached,
        bool base64,
        CancellationToken token = default)
    {
        var storeFlag = certificate.StoreLocation == StoreLocation.CurrentUser ? "-my" : "-MY";
        var args = new List<string>
        {
            "-sfsign", "-sign", "-in", inputPath, "-out", outputPath,
            storeFlag, certificate.Thumbprint, "-alg", algorithm, "-add", "-addsigtime", "-cades_strict", "-ask"
        };
        if (detached) args.Add("-detached");
        if (base64) args.Add("-base64");
        return RunAsync(args, token);
    }

    private async Task<ProcessResult> RunAsync(IEnumerable<string> arguments, CancellationToken token)
    {
        if (ToolPath is null)
            return new ProcessResult(-1, "CryptoPro CSP не найден. Установите CryptoPro CSP 5 и повторите операцию.");

        return await RunExecutableAsync(ToolPath, arguments, token);
    }

    private static async Task<ProcessResult> RunExecutableAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = SafeOemEncoding(),
            StandardErrorEncoding = SafeOemEncoding()
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        return new ProcessResult(process.ExitCode, (await stdout) + (await stderr));
    }

    private static HashSet<string> GetCurrentUserThumbprints()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        return store.Certificates.Select(x => x.Thumbprint).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Encoding SafeOemEncoding()
    {
        try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch { return Encoding.UTF8; }
    }

    private static string? FindTool() => FindExecutable("csptest.exe");

    private static string? FindExecutable(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Crypto Pro", "CSP", fileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Crypto Pro", "CSP", fileName)
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator)
            .Select(folder => Path.Combine(folder.Trim(), fileName))
            .FirstOrDefault(File.Exists);
    }
}
