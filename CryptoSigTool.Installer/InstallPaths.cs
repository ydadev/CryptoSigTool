namespace CryptoSigTool.Installer;

internal static class InstallPaths
{
    public const string ProductName = "CryptoSigTool";
    public const string Version = "1.4.0";
    public const string Publisher = "ydadev";
    public const string RepositoryUrl = "https://github.com/ydadev/CryptoSigTool";
    public const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CryptoSigTool";

    public static string DefaultInstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);

    public static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ProductName + ".lnk");

    public static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ProductName + ".lnk");
}
