using Microsoft.Win32;

namespace SnapForge.Services;

public sealed class StartupService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SnapForge";

    public void SetStartup(bool enabled)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            string exePath = Environment.ProcessPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                key.SetValue(AppName, $"\"{exePath}\"");
            }
            return;
        }

        key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
