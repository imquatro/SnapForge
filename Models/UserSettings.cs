namespace SnapForge.Models;

public sealed class UserSettings
{
    public string SaveDirectory { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; } = true;
    public string HotkeyComboText { get; set; } = "Shift+F11";
    public string OverlayHotkeyComboText { get; set; } = "Shift+F12";
    public int HotkeyModifiers { get; set; } = 4;
    public int HotkeyVirtualKey { get; set; } = 0x7A;
    public int OverlayHotkeyModifiers { get; set; } = 4;
    public int OverlayHotkeyVirtualKey { get; set; } = 0x7B;
    public int NotificationDurationMs { get; set; } = 1800;
    public int GalleryThumbSize { get; set; } = 170;
    public bool AskBeforeDeleteImage { get; set; } = true;
}
