using Microsoft.Win32;
using System.Runtime.Versioning;

namespace CodexBar.Runtime;

[SupportedOSPlatform("windows")]
public sealed class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexBarWin";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

        if (enabled)
        {
            key.SetValue(ValueName, $"{Quote(executablePath)} --tray-only");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string Quote(string path)
        => path.Contains(' ') ? $"\"{path}\"" : path;
}
