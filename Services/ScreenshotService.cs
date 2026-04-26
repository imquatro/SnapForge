using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace SnapForge.Services;

public sealed class ScreenshotService
{
    public string CaptureTo(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);

        Rectangle bounds = Screen.AllScreens
            .Select(screen => screen.Bounds)
            .Aggregate(Rectangle.Union);

        using Bitmap bitmap = new(bounds.Width, bounds.Height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);

        string fileName = $"SnapForge_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string filePath = Path.Combine(directoryPath, fileName);
        bitmap.Save(filePath, ImageFormat.Png);

        return filePath;
    }
}
