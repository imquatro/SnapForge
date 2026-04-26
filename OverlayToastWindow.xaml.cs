using System.Windows;
using System.Windows.Media.Imaging;

namespace SnapForge;

public partial class OverlayToastWindow : Window
{
    public OverlayToastWindow(string filePath, string overlayShortcutText)
    {
        InitializeComponent();
        HintText.Text = $"Press {overlayShortcutText} for overlay";
        try
        {
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
        }
        catch
        {
            PreviewImage.Source = null;
        }
    }
}
