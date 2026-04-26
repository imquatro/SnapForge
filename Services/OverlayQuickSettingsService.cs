using System.IO;
using System.Text.Json;
using SnapForge.Models;

namespace SnapForge.Services;

public sealed class OverlayQuickSettingsService
{
    private readonly string _path;

    public OverlayQuickSettingsService()
    {
        string baseDir = AppContext.BaseDirectory;
        string installConfigDir = Path.Combine(baseDir, "config");
        _path = ResolveWritablePath(installConfigDir);
    }

    public OverlayQuickSettings Load()
    {
        if (!File.Exists(_path))
        {
            OverlayQuickSettings defaults = new();
            Save(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(_path);
            OverlayQuickSettings? parsed = JsonSerializer.Deserialize<OverlayQuickSettings>(json);
            return parsed ?? new OverlayQuickSettings();
        }
        catch
        {
            return new OverlayQuickSettings();
        }
    }

    public void Save(OverlayQuickSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    private static string ResolveWritablePath(string preferredDir)
    {
        try
        {
            Directory.CreateDirectory(preferredDir);
            return Path.Combine(preferredDir, "overlay_quick.json");
        }
        catch
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string fallbackDir = Path.Combine(appData, "SnapForge");
            Directory.CreateDirectory(fallbackDir);
            return Path.Combine(fallbackDir, "overlay_quick.json");
        }
    }
}
