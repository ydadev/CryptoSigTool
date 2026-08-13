using System.Diagnostics;
using Microsoft.Win32;

namespace CryptoSigTool.Installer;

internal static class Uninstaller
{
    public static int Begin()
    {
        var installDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? InstallPaths.DefaultInstallDirectory;
        var temporaryCopy = Path.Combine(Path.GetTempPath(), $"CryptoSigTool-Uninstall-{Guid.NewGuid():N}.exe");
        File.Copy(Environment.ProcessPath!, temporaryCopy, true);
        Process.Start(new ProcessStartInfo
        {
            FileName = temporaryCopy,
            UseShellExecute = true,
            ArgumentList = { "--uninstall-final", installDirectory }
        });
        return 0;
    }

    public static int Run(string installDirectory)
    {
        var answer = MessageBox.Show(
            $"Удалить CryptoSigTool и все файлы программы из:\r\n{installDirectory}?",
            "Удаление CryptoSigTool", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return 0;

        try
        {
            foreach (var process in Process.GetProcessesByName("CryptoSigTool"))
            {
                try { process.CloseMainWindow(); process.WaitForExit(3000); } catch { }
            }
            DeleteIfExists(InstallPaths.StartMenuShortcut);
            DeleteIfExists(InstallPaths.DesktopShortcut);
            Registry.LocalMachine.DeleteSubKeyTree(InstallPaths.UninstallRegistryKey, false);
            if (Directory.Exists(installDirectory)) Directory.Delete(installDirectory, true);
            MessageBox.Show("CryptoSigTool удалён.", "Удаление CryptoSigTool", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось полностью удалить CryptoSigTool.\r\n\r\n" + ex.Message,
                "Удаление CryptoSigTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
