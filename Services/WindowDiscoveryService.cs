using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapForge.Services;

public sealed class WindowDiscoveryService
{
    public sealed class OpenWindowInfo
    {
        public required IntPtr Handle { get; init; }
        public required string Title { get; init; }
        public required string ProcessName { get; init; }
        public required ImageSource IconSource { get; init; }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public IReadOnlyList<OpenWindowInfo> GetOpenWindows()
    {
        List<OpenWindowInfo> windows = [];
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            System.Text.StringBuilder titleBuilder = new(256);
            GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out uint processId);
            string processName = "Unknown";
            try
            {
                using Process process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
                // ignore process lookup errors
            }

            windows.Add(new OpenWindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessName = processName,
                IconSource = GetWindowIcon(processId)
            });

            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(x => x.ProcessName)
            .ThenBy(x => x.Title)
            .ToList();
    }

    private static ImageSource GetWindowIcon(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string? exePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return BuildFallbackIcon();
            }

            using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
            {
                return BuildFallbackIcon();
            }

            IntPtr hIcon = icon.Handle;
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            _ = DestroyIcon(hIcon);
            return source;
        }
        catch
        {
            return BuildFallbackIcon();
        }
    }

    private static ImageSource BuildFallbackIcon()
    {
        DrawingVisual visual = new();
        using DrawingContext dc = visual.RenderOpen();
        dc.DrawRoundedRectangle(
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 64, 96)),
            null,
            new System.Windows.Rect(0, 0, 32, 32),
            6,
            6);
        RenderTargetBitmap bmp = new(32, 32, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
