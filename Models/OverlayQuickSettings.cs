namespace SnapForge.Models;

public sealed class OverlayQuickSettings
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Opacity { get; set; } = 0.92;
    public bool AutoDismiss { get; set; } = true;
    public int AutoDismissMs { get; set; } = 8000;
}
