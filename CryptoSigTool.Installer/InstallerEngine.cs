using System.Reflection;
using Microsoft.Win32;

namespace CryptoSigTool.Installer;

internal static class InstallerEngine
{
    private const string ResourceMarker = ".Bundle.";

    public static IReadOnlyList<string> GetPayloadNames() => typeof(InstallerEngine).Assembly
        .GetManifestResourceNames()
        .Where(x => x.Contains(ResourceMarker, StringComparison.Ordinal))
        .Select(x => x[(x.IndexOf(ResourceMarker, StringComparison.Ordinal) + ResourceMarker.Length)..])
        .OrderBy(x => x)
        .ToArray();

    public static void Install(string installDirectory, bool desktopShortcut)
    {
        installDirectory = Path.GetFullPath(installDirectory);
        Directory.CreateDirectory(installDirectory);
        ExtractPayload(installDirectory);

        var currentExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь установщика.");
        var uninstaller = Path.Combine(installDirectory, "Uninstall.exe");
        File.Copy(currentExecutable, uninstaller, true);

        var application = Path.Combine(installDirectory, "CryptoSigTool.exe");
        if (!File.Exists(application)) throw new InvalidDataException("В установщике отсутствует CryptoSigTool.exe.");
        ShellShortcut.Create(InstallPaths.StartMenuShortcut, application, installDirectory, "Электронные подписи CMS/PKCS#7 через CryptoPro CSP");
        if (desktopShortcut)
            ShellShortcut.Create(InstallPaths.DesktopShortcut, application, installDirectory, "CryptoSigTool");

        using var key = Registry.LocalMachine.CreateSubKey(InstallPaths.UninstallRegistryKey, true)
            ?? throw new InvalidOperationException("Не удалось создать запись деинсталлятора.");
        key.SetValue("DisplayName", InstallPaths.ProductName);
        key.SetValue("DisplayVersion", InstallPaths.Version);
        key.SetValue("Publisher", InstallPaths.Publisher);
        key.SetValue("DisplayIcon", application);
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("URLInfoAbout", InstallPaths.RepositoryUrl);
        key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateInstalledSize(installDirectory), RegistryValueKind.DWord);
    }

    private static void ExtractPayload(string installDirectory)
    {
        var assembly = typeof(InstallerEngine).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(x => x.Contains(ResourceMarker, StringComparison.Ordinal)))
        {
            var fileName = resourceName[(resourceName.IndexOf(ResourceMarker, StringComparison.Ordinal) + ResourceMarker.Length)..];
            var destination = Path.Combine(installDirectory, fileName);
            using var source = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Не найден ресурс {resourceName}.");
            using var target = File.Create(destination);
            source.CopyTo(target);
        }
    }

    private static int EstimateInstalledSize(string directory)
    {
        var bytes = Directory.EnumerateFiles(directory).Sum(x => new FileInfo(x).Length);
        return (int)Math.Min(int.MaxValue, Math.Max(1, bytes / 1024));
    }
}
