using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using System.Net.Http;
using System.Reflection;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using SnapForge.Models;
using SnapForge.Services;

namespace SnapForge;

public partial class MainWindow : Window
{
    private const string UpdateRepoOwner = "imquatro";
    private const string UpdateRepoName = "SnapForge";
    private const int CaptureHotkeyId = 7000;
    private const int OverlayHotkeyId = 7001;
    private readonly SettingsService _settingsService = new();
    private readonly ScreenshotService _screenshotService = new();
    private readonly StartupService _startupService = new();
    private readonly UserSettings _settings;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly ObservableCollection<GalleryItemView> _galleryItems = [];
    private readonly MediaPlayer _toastSoundPlayer = new();
    private readonly List<string> _viewerFolderImages = [];
    private static readonly HttpClient _httpClient = new();
    private readonly Stack<UIElement> _editorUndoStack = [];
    private bool _allowClose;
    private HwndSource? _hwndSource;
    private OverlayToastWindow? _captureToast;
    private string? _selectedImagePath;
    private int _viewerIndex;
    private double _viewerZoom = 1;
    private bool _isViewerPanning;
    private System.Windows.Point _viewerPanStart;
    private System.Windows.Point _viewerPanOrigin;
    private double _editorZoom = 1;
    private bool _isEditorPanning;
    private bool _isEditorDrawing;
    private System.Windows.Point _editorPanStart;
    private System.Windows.Point _editorPanOrigin;
    private System.Windows.Shapes.Polyline? _activeEditorPolyline;
    private System.Windows.Media.Color _editorColor = Colors.OrangeRed;
    private bool _editorCutMode;
    private bool _editorCropDragging;
    private bool _editorCropDragMoved;
    private System.Windows.Point _editorCropDragStart;
    private System.Windows.Point _editorCropStartOrigin;
    private double _editorSaturation = 1;
    private double _editorValue = 1;
    private OverlayHubWindow? _overlayHubWindow;
    private string? _latestReleaseUrl;
    private string? _latestInstallerDownloadUrl;
    private bool _isCheckingUpdates;
    private bool _isUpdatingInBackground;
    private System.Windows.Threading.DispatcherTimer? _hotkeyRetryTimer;
    private LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _keyboardHookHandle = IntPtr.Zero;
    private bool _captureHotkeyPressed;
    private bool _overlayHotkeyPressed;
    private bool _hotkeysRegisteredSuccessfully;
    private long _lastCaptureTicks;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10;

    public MainWindow()
    {
        _settings = _settingsService.Load();
        InitializeComponent();
        TryApplyWindowIcon();
        _trayIcon = BuildTrayIcon();
        LoadSettingsIntoUI();
        GalleryItemsControl.ItemsSource = _galleryItems;
        RefreshGalleryItems();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        EnsureHotkeySettingsValid();
        _hwndSource = (HwndSource?)PresentationSource.FromVisual(this);
        if (_hwndSource is null)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                _hwndSource = HwndSource.FromHwnd(handle);
            }
        }

        if (_hwndSource is null)
        {
            HotkeyStatusText.Text = "Hotkey init failed (window handle).";
            return;
        }

        _hwndSource.AddHook(WndProc);
        TryRegisterAllHotkeysWithRetry();
        InstallKeyboardHookFallback();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DockRightHalf();

        if (_settings.StartWithWindows)
        {
            _startupService.SetStartup(true);
        }

        _ = CheckUpdatesAsync(isManual: false);

        WindowState = WindowState.Minimized;
        Hide();
    }

    private void DockRightHalf()
    {
        Rect work = SystemParameters.WorkArea;
        WindowState = WindowState.Normal;
        Width = Math.Max(MinWidth, work.Width / 2);
        Height = work.Height;
        Left = work.Right - Width;
        Top = work.Top;
    }

    private void LoadSettingsIntoUI()
    {
        SaveDirectoryTextBox.Text = _settings.SaveDirectory;
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        ThumbSizeSlider.Value = _settings.GalleryThumbSize;
        NotificationDurationSlider.Value = _settings.NotificationDurationMs;
        NotificationDurationText.Text = $"{_settings.NotificationDurationMs} ms";
        CaptureHotkeyTextBox.Text = ExtractKeyToken(_settings.HotkeyComboText);
        OverlayHotkeyTextBox.Text = ExtractKeyToken(_settings.OverlayHotkeyComboText);
        EditorHueSlider.Value = 15;
        EditorColorPreview.Background = new SolidColorBrush(_editorColor);
        ViewerZoomText.Text = "100%";
        CurrentVersionText.Text = $"Current version: {GetCurrentAppVersion()}";
        UpdateProgressBar.Value = 0;
        UpdateProgressText.Text = "Progress: 0%";
        SetUpdateProgressVisible(false);
        UpdateEditorColorPlaneVisuals();
    }

    private Forms.NotifyIcon BuildTrayIcon()
    {
        Drawing.Icon trayResolvedIcon = ResolveExecutableIcon();
        Forms.NotifyIcon tray = new()
        {
            Text = "SnapForge Capture",
            Icon = trayResolvedIcon,
            Visible = true
        };
        Forms.ContextMenuStrip menu = new();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add("Take Screenshot", null, (_, _) => CaptureScreenshot());
        menu.Items.Add("Open Panel", null, (_, _) => OpenRecentTab());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowFromTray();
        return tray;
    }

    private void TryApplyWindowIcon()
    {
        try
        {
            Drawing.Icon exeIcon = ResolveExecutableIcon();
            if (exeIcon.Handle == IntPtr.Zero)
            {
                return;
            }

            Icon = Imaging.CreateBitmapSourceFromHIcon(
                exeIcon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
        }
        catch
        {
        }
    }

    private static Drawing.Icon ResolveExecutableIcon()
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                Drawing.Icon? extracted = Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (extracted is not null)
                {
                    return (Drawing.Icon)extracted.Clone();
                }
            }
        }
        catch
        {
        }

        return Drawing.SystemIcons.Application;
    }

    private void ShowFromTray()
    {
        Show();
        TryRegisterAllHotkeysWithRetry();
        DockRightHalf();
        Activate();
    }

    public void ShowFromExternalActivation()
    {
        ShowFromTray();
        MainTabControl.SelectedIndex = 0;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            UninstallKeyboardHookFallback();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            if (_hwndSource is not null)
            {
                UnregisterHotKey(_hwndSource.Handle, CaptureHotkeyId);
                UnregisterHotKey(_hwndSource.Handle, OverlayHotkeyId);
                _hwndSource.RemoveHook(WndProc);
            }
            System.Windows.Application.Current.Shutdown();
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
        {
            DockRightHalf();
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseToTrayButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void CaptureButton_Click(object sender, RoutedEventArgs e) => CaptureScreenshot();

    private void CaptureScreenshot()
    {
        long now = Environment.TickCount64;
        if (now - _lastCaptureTicks < 500)
        {
            return;
        }

        _lastCaptureTicks = now;

        try
        {
            string filePath = _screenshotService.CaptureTo(_settings.SaveDirectory);
            CaptureResultText.Text = $"Saved: {Path.GetFileName(filePath)}";
            RefreshGalleryItems();
            SetSelectedImage(filePath);
            ShowCaptureToast(filePath);
        }
        catch
        {
            CaptureResultText.Text = "Capture failed. Check folder permissions.";
        }
    }

    private void OpenRecentTab()
    {
        ShowFromTray();
        MainTabControl.SelectedIndex = 0;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using Forms.FolderBrowserDialog dialog = new();
        dialog.Description = "Choose screenshot save folder";
        dialog.UseDescriptionForTitle = true;
        dialog.InitialDirectory = _settings.SaveDirectory;
        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        _settings.SaveDirectory = dialog.SelectedPath;
        SaveDirectoryTextBox.Text = _settings.SaveDirectory;
        _settingsService.Save(_settings);
    }

    private void ApplyHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        string captureInput = NormalizeShiftComboInput(CaptureHotkeyTextBox.Text);
        string overlayInput = NormalizeShiftComboInput(OverlayHotkeyTextBox.Text);
        if (!HotkeyParser.TryParseCombination(captureInput, out int captureMods, out int captureVk, out string captureNorm) ||
            !HotkeyParser.TryParseCombination(overlayInput, out int overlayMods, out int overlayVk, out string overlayNorm))
        {
            HotkeyStatusText.Text = "Invalid hotkey format. Example: F11 (Shift is added automatically).";
            return;
        }

        if (captureVk == overlayVk && captureMods == overlayMods)
        {
            HotkeyStatusText.Text = "Capture and overlay hotkeys must be different.";
            return;
        }

        _settings.HotkeyComboText = captureNorm;
        _settings.OverlayHotkeyComboText = overlayNorm;
        _settings.HotkeyModifiers = captureMods;
        _settings.HotkeyVirtualKey = captureVk;
        _settings.OverlayHotkeyModifiers = overlayMods;
        _settings.OverlayHotkeyVirtualKey = overlayVk;
        CaptureHotkeyTextBox.Text = ExtractKeyToken(captureNorm);
        OverlayHotkeyTextBox.Text = ExtractKeyToken(overlayNorm);

        if (!TryRegisterAllHotkeys(out string? error))
        {
            HotkeyStatusText.Text = error ?? "Hotkey registration failed.";
            return;
        }

        HotkeyStatusText.Text = "Hotkeys saved.";
        _settingsService.Save(_settings);
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        box.Text = key.ToString();
        e.Handled = true;
    }

    private void HotkeyTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box)
        {
            return;
        }

        box.Text = string.Empty;
    }

    private static string NormalizeShiftComboInput(string raw)
    {
        string value = raw?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Shift+F11";
        }

        if (value.Contains('+'))
        {
            return value;
        }

        return $"Shift+{value}";
    }

    private static string ExtractKeyToken(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo))
        {
            return string.Empty;
        }

        string[] parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? combo : parts[^1];
    }

    private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = StartWithWindowsCheckBox.IsChecked == true;
        _settings.StartWithWindows = enabled;
        _startupService.SetStartup(enabled);
        _settingsService.Save(_settings);
    }

    private bool TryRegisterAllHotkeys(out string? error)
    {
        error = null;
        if (_hwndSource is null)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                _hwndSource = HwndSource.FromHwnd(handle);
            }

            if (_hwndSource is null)
            {
                error = "Hotkey registration failed (no window handle).";
                return false;
            }
        }

        UnregisterHotKey(_hwndSource.Handle, CaptureHotkeyId);
        UnregisterHotKey(_hwndSource.Handle, OverlayHotkeyId);
        bool captureOk = RegisterHotKey(_hwndSource.Handle, CaptureHotkeyId, (uint)_settings.HotkeyModifiers, (uint)_settings.HotkeyVirtualKey);
        bool overlayOk = RegisterHotKey(_hwndSource.Handle, OverlayHotkeyId, (uint)_settings.OverlayHotkeyModifiers, (uint)_settings.OverlayHotkeyVirtualKey);
        if (!captureOk || !overlayOk)
        {
            _hotkeysRegisteredSuccessfully = false;
            error = "Hotkey already used by another app or invalid.";
            return false;
        }

        _hotkeysRegisteredSuccessfully = true;
        return true;
    }

    private void TryRegisterAllHotkeysWithRetry()
    {
        bool ok = TryRegisterAllHotkeys(out string? error);
        if (ok)
        {
            if (!string.IsNullOrWhiteSpace(HotkeyStatusText.Text) &&
                HotkeyStatusText.Text.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                HotkeyStatusText.Text = "Hotkeys active.";
            }

            if (_hotkeyRetryTimer is not null)
            {
                _hotkeyRetryTimer.Stop();
                _hotkeyRetryTimer = null;
            }
            return;
        }

        HotkeyStatusText.Text = error ?? "Hotkey registration failed.";
        if (_hotkeyRetryTimer is not null)
        {
            return;
        }

        _hotkeyRetryTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _hotkeyRetryTimer.Tick += (_, _) =>
        {
            if (TryRegisterAllHotkeys(out string? retryError))
            {
                HotkeyStatusText.Text = "Hotkeys active.";
                _hotkeyRetryTimer?.Stop();
                _hotkeyRetryTimer = null;
            }
            else if (!string.IsNullOrWhiteSpace(retryError))
            {
                HotkeyStatusText.Text = retryError;
            }
        };
        _hotkeyRetryTimer.Start();
    }

    private void InstallKeyboardHookFallback()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            return;
        }

        _keyboardProc = KeyboardHookProc;
        string moduleName = Process.GetCurrentProcess().MainModule?.ModuleName ?? string.Empty;
        IntPtr moduleHandle = GetModuleHandle(moduleName);
        _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);
    }

    private void UninstallKeyboardHookFallback()
    {
        if (_keyboardHookHandle == IntPtr.Zero)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_keyboardHookHandle);
        _keyboardHookHandle = IntPtr.Zero;
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            KbdLlHookStruct data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            int msg = wParam.ToInt32();
            bool keyDown = msg == WmKeyDown || msg == WmSysKeyDown;
            bool keyUp = msg == WmKeyUp || msg == WmSysKeyUp;
            bool shiftHeld = (GetAsyncKeyState(VkShift) & 0x8000) != 0;

            if (keyDown && shiftHeld && !_hotkeysRegisteredSuccessfully)
            {
                int captureVk = _settings.HotkeyVirtualKey;
                int overlayVk = _settings.OverlayHotkeyVirtualKey;
                if ((int)data.vkCode == captureVk && !_captureHotkeyPressed)
                {
                    _captureHotkeyPressed = true;
                    Dispatcher.BeginInvoke(() => CaptureScreenshot(), System.Windows.Threading.DispatcherPriority.Background);
                }
                else if ((int)data.vkCode == overlayVk && !_overlayHotkeyPressed)
                {
                    _overlayHotkeyPressed = true;
                    Dispatcher.BeginInvoke(() => ShowOverlayHub(), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            else if (keyUp)
            {
                if ((int)data.vkCode == _settings.HotkeyVirtualKey)
                {
                    _captureHotkeyPressed = false;
                }

                if ((int)data.vkCode == _settings.OverlayHotkeyVirtualKey)
                {
                    _overlayHotkeyPressed = false;
                }
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private void EnsureHotkeySettingsValid()
    {
        bool changed = false;
        if (!HotkeyParser.TryParseCombination(_settings.HotkeyComboText, out int capMods, out int capVk, out string capNorm))
        {
            capNorm = "Shift+F11";
            HotkeyParser.TryParseCombination(capNorm, out capMods, out capVk, out _);
            changed = true;
        }

        if (!HotkeyParser.TryParseCombination(_settings.OverlayHotkeyComboText, out int ovMods, out int ovVk, out string ovNorm))
        {
            ovNorm = "Shift+F12";
            HotkeyParser.TryParseCombination(ovNorm, out ovMods, out ovVk, out _);
            changed = true;
        }

        if (_settings.HotkeyVirtualKey != capVk || _settings.HotkeyModifiers != capMods || !string.Equals(_settings.HotkeyComboText, capNorm, StringComparison.OrdinalIgnoreCase))
        {
            _settings.HotkeyVirtualKey = capVk;
            _settings.HotkeyModifiers = capMods;
            _settings.HotkeyComboText = capNorm;
            changed = true;
        }

        if (_settings.OverlayHotkeyVirtualKey != ovVk || _settings.OverlayHotkeyModifiers != ovMods || !string.Equals(_settings.OverlayHotkeyComboText, ovNorm, StringComparison.OrdinalIgnoreCase))
        {
            _settings.OverlayHotkeyVirtualKey = ovVk;
            _settings.OverlayHotkeyModifiers = ovMods;
            _settings.OverlayHotkeyComboText = ovNorm;
            changed = true;
        }

        if (changed)
        {
            _settingsService.Save(_settings);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmHotkey = 0x0312;
        if (msg == wmHotkey && wParam.ToInt32() == CaptureHotkeyId)
        {
            CaptureScreenshot();
            handled = true;
        }
        else if (msg == wmHotkey && wParam.ToInt32() == OverlayHotkeyId)
        {
            Dispatcher.BeginInvoke(() => ShowOverlayHub(), System.Windows.Threading.DispatcherPriority.Normal);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void RefreshGalleryItems()
    {
        Directory.CreateDirectory(_settings.SaveDirectory);
        IEnumerable<string> files = Directory.GetFiles(_settings.SaveDirectory, "*.png")
            .OrderByDescending(File.GetCreationTime)
            .Take(120);

        string? preserve = _selectedImagePath;
        _galleryItems.Clear();
        foreach (string file in files)
        {
            _galleryItems.Add(new GalleryItemView
            {
                FilePath = file,
                FileName = Path.GetFileName(file),
                ThumbSize = _settings.GalleryThumbSize,
                Thumbnail = CreateThumb(file),
                IsSelected = false
            });
        }

        string? fallback = _galleryItems.FirstOrDefault()?.FilePath;
        SetSelectedImage(preserve is not null && File.Exists(preserve) ? preserve : fallback);
    }

    private static BitmapImage CreateThumb(string filePath)
    {
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath);
        bitmap.DecodePixelWidth = 260;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void SetSelectedImage(string? filePath)
    {
        _selectedImagePath = filePath;
        foreach (GalleryItemView item in _galleryItems)
        {
            item.IsSelected = string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            LoadViewerFolderImages(filePath);
            LoadViewerCurrentImage();
            LoadEditorImage(filePath);
        }
    }

    private void RecentTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border border || border.DataContext is not GalleryItemView item)
        {
            return;
        }

        SetSelectedImage(item.FilePath);
    }

    private void OpenImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string filePath } || !File.Exists(filePath))
        {
            return;
        }

        SetSelectedImage(filePath);
        MainTabControl.SelectedIndex = 1;
    }

    private void EditImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string filePath } || !File.Exists(filePath))
        {
            return;
        }

        SetSelectedImage(filePath);
        MainTabControl.SelectedIndex = 2;
    }

    private void DeleteImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string filePath } || !File.Exists(filePath))
        {
            return;
        }

        bool shouldDelete = !_settings.AskBeforeDeleteImage;
        if (!shouldDelete)
        {
            DeleteConfirmWindow confirm = new(Path.GetFileName(filePath))
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            confirm.ShowDialog();
            shouldDelete = confirm.ShouldDelete;
            if (confirm.DoNotAskAgain)
            {
                _settings.AskBeforeDeleteImage = false;
                _settingsService.Save(_settings);
            }
        }

        if (!shouldDelete)
        {
            return;
        }

        try
        {
            File.Delete(filePath);
            RefreshGalleryItems();
            CaptureResultText.Text = "Image deleted.";
        }
        catch
        {
            CaptureResultText.Text = "Could not delete this image.";
        }
    }

    private void SendImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string filePath } || !File.Exists(filePath))
        {
            return;
        }

        SetSelectedImage(filePath);
        SendWindow sendWindow = new(filePath)
        {
            Owner = this
        };
        sendWindow.Show();
        CaptureResultText.Text = $"Send opened: {Path.GetFileName(filePath)}";
    }

    private void OpenCaptureFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_settings.SaveDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = _settings.SaveDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
            CaptureResultText.Text = "Could not open capture folder.";
        }
    }

    private void ThumbSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || GalleryItemsControl is null)
        {
            return;
        }

        _settings.GalleryThumbSize = (int)e.NewValue;
        foreach (GalleryItemView item in _galleryItems)
        {
            item.ThumbSize = _settings.GalleryThumbSize;
        }
        GalleryItemsControl.Items.Refresh();
        _settingsService.Save(_settings);
    }

    private void GalleryScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        double next = Math.Clamp(ThumbSizeSlider.Value + (e.Delta > 0 ? 8 : -8), ThumbSizeSlider.Minimum, ThumbSizeSlider.Maximum);
        ThumbSizeSlider.Value = next;
        e.Handled = true;
    }

    private void LoadViewerFolderImages(string filePath)
    {
        _viewerFolderImages.Clear();
        string? folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            _viewerFolderImages.Add(filePath);
            _viewerIndex = 0;
            return;
        }

        _viewerFolderImages.AddRange(Directory.GetFiles(folder, "*.png").OrderByDescending(File.GetCreationTime));
        _viewerIndex = Math.Max(0, _viewerFolderImages.FindIndex(x => string.Equals(x, filePath, StringComparison.OrdinalIgnoreCase)));
    }

    private void LoadViewerCurrentImage()
    {
        if (_viewerFolderImages.Count == 0)
        {
            return;
        }

        string path = _viewerFolderImages[_viewerIndex];
        ViewerImage.Source = LoadBitmapSafe(path);
        ApplyViewerZoom(1, true);
    }

    private static BitmapImage LoadBitmapSafe(string filePath)
    {
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void ViewerPrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewerFolderImages.Count == 0)
        {
            return;
        }

        _viewerIndex = (_viewerIndex - 1 + _viewerFolderImages.Count) % _viewerFolderImages.Count;
        string path = _viewerFolderImages[_viewerIndex];
        SetSelectedImage(path);
    }

    private void ViewerNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewerFolderImages.Count == 0)
        {
            return;
        }

        _viewerIndex = (_viewerIndex + 1) % _viewerFolderImages.Count;
        string path = _viewerFolderImages[_viewerIndex];
        SetSelectedImage(path);
    }

    private void ViewerZoomInButton_Click(object sender, RoutedEventArgs e) => ApplyViewerZoom(_viewerZoom * 1.12, false);
    private void ViewerZoomOutButton_Click(object sender, RoutedEventArgs e) => ApplyViewerZoom(_viewerZoom / 1.12, false);

    private void ApplyViewerZoom(double zoom, bool resetPan)
    {
        _viewerZoom = Math.Clamp(zoom, 0.2, 6);
        ViewerScaleTransform.ScaleX = _viewerZoom;
        ViewerScaleTransform.ScaleY = _viewerZoom;
        if (resetPan)
        {
            ViewerTranslateTransform.X = 0;
            ViewerTranslateTransform.Y = 0;
        }
        ClampViewerPan();
        ViewerZoomText.Text = $"{(int)(_viewerZoom * 100)}%";
    }

    private void ClampViewerPan()
    {
        double contentWidth = Math.Max(1, ViewerImage.ActualWidth * ViewerScaleTransform.ScaleX);
        double contentHeight = Math.Max(1, ViewerImage.ActualHeight * ViewerScaleTransform.ScaleY);
        double minX = Math.Min(0, ViewerViewportHost.ActualWidth - contentWidth);
        double minY = Math.Min(0, ViewerViewportHost.ActualHeight - contentHeight);
        double maxX = Math.Max(0, (ViewerViewportHost.ActualWidth - contentWidth) / 2);
        double maxY = Math.Max(0, (ViewerViewportHost.ActualHeight - contentHeight) / 2);
        ViewerTranslateTransform.X = Math.Clamp(ViewerTranslateTransform.X, minX, maxX);
        ViewerTranslateTransform.Y = Math.Clamp(ViewerTranslateTransform.Y, minY, maxY);
    }

    private void ViewerViewportHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isViewerPanning = true;
        _viewerPanStart = e.GetPosition(ViewerViewportHost);
        _viewerPanOrigin = new System.Windows.Point(ViewerTranslateTransform.X, ViewerTranslateTransform.Y);
        ViewerViewportHost.CaptureMouse();
    }

    private void ViewerViewportHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isViewerPanning || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        System.Windows.Point current = e.GetPosition(ViewerViewportHost);
        ViewerTranslateTransform.X = _viewerPanOrigin.X + (current.X - _viewerPanStart.X);
        ViewerTranslateTransform.Y = _viewerPanOrigin.Y + (current.Y - _viewerPanStart.Y);
        ClampViewerPan();
    }

    private void ViewerViewportHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isViewerPanning = false;
        ViewerViewportHost.ReleaseMouseCapture();
    }

    private void ViewerViewportHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        ApplyViewerZoom(_viewerZoom * (e.Delta > 0 ? 1.12 : 1 / 1.12), false);
        e.Handled = true;
    }

    private void ViewerViewportHost_SizeChanged(object sender, SizeChangedEventArgs e) => ClampViewerPan();
    private void ViewerEditButton_Click(object sender, RoutedEventArgs e) => MainTabControl.SelectedIndex = 2;
    private void ViewerDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedImagePath is null)
        {
            return;
        }
        DeleteImageByPath(_selectedImagePath);
    }

    private void DeleteImageByPath(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
            RefreshGalleryItems();
        }
        catch
        {
            CaptureResultText.Text = "Could not delete this image.";
        }
    }

    private void LoadEditorImage(string filePath)
    {
        EditorBaseImage.Source = LoadBitmapSafe(filePath);
        EditorDrawLayer.Children.Clear();
        _editorUndoStack.Clear();
        _editorZoom = 1;
        ApplyEditorZoom(1, true);
    }

    private void EditorHueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || EditorColorPreview is null || EditorHueSlider is null)
        {
            return;
        }

        _editorColor = HsvToRgb(EditorHueSlider.Value, _editorSaturation, _editorValue);
        EditorColorPreview.Background = new SolidColorBrush(_editorColor);
        UpdateEditorColorPlaneVisuals();
    }

    private void EditorColorPlane_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SetEditorColorFromPlanePoint(e.GetPosition(EditorColorPlane));
        EditorColorPlane.CaptureMouse();
        e.Handled = true;
    }

    private void EditorColorPlane_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (EditorColorPlane.IsMouseCaptured)
        {
            EditorColorPlane.ReleaseMouseCapture();
        }
        e.Handled = true;
    }

    private void EditorColorPlane_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !EditorColorPlane.IsMouseCaptured)
        {
            return;
        }

        SetEditorColorFromPlanePoint(e.GetPosition(EditorColorPlane));
        e.Handled = true;
    }

    private void SetEditorColorFromPlanePoint(System.Windows.Point point)
    {
        if (EditorColorPlane is null || EditorColorPlaneMarker is null || EditorHueSlider is null || EditorColorPreview is null)
        {
            return;
        }

        if (EditorColorPlane.ActualWidth < 2 || EditorColorPlane.ActualHeight < 2)
        {
            return;
        }

        double x = Math.Clamp(point.X, 0, EditorColorPlane.ActualWidth);
        double y = Math.Clamp(point.Y, 0, EditorColorPlane.ActualHeight);
        _editorSaturation = x / EditorColorPlane.ActualWidth;
        _editorValue = 1 - (y / EditorColorPlane.ActualHeight);
        EditorColorPlaneMarker.Margin = new Thickness(
            x - (EditorColorPlaneMarker.Width / 2),
            y - (EditorColorPlaneMarker.Height / 2),
            0,
            0);
        _editorColor = HsvToRgb(EditorHueSlider.Value, _editorSaturation, _editorValue);
        EditorColorPreview.Background = new SolidColorBrush(_editorColor);
    }

    private void UpdateEditorColorPlaneVisuals()
    {
        if (EditorColorPlaneHueBase is null || EditorColorPlane is null || EditorColorPlaneMarker is null || EditorHueSlider is null)
        {
            return;
        }

        EditorColorPlaneHueBase.Fill = new SolidColorBrush(HsvToRgb(EditorHueSlider.Value, 1, 1));
        double x = _editorSaturation * Math.Max(0, EditorColorPlane.ActualWidth);
        double y = (1 - _editorValue) * Math.Max(0, EditorColorPlane.ActualHeight);
        EditorColorPlaneMarker.Margin = new Thickness(
            x - (EditorColorPlaneMarker.Width / 2),
            y - (EditorColorPlaneMarker.Height / 2),
            0,
            0);
    }

    private static System.Windows.Media.Color HsvToRgb(double hue, double saturation, double value)
    {
        hue = (hue % 360 + 360) % 360;
        double c = value * saturation;
        double x = c * (1 - Math.Abs((hue / 60d % 2) - 1));
        double m = value - c;
        (double r1, double g1, double b1) = ((int)(hue / 60d)) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return System.Windows.Media.Color.FromRgb((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    private void EditorViewportHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editorCutMode)
        {
            return;
        }

        _isEditorDrawing = true;
        _activeEditorPolyline = new System.Windows.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(_editorColor),
            StrokeThickness = EditorBrushSizeSlider.Value,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        _activeEditorPolyline.Points.Add(ToEditorImagePoint(e.GetPosition(EditorViewportHost)));
        EditorDrawLayer.Children.Add(_activeEditorPolyline);
        EditorViewportHost.CaptureMouse();
    }

    private void EditorViewportHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_editorCutMode)
        {
            return;
        }

        if (_isEditorDrawing && e.LeftButton == MouseButtonState.Pressed && _activeEditorPolyline is not null)
        {
            _activeEditorPolyline.Points.Add(ToEditorImagePoint(e.GetPosition(EditorViewportHost)));
            return;
        }

        if (!_isEditorPanning || e.RightButton != MouseButtonState.Pressed)
        {
            return;
        }

        System.Windows.Point current = e.GetPosition(EditorViewportHost);
        EditorTranslateTransform.X = _editorPanOrigin.X + (current.X - _editorPanStart.X);
        EditorTranslateTransform.Y = _editorPanOrigin.Y + (current.Y - _editorPanStart.Y);
        ClampEditorPan();
    }

    private void EditorViewportHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isEditorDrawing)
        {
            return;
        }

        _isEditorDrawing = false;
        if (_activeEditorPolyline is not null)
        {
            _editorUndoStack.Push(_activeEditorPolyline);
            _activeEditorPolyline = null;
        }
        EditorViewportHost.ReleaseMouseCapture();
    }

    private void EditorViewportHost_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editorCutMode)
        {
            return;
        }

        _isEditorPanning = true;
        _editorPanStart = e.GetPosition(EditorViewportHost);
        _editorPanOrigin = new System.Windows.Point(EditorTranslateTransform.X, EditorTranslateTransform.Y);
        EditorViewportHost.CaptureMouse();
    }

    private void EditorViewportHost_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isEditorPanning = false;
        EditorViewportHost.ReleaseMouseCapture();
    }

    private void EditorViewportHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        ApplyEditorZoom(_editorZoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), false);
        e.Handled = true;
    }

    private System.Windows.Point ToEditorImagePoint(System.Windows.Point viewportPoint)
    {
        GeneralTransform? inverse = EditorImageLayer.RenderTransform.Inverse;
        return inverse is null ? viewportPoint : inverse.Transform(viewportPoint);
    }

    private void ApplyEditorZoom(double zoom, bool resetPan)
    {
        _editorZoom = Math.Clamp(zoom, 0.2, 6);
        EditorScaleTransform.ScaleX = _editorZoom;
        EditorScaleTransform.ScaleY = _editorZoom;
        if (resetPan)
        {
            EditorTranslateTransform.X = 0;
            EditorTranslateTransform.Y = 0;
        }
        ClampEditorPan();
    }

    private void ClampEditorPan()
    {
        double contentWidth = Math.Max(1, EditorBaseImage.ActualWidth * EditorScaleTransform.ScaleX);
        double contentHeight = Math.Max(1, EditorBaseImage.ActualHeight * EditorScaleTransform.ScaleY);
        double minX = Math.Min(0, EditorViewportHost.ActualWidth - contentWidth);
        double minY = Math.Min(0, EditorViewportHost.ActualHeight - contentHeight);
        double maxX = Math.Max(0, (EditorViewportHost.ActualWidth - contentWidth) / 2);
        double maxY = Math.Max(0, (EditorViewportHost.ActualHeight - contentHeight) / 2);
        EditorTranslateTransform.X = Math.Clamp(EditorTranslateTransform.X, minX, maxX);
        EditorTranslateTransform.Y = Math.Clamp(EditorTranslateTransform.Y, minY, maxY);
    }

    private void EditorViewportHost_SizeChanged(object sender, SizeChangedEventArgs e) => ClampEditorPan();

    private void EditorUndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editorUndoStack.Count == 0)
        {
            return;
        }

        UIElement element = _editorUndoStack.Pop();
        EditorDrawLayer.Children.Remove(element);
    }

    private void EditorSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_selectedImagePath is null || EditorBaseImage.Source is null)
            {
                return;
            }

            string? folder = Path.GetDirectoryName(_selectedImagePath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            RenderTargetBitmap merged = new((int)Math.Max(1, EditorImageLayer.ActualWidth), (int)Math.Max(1, EditorImageLayer.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            merged.Render(EditorImageLayer);
            string outputPath = TrySaveEditorRenderWithRetry(folder, merged);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                CaptureResultText.Text = "Save failed. File is locked by another process.";
                return;
            }

            CaptureResultText.Text = $"Edited copy saved: {Path.GetFileName(outputPath)}";
            RefreshGalleryItems();
            SetSelectedImage(outputPath);
            MainTabControl.SelectedIndex = 0;
        }
        catch
        {
            CaptureResultText.Text = "Save failed. Retry once.";
        }
    }

    private string TrySaveEditorRenderWithRetry(string folder, RenderTargetBitmap merged)
    {
        for (int attempt = 0; attempt < 7; attempt++)
        {
            string outputPath = BuildUniqueEditedPath(folder, "snapforge_edit");
            try
            {
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(merged));
                using FileStream stream = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                encoder.Save(stream);
                return outputPath;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(20);
            }
            catch (UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(20);
            }
        }

        return string.Empty;
    }

    private static string BuildUniqueEditedPath(string folder, string basePrefix)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string candidate = Path.Combine(folder, $"{basePrefix}_{stamp}.png");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (int i = 1; i <= 999; i++)
        {
            string retry = Path.Combine(folder, $"{basePrefix}_{stamp}_{i:000}.png");
            if (!File.Exists(retry))
            {
                return retry;
            }
        }

        return Path.Combine(folder, $"{basePrefix}_{Guid.NewGuid():N}.png");
    }

    private void EditorCutModeButton_Click(object sender, RoutedEventArgs e)
    {
        _editorCutMode = !_editorCutMode;
        EditorCropOverlay.Visibility = _editorCutMode ? Visibility.Visible : Visibility.Collapsed;
        EditorApplyCutButton.Visibility = _editorCutMode ? Visibility.Visible : Visibility.Collapsed;
        EditorCutModeButton.Background = _editorCutMode
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 176, 86))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(61, 102, 224));
        if (_editorCutMode)
        {
            System.Windows.Controls.Canvas.SetLeft(EditorCropRect, Math.Max(10, (EditorCropOverlay.ActualWidth - EditorCropRect.Width) / 2));
            System.Windows.Controls.Canvas.SetTop(EditorCropRect, Math.Max(10, (EditorCropOverlay.ActualHeight - EditorCropRect.Height) / 2));
            PositionEditorCutButton();
        }
    }

    private void EditorCropRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editorCutMode)
        {
            return;
        }

        _editorCropDragging = true;
        _editorCropDragMoved = false;
        _editorCropDragStart = e.GetPosition(EditorCropOverlay);
        _editorCropStartOrigin = new System.Windows.Point(System.Windows.Controls.Canvas.GetLeft(EditorCropRect), System.Windows.Controls.Canvas.GetTop(EditorCropRect));
        EditorCropRect.CaptureMouse();
        e.Handled = true;
    }

    private void EditorCropRect_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_editorCropDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        System.Windows.Point now = e.GetPosition(EditorCropOverlay);
        _editorCropDragMoved = true;
        double left = _editorCropStartOrigin.X + (now.X - _editorCropDragStart.X);
        double top = _editorCropStartOrigin.Y + (now.Y - _editorCropDragStart.Y);
        System.Windows.Controls.Canvas.SetLeft(EditorCropRect, Math.Clamp(left, 0, Math.Max(0, EditorCropOverlay.ActualWidth - EditorCropRect.Width)));
        System.Windows.Controls.Canvas.SetTop(EditorCropRect, Math.Clamp(top, 0, Math.Max(0, EditorCropOverlay.ActualHeight - EditorCropRect.Height)));
        PositionEditorCutButton();
    }

    private void EditorCropRect_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _editorCropDragging = false;
        EditorCropRect.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void EditorCropRect_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_editorCutMode)
        {
            EditorApplyCutButton.Visibility = Visibility.Visible;
            PositionEditorCutButton();
        }
    }

    private void EditorCropRect_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        PositionEditorCutButton();
    }

    private void EditorCropOverlay_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editorCutMode || IsEditorCropInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        System.Windows.Point p = e.GetPosition(EditorCropOverlay);
        double left = System.Windows.Controls.Canvas.GetLeft(EditorCropRect);
        double top = System.Windows.Controls.Canvas.GetTop(EditorCropRect);
        if (p.X < left || p.X > left + EditorCropRect.Width || p.Y < top || p.Y > top + EditorCropRect.Height)
        {
            return;
        }

        _editorCropDragging = true;
        _editorCropDragMoved = false;
        _editorCropDragStart = p;
        _editorCropStartOrigin = new System.Windows.Point(left, top);
        EditorCropOverlay.CaptureMouse();
    }

    private void EditorCropOverlay_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_editorCropDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        System.Windows.Point now = e.GetPosition(EditorCropOverlay);
        double dx = now.X - _editorCropDragStart.X;
        double dy = now.Y - _editorCropDragStart.Y;
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
        {
            return;
        }

        _editorCropDragMoved = true;
        double left = _editorCropStartOrigin.X + dx;
        double top = _editorCropStartOrigin.Y + dy;
        System.Windows.Controls.Canvas.SetLeft(EditorCropRect, Math.Clamp(left, 0, Math.Max(0, EditorCropOverlay.ActualWidth - EditorCropRect.Width)));
        System.Windows.Controls.Canvas.SetTop(EditorCropRect, Math.Clamp(top, 0, Math.Max(0, EditorCropOverlay.ActualHeight - EditorCropRect.Height)));
        PositionEditorCutButton();
    }

    private void EditorCropOverlay_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_editorCropDragging)
        {
            return;
        }

        _editorCropDragging = false;
        EditorCropRect.ReleaseMouseCapture();
        EditorCropOverlay.ReleaseMouseCapture();
        if (_editorCropDragMoved)
        {
            e.Handled = true;
        }
    }

    private static bool IsEditorCropInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.Thumb || source is System.Windows.Controls.Button)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void EditorCropTopLeftThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        => ResizeEditorCropRect(e.HorizontalChange, e.VerticalChange, -e.HorizontalChange, -e.VerticalChange);

    private void EditorCropTopRightThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        => ResizeEditorCropRect(0, e.VerticalChange, e.HorizontalChange, -e.VerticalChange);

    private void EditorCropBottomLeftThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        => ResizeEditorCropRect(e.HorizontalChange, 0, -e.HorizontalChange, e.VerticalChange);

    private void EditorCropBottomRightThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        => ResizeEditorCropRect(0, 0, e.HorizontalChange, e.VerticalChange);

    private void ResizeEditorCropRect(double leftDelta, double topDelta, double widthDelta, double heightDelta)
    {
        double left = System.Windows.Controls.Canvas.GetLeft(EditorCropRect) + leftDelta;
        double top = System.Windows.Controls.Canvas.GetTop(EditorCropRect) + topDelta;
        double width = Math.Clamp(EditorCropRect.Width + widthDelta, 40, Math.Max(40, EditorCropOverlay.ActualWidth));
        double height = Math.Clamp(EditorCropRect.Height + heightDelta, 40, Math.Max(40, EditorCropOverlay.ActualHeight));
        left = Math.Clamp(left, 0, Math.Max(0, EditorCropOverlay.ActualWidth - width));
        top = Math.Clamp(top, 0, Math.Max(0, EditorCropOverlay.ActualHeight - height));
        EditorCropRect.Width = width;
        EditorCropRect.Height = height;
        System.Windows.Controls.Canvas.SetLeft(EditorCropRect, left);
        System.Windows.Controls.Canvas.SetTop(EditorCropRect, top);
        PositionEditorCutButton();
    }

    private void PositionEditorCutButton()
    {
        double left = System.Windows.Controls.Canvas.GetLeft(EditorCropRect) + ((EditorCropRect.Width - EditorApplyCutButton.Width) / 2);
        double top = System.Windows.Controls.Canvas.GetTop(EditorCropRect) + ((EditorCropRect.Height - EditorApplyCutButton.Height) / 2);
        System.Windows.Controls.Canvas.SetLeft(EditorApplyCutButton, Math.Clamp(left, 0, Math.Max(0, EditorCropOverlay.ActualWidth - EditorApplyCutButton.Width)));
        System.Windows.Controls.Canvas.SetTop(EditorApplyCutButton, Math.Clamp(top, 0, Math.Max(0, EditorCropOverlay.ActualHeight - EditorApplyCutButton.Height)));
    }

    private void EditorApplyCutButton_Click(object sender, RoutedEventArgs e)
    {
        int x = Math.Max(0, (int)Math.Round(System.Windows.Controls.Canvas.GetLeft(EditorCropRect)));
        int y = Math.Max(0, (int)Math.Round(System.Windows.Controls.Canvas.GetTop(EditorCropRect)));
        int width = Math.Max(1, (int)Math.Round(EditorCropRect.Width));
        int height = Math.Max(1, (int)Math.Round(EditorCropRect.Height));
        EditorCropOverlay.Visibility = Visibility.Collapsed;
        EditorCropOverlay.UpdateLayout();
        EditorViewportHost.UpdateLayout();
        RenderTargetBitmap rendered = new((int)Math.Max(1, EditorViewportHost.ActualWidth), (int)Math.Max(1, EditorViewportHost.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        rendered.Render(EditorViewportHost);
        width = Math.Min(width, rendered.PixelWidth - x);
        height = Math.Min(height, rendered.PixelHeight - y);
        if (width < 2 || height < 2)
        {
            return;
        }

        CroppedBitmap cropped = new(rendered, new Int32Rect(x, y, width, height));
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using MemoryStream ms = new();
        encoder.Save(ms);
        ms.Position = 0;

        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        EditorBaseImage.Source = bitmap;
        EditorDrawLayer.Children.Clear();
        _editorUndoStack.Clear();
        _editorCutMode = false;
        EditorCropOverlay.Visibility = Visibility.Collapsed;
        EditorApplyCutButton.Visibility = Visibility.Collapsed;
        EditorCutModeButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(61, 102, 224));
        ApplyEditorZoom(1, true);
        Dispatcher.BeginInvoke(() => FitMainEditorImageToViewportAfterCut(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void FitMainEditorImageToViewportAfterCut()
    {
        if (EditorBaseImage.Source is null || EditorViewportHost.ActualWidth < 2 || EditorViewportHost.ActualHeight < 2)
        {
            return;
        }

        double contentWidth = Math.Max(1, EditorBaseImage.ActualWidth);
        double contentHeight = Math.Max(1, EditorBaseImage.ActualHeight);
        if (contentWidth < 2 || contentHeight < 2)
        {
            return;
        }

        double fillScale = Math.Max(EditorViewportHost.ActualWidth / contentWidth, EditorViewportHost.ActualHeight / contentHeight);
        fillScale = Math.Clamp(fillScale, 1, 6);
        ApplyEditorZoom(fillScale, true);
    }

    private void ShowQuickCaptureTabButton_Click(object sender, RoutedEventArgs e) => MainTabControl.SelectedIndex = 0;

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsTypingIntoTextInput())
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.V)
        {
            if (!string.IsNullOrWhiteSpace(_selectedImagePath) && File.Exists(_selectedImagePath))
            {
                MainTabControl.SelectedIndex = 1;
                e.Handled = true;
            }
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.S)
        {
            if (MainTabControl.SelectedIndex == 2)
            {
                EditorSaveButton_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            return;
        }

        if (MainTabControl.SelectedIndex == 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.Left)
            {
                ViewerPrevButton_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Right)
            {
                ViewerNextButton_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.Z)
        {
            if (MainTabControl.SelectedIndex == 2)
            {
                EditorUndoButton_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }

    private static bool IsTypingIntoTextInput()
    {
        return Keyboard.FocusedElement is System.Windows.Controls.TextBox;
    }

    private void BuyMeCoffeeButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternalLink("https://www.facebook.com/profile.php?id=100057897893230");
    }

    private void FacebookSupportLink_Click(object sender, RoutedEventArgs e)
    {
        OpenExternalLink("https://www.facebook.com/profile.php?id=100057897893230");
    }

    private static void OpenExternalLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckUpdatesAsync(isManual: true);
    }

    private void OpenUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_latestReleaseUrl))
        {
            OpenExternalLink(_latestReleaseUrl);
        }
    }

    private static string GetCurrentAppVersion()
    {
        string info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        int plusIndex = info.IndexOf('+');
        if (plusIndex > 0)
        {
            info = info[..plusIndex];
        }

        return info;
    }

    private static string NormalizeVersionText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0.0.0";
        }

        return value.Trim().TrimStart('v', 'V');
    }

    private static bool IsVersionNewer(string candidate, string current)
    {
        if (Version.TryParse(candidate, out Version? candidateVersion) &&
            Version.TryParse(current, out Version? currentVersion))
        {
            return candidateVersion > currentVersion;
        }

        return !string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase);
    }

    private async Task CheckUpdatesAsync(bool isManual)
    {
        if (_isCheckingUpdates || _isUpdatingInBackground)
        {
            return;
        }

        if (UpdateRepoOwner.Contains("YOUR_") || UpdateRepoName.Contains("YOUR_"))
        {
            if (isManual)
            {
                UpdateStatusText.Text = "Set UpdateRepoOwner and UpdateRepoName in MainWindow.xaml.cs first.";
            }
            return;
        }

        _isCheckingUpdates = true;
        _latestInstallerDownloadUrl = null;
        OpenUpdateButton.Visibility = Visibility.Collapsed;
        SetUpdateProgressVisible(false);
        if (isManual)
        {
            UpdateStatusText.Text = "Checking for updates...";
        }

        try
        {
            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SnapForgeUpdateChecker/1.0");
            }
            string url = $"https://api.github.com/repos/{UpdateRepoOwner}/{UpdateRepoName}/releases/latest";
            string json = await _httpClient.GetStringAsync(url);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string latestTag = root.TryGetProperty("tag_name", out JsonElement tagEl) ? tagEl.GetString() ?? string.Empty : string.Empty;
            string latestUrl = root.TryGetProperty("html_url", out JsonElement pageEl) ? pageEl.GetString() ?? string.Empty : string.Empty;
            _latestReleaseUrl = latestUrl;
            _latestInstallerDownloadUrl = TryGetInstallerDownloadUrl(root);

            string current = GetCurrentAppVersion();
            string currentNorm = NormalizeVersionText(current);
            string latestNorm = NormalizeVersionText(latestTag);
            LatestVersionText.Text = $"Latest version: {latestTag}";

            if (IsVersionNewer(latestNorm, currentNorm))
            {
                UpdateStatusText.Text = "New version found. Updating now...";
                OpenUpdateButton.Visibility = string.IsNullOrWhiteSpace(_latestReleaseUrl) ? Visibility.Collapsed : Visibility.Visible;
                if (!string.IsNullOrWhiteSpace(_latestInstallerDownloadUrl))
                {
                    await DownloadAndInstallUpdateAsync(_latestInstallerDownloadUrl);
                }
                else
                {
                    UpdateStatusText.Text = "Installer asset not found in release. Use 'Open update download'.";
                    SetUpdateProgressVisible(false);
                }
            }
            else
            {
                UpdateStatusText.Text = "You are up to date.";
                SetUpdateProgressVisible(false);
            }
        }
        catch
        {
            if (isManual)
            {
                UpdateStatusText.Text = "Update check failed. Verify internet or GitHub repo settings.";
                SetUpdateProgressVisible(false);
            }
        }
        finally
        {
            _isCheckingUpdates = false;
        }
    }

    private async Task DownloadAndInstallUpdateAsync(string downloadUrl)
    {
        _isUpdatingInBackground = true;
        try
        {
            string tempInstallerPath = Path.Combine(Path.GetTempPath(), $"SnapForgeUpdate_{DateTime.Now:yyyyMMdd_HHmmss}.exe");
            UpdateStatusText.Text = "Downloading update...";
            SetUpdateProgressVisible(true);
            SetUpdateProgress(2, "Starting download");

            using HttpResponseMessage response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;

            await using Stream source = await response.Content.ReadAsStreamAsync();
            await using FileStream target = new(tempInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                readTotal += read;
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    double percent = (double)readTotal / totalBytes.Value;
                    double mapped = 5 + (percent * 70);
                    SetUpdateProgress(mapped, $"Downloading {Math.Round(percent * 100)}%");
                }
            }

            SetUpdateProgress(78, "Download complete");
            UpdateStatusText.Text = "Installing update in silent mode...";
            StartSilentUpdateAndRestart(tempInstallerPath);
        }
        catch
        {
            UpdateStatusText.Text = "Automatic update failed. Use 'Open update download'.";
            SetUpdateProgress(0, "Update failed");
            SetUpdateProgressVisible(false);
            _isUpdatingInBackground = false;
        }
    }

    private void StartSilentUpdateAndRestart(string installerPath)
    {
        try
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName
                                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SnapForge.exe");
            string args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /SP-";
            string command = $"/c start \"\" /wait \"{installerPath}\" {args} && timeout /t 1 /nobreak >nul && start \"\" \"{currentExe}\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = command,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            SetUpdateProgress(100, "Installing and restarting...");
            _allowClose = true;
            Close();
        }
        catch
        {
            UpdateStatusText.Text = "Could not start silent installer.";
            SetUpdateProgress(0, "Install start failed");
            SetUpdateProgressVisible(false);
            _isUpdatingInBackground = false;
        }
    }

    private void SetUpdateProgress(double value, string text)
    {
        double clamped = Math.Clamp(value, 0, 100);
        UpdateProgressBar.Value = clamped;
        UpdateProgressText.Text = $"Progress: {Math.Round(clamped)}% - {text}";
    }

    private void SetUpdateProgressVisible(bool visible)
    {
        UpdateProgressBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgressText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string? TryGetInstallerDownloadUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? fallbackExe = null;
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out JsonElement nameEl) ? (nameEl.GetString() ?? string.Empty) : string.Empty;
            string url = asset.TryGetProperty("browser_download_url", out JsonElement urlEl) ? (urlEl.GetString() ?? string.Empty) : string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("installer", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }

                fallbackExe = url;
            }
        }

        return fallbackExe;
    }

    private void NotificationDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.NotificationDurationMs = (int)e.NewValue;
        NotificationDurationText.Text = $"{_settings.NotificationDurationMs} ms";
        _settingsService.Save(_settings);
    }

    private void ShowCaptureToast(string filePath)
    {
        _captureToast?.Close();
        string overlayShortcut = string.IsNullOrWhiteSpace(_settings.OverlayHotkeyComboText) ? "Shift+F12" : _settings.OverlayHotkeyComboText;
        _captureToast = new OverlayToastWindow(filePath, overlayShortcut);
        _captureToast.Left = SystemParameters.WorkArea.Right - _captureToast.Width - 20;
        double finalTop = SystemParameters.WorkArea.Top + 20;
        _captureToast.Top = SystemParameters.WorkArea.Top - _captureToast.Height - 8;
        _captureToast.Show();
        PlayCaptureSound();
        DoubleAnimation slide = new()
        {
            From = _captureToast.Top,
            To = finalTop,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _captureToast.BeginAnimation(TopProperty, slide);
        DoubleAnimation fade = new()
        {
            BeginTime = TimeSpan.FromMilliseconds(_settings.NotificationDurationMs),
            Duration = TimeSpan.FromMilliseconds(550),
            From = 1,
            To = 0
        };
        fade.Completed += (_, _) =>
        {
            _captureToast?.Close();
            _captureToast = null;
        };
        _captureToast.BeginAnimation(OpacityProperty, fade);
    }

    private void PlayCaptureSound()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "assets", "sounds", "capture.mp3"),
            Path.Combine(baseDir, "assets", "sounds", "capture.wav"),
            Path.Combine(baseDir, "sounds", "capture.mp3"),
            Path.Combine(baseDir, "sounds", "capture.wav")
        ];
        string? soundPath = candidates.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(soundPath))
        {
            System.Media.SystemSounds.Asterisk.Play();
            return;
        }

        try
        {
            _toastSoundPlayer.Open(new Uri(soundPath, UriKind.Absolute));
            _toastSoundPlayer.Volume = 0.9;
            _toastSoundPlayer.Position = TimeSpan.Zero;
            _toastSoundPlayer.Play();
        }
        catch
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
    }

    private void ShowOverlayHub()
    {
        RefreshGalleryItems();
        List<ScreenshotItem> items = [];
        foreach (GalleryItemView item in _galleryItems
                     .Where(x => File.Exists(x.FilePath))
                     .OrderByDescending(x => File.GetCreationTime(x.FilePath)))
        {
            try
            {
                items.Add(new ScreenshotItem
                {
                    FilePath = item.FilePath,
                    FileName = item.FileName,
                    CreatedAt = File.GetCreationTime(item.FilePath),
                    Thumbnail = CreateThumb(item.FilePath)
                });
            }
            catch
            {
                // skip broken/locked file thumbnails and continue opening overlay
            }
        }

        if (_overlayHubWindow is not null)
        {
            if (_overlayHubWindow.IsVisible)
            {
                _overlayHubWindow.Activate();
                _overlayHubWindow.Topmost = true;
                _overlayHubWindow.Topmost = false;
                return;
            }

            _overlayHubWindow = null;
        }

        try
        {
            _overlayHubWindow = new OverlayHubWindow(items);
            _overlayHubWindow.Left = SystemParameters.WorkArea.Left + 16;
            _overlayHubWindow.Top = SystemParameters.WorkArea.Top + ((SystemParameters.WorkArea.Height - _overlayHubWindow.Height) / 2);
            _overlayHubWindow.Closed += (_, _) => _overlayHubWindow = null;
            _overlayHubWindow.Show();
        }
        catch
        {
            CaptureResultText.Text = "Overlay open failed.";
        }
    }

    private sealed class GalleryItemView : INotifyPropertyChanged
    {
        private int _thumbSize;
        private bool _isSelected;
        public event PropertyChangedEventHandler? PropertyChanged;
        public required string FilePath { get; init; }
        public required string FileName { get; init; }
        public required BitmapImage Thumbnail { get; init; }
        public int ThumbSize
        {
            get => _thumbSize;
            set
            {
                if (_thumbSize == value) return;
                _thumbSize = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbSize)));
            }
        }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
}