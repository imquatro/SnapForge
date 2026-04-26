using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SnapForge.Services;

public sealed class WindowDiscoveryService
{
    public sealed class OpenWindowInfo
    {
        public required IntPtr Handle { get; init; }
        public required string Title { get; init; }
        public required string ProcessName { get; init; }
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
                ProcessName = processName
            });

            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(x => x.ProcessName)
            .ThenBy(x => x.Title)
            .ToList();
    }
}
