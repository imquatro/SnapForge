using System.Text.Json;
using System.IO;
using SnapForge.Models;
using SnapForge.Services;

namespace SnapForge.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        string baseDir = AppContext.BaseDirectory;
        string installConfigDir = Path.Combine(baseDir, "config");
        _settingsPath = ResolveWritableSettingsPath(installConfigDir);
    }

    public UserSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            UserSettings defaults = BuildDefaultSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);
            UserSettings? parsed = JsonSerializer.Deserialize<UserSettings>(json);
            if (parsed is null)
            {
                return BuildDefaultSettings();
            }

            if (string.IsNullOrWhiteSpace(parsed.SaveDirectory))
            {
                parsed.SaveDirectory = GetDefaultSaveDirectory();
            }

            MigrateHotkeyTextIfNeeded(parsed);
            Directory.CreateDirectory(parsed.SaveDirectory);
            return parsed;
        }
        catch
        {
            UserSettings defaults = BuildDefaultSettings();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(UserSettings settings)
    {
        Directory.CreateDirectory(settings.SaveDirectory);
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }

    private static UserSettings BuildDefaultSettings()
    {
        return new UserSettings
        {
            SaveDirectory = GetDefaultSaveDirectory(),
            StartWithWindows = true,
            HotkeyComboText = "Shift+F11",
            OverlayHotkeyComboText = "Shift+F12",
            HotkeyModifiers = 4,
            HotkeyVirtualKey = 0x7A,
            OverlayHotkeyModifiers = 4,
            OverlayHotkeyVirtualKey = 0x7B,
            NotificationDurationMs = 1800,
            GalleryThumbSize = 170,
            AskBeforeDeleteImage = true
        };
    }

    private static void MigrateHotkeyTextIfNeeded(UserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.HotkeyComboText))
        {
            settings.HotkeyComboText = HotkeyParser.FormatFromWin32(settings.HotkeyModifiers, settings.HotkeyVirtualKey);
        }

        if (string.IsNullOrWhiteSpace(settings.OverlayHotkeyComboText))
        {
            settings.OverlayHotkeyComboText = HotkeyParser.FormatFromWin32(settings.OverlayHotkeyModifiers, settings.OverlayHotkeyVirtualKey);
        }
    }

    private static string GetDefaultSaveDirectory()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return Path.Combine(pictures, "SnapForge");
    }

    private static string ResolveWritableSettingsPath(string preferredDir)
    {
        try
        {
            Directory.CreateDirectory(preferredDir);
            string preferredPath = Path.Combine(preferredDir, "settings.json");

            if (!File.Exists(preferredPath))
            {
                File.WriteAllText(preferredPath, "{}");
            }

            return preferredPath;
        }
        catch
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string fallbackDir = Path.Combine(appData, "SnapForge");
            Directory.CreateDirectory(fallbackDir);
            return Path.Combine(fallbackDir, "settings.json");
        }
    }
}
