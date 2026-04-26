using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SnapForge.Services;

namespace SnapForge;

public partial class ImageViewerPopupWindow : Window
{
    private readonly List<string> _folderImages = [];
    private int _currentIndex;
    private double _zoom = 1;
    private bool _isPanning;
    private System.Windows.Point _panStart;
    private System.Windows.Point _panOrigin;
    private readonly SettingsService _settingsService = new();
    private readonly Models.UserSettings _settings;

    public ImageViewerPopupWindow(string filePath)
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        LoadFolderImages(filePath);
        LoadCurrentImage();
    }

    public void ShowImage(string filePath)
    {
        LoadFolderImages(filePath);
        LoadCurrentImage();
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
    }

    private void LoadFolderImages(string filePath)
    {
        _folderImages.Clear();
        string? folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            _folderImages.Add(filePath);
            _currentIndex = 0;
            return;
        }

        _folderImages.AddRange(Directory.GetFiles(folder, "*.png").OrderByDescending(File.GetCreationTime));
        _currentIndex = Math.Max(0, _folderImages.FindIndex(x => string.Equals(x, filePath, StringComparison.OrdinalIgnoreCase)));
    }

    private void LoadCurrentImage()
    {
        if (_folderImages.Count == 0)
        {
            return;
        }

        string path = _folderImages[_currentIndex];
        FileNameText.Text = Path.GetFileName(path);
        BitmapImage bitmap = LoadBitmapSafe(path);
        PreviewImage.Source = bitmap;
        ApplyZoom(1, true);
        Dispatcher.BeginInvoke(() => UpdateTransform(true), System.Windows.Threading.DispatcherPriority.Loaded);
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

    private void ApplyZoom(double value, bool resetPan = false)
    {
        _zoom = Math.Clamp(value, 0.2, 6);
        UpdateTransform(resetPan);
        ZoomText.Text = $"{(int)(_zoom * 100)}%";
    }

    private void UpdateTransform(bool resetPan)
    {
        if (PreviewImage.Source is not BitmapSource source || ViewportHost.ActualWidth < 2 || ViewportHost.ActualHeight < 2)
        {
            return;
        }

        double effectiveScale = _zoom;
        ImageScaleTransform.ScaleX = effectiveScale;
        ImageScaleTransform.ScaleY = effectiveScale;

        double contentWidth = Math.Max(1, PreviewImage.ActualWidth * effectiveScale);
        double contentHeight = Math.Max(1, PreviewImage.ActualHeight * effectiveScale);

        if (resetPan)
        {
            ImageTranslateTransform.X = (ViewportHost.ActualWidth - contentWidth) / 2;
            ImageTranslateTransform.Y = (ViewportHost.ActualHeight - contentHeight) / 2;
            return;
        }

        double minX = Math.Min(0, ViewportHost.ActualWidth - contentWidth);
        double minY = Math.Min(0, ViewportHost.ActualHeight - contentHeight);
        double maxX = Math.Max(0, (ViewportHost.ActualWidth - contentWidth) / 2);
        double maxY = Math.Max(0, (ViewportHost.ActualHeight - contentHeight) / 2);
        ImageTranslateTransform.X = Math.Clamp(ImageTranslateTransform.X, minX, maxX);
        ImageTranslateTransform.Y = Math.Clamp(ImageTranslateTransform.Y, minY, maxY);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoom * 1.12);

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoom / 1.12);

    private void ViewportHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        double factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        ApplyZoom(_zoom * factor);
        e.Handled = true;
    }

    private void ViewportHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _panStart = e.GetPosition(ViewportHost);
        _panOrigin = new System.Windows.Point(ImageTranslateTransform.X, ImageTranslateTransform.Y);
        ViewportHost.CaptureMouse();
    }

    private void ViewportHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPanning || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        System.Windows.Point current = e.GetPosition(ViewportHost);
        ImageTranslateTransform.X = _panOrigin.X + (current.X - _panStart.X);
        ImageTranslateTransform.Y = _panOrigin.Y + (current.Y - _panStart.Y);
        UpdateTransform(false);
    }

    private void ViewportHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ViewportHost.ReleaseMouseCapture();
    }

    private void ViewportHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_zoom <= 1.001)
        {
            _zoom = 1;
            ApplyZoom(1, true);
            return;
        }

        UpdateTransform(false);
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (_folderImages.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex - 1 + _folderImages.Count) % _folderImages.Count;
        LoadCurrentImage();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_folderImages.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex + 1) % _folderImages.Count;
        LoadCurrentImage();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_folderImages.Count == 0)
        {
            return;
        }

        string currentPath = _folderImages[_currentIndex];
        if (!File.Exists(currentPath))
        {
            _folderImages.RemoveAt(_currentIndex);
            _currentIndex = Math.Clamp(_currentIndex, 0, Math.Max(0, _folderImages.Count - 1));
            if (_folderImages.Count == 0)
            {
                Close();
            }
            else
            {
                LoadCurrentImage();
            }
            return;
        }

        bool shouldDelete = !_settings.AskBeforeDeleteImage;
        bool disableAsking = false;

        if (!shouldDelete)
        {
            DeleteConfirmWindow confirm = new(Path.GetFileName(currentPath))
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            confirm.ShowDialog();
            shouldDelete = confirm.ShouldDelete;
            disableAsking = confirm.DoNotAskAgain;
        }

        if (!shouldDelete)
        {
            return;
        }

        try
        {
            File.Delete(currentPath);
            _folderImages.RemoveAt(_currentIndex);
            if (disableAsking && _settings.AskBeforeDeleteImage)
            {
                _settings.AskBeforeDeleteImage = false;
                _settingsService.Save(_settings);
            }

            if (_folderImages.Count == 0)
            {
                Close();
                return;
            }

            _currentIndex = Math.Clamp(_currentIndex, 0, _folderImages.Count - 1);
            LoadCurrentImage();
        }
        catch
        {
            // ignore delete failures to keep viewer alive
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_folderImages.Count == 0)
        {
            return;
        }

        string currentPath = _folderImages[_currentIndex];
        if (!File.Exists(currentPath))
        {
            return;
        }

        try
        {
            SimpleImageEditorWindow editor = new(currentPath)
            {
                Left = Left,
                Top = Top,
                Width = Width,
                Height = Height
            };
            editor.Show();
            Close();
        }
        catch
        {
            System.Windows.MessageBox.Show("Editor could not open for this image.", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
