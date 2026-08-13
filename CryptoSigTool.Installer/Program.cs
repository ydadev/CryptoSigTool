using System.Diagnostics;

namespace CryptoSigTool.Installer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            return Uninstaller.Begin();
        if (args.Contains("--uninstall-final", StringComparer.OrdinalIgnoreCase))
        {
            var index = Array.FindIndex(args, x => x.Equals("--uninstall-final", StringComparison.OrdinalIgnoreCase));
            return Uninstaller.Run(index >= 0 && index + 1 < args.Length ? args[index + 1] : InstallPaths.DefaultInstallDirectory);
        }
        if (args.Contains("--installer-smoke", StringComparer.OrdinalIgnoreCase))
        {
            var payload = InstallerEngine.GetPayloadNames();
            Console.WriteLine($"OK installer payload={payload.Count}: {string.Join(", ", payload)}");
            return payload.Contains("CryptoSigTool.exe", StringComparer.OrdinalIgnoreCase) ? 0 : 1;
        }

        Application.Run(new InstallerForm());
        return 0;
    }
}
