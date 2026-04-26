using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SnapForge.Services;

public sealed class SendAutomationService
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SwRestore = 9;
    private const int SwMaximize = 3;

    public bool TrySendImageToWindow(IntPtr handle, string filePath)
    {
        if (handle == IntPtr.Zero || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            if (IsIconic(handle))
            {
                ShowWindow(handle, SwRestore);
            }
            ShowWindow(handle, SwMaximize);
            SetForegroundWindow(handle);
            using FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            System.Windows.Clipboard.SetImage(bitmap);
            Forms.SendKeys.SendWait("^v");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
