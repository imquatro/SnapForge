using System.Runtime.InteropServices;
using System.IO;
using Forms = System.Windows.Forms;

namespace SnapForge.Services;

public sealed class SendAutomationService
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SwRestore = 9;

    public bool TrySendImageToWindow(IntPtr handle, string filePath)
    {
        if (handle == IntPtr.Zero || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            ShowWindow(handle, SwRestore);
            SetForegroundWindow(handle);
            Forms.Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { filePath });

            // Best-effort global paste into the active input/chat box.
            Forms.SendKeys.SendWait("^v");
            Thread.Sleep(120);
            Forms.SendKeys.SendWait("{ENTER}");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
