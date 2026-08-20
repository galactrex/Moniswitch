using Microsoft.Win32;
using System.Security;

namespace Moniswitch;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Moniswitch";

    public static void Apply(bool enabled)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !string.Equals(Path.GetFileName(executablePath), "Moniswitch.exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                key.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            // Startup registration is a convenience. A policy-managed registry
            // must not prevent display routing from starting.
        }
    }

    internal static string BuildCommand(string executablePath) => $"\"{executablePath}\"";
}
