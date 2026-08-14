using Microsoft.Win32;

namespace CryptoSigTool;

internal static class TspServiceStore
{
    private const string RegistryPath = @"Software\CryptoSigTool";
    private const string ValueName = "TspServices";

    public static string[] Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            return (key?.GetValue(ValueName) as string[] ?? Array.Empty<string>())
                .Where(IsValid)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void Remember(string address)
    {
        if (!IsValid(address)) return;
        try
        {
            var values = Load()
                .Append(address.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .TakeLast(20)
                .ToArray();
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            key?.SetValue(ValueName, values, RegistryValueKind.MultiString);
        }
        catch
        {
            // The signature is already valid; inability to save UI history is non-fatal.
        }
    }

    private static bool IsValid(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
