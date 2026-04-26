using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapForge;

public partial class SimpleImageEditorWindow : Window
{
    public static event Action<string>? ImageSaved;
    private string _filePath;
    private double _zoom = 1;
    private bool _isPanning;
    private System.Windows.Point _panStart;
    private System.Windows.Point _panOrigin;
    private readonly Stack<UIElement> _undoStack = [];
    private System.Windows.Media.Color _customColor = Colors.OrangeRed;
    private bool _isDrawing;
    private System.Windows.Shapes.Polyline? _activePolyline;
    private bool _cropMode;
    private double _hue = 15;
    private double _saturation = 1;
    private double _value = 1;

    public SimpleImageEditorWindow(string filePath)
    {
        InitializeComponent();
        _filePath = string.Empty;
        UpdateBrush();
        UpdateModeText();
        UpdateColorPlaneVisuals();
        ShowImage(filePath);
    }

    public void ShowImage(string filePath)
    {
        _filePath = filePath;
        BitmapImage bitmap = LoadBitmapSafe(filePath);
        BaseImage.Source = bitmap;
        DrawLayer.Children.Clear();
        _undoStack.Clear();
        _activePolyline = null;
        _isDrawing = false;
        _isPanning = false;
        _cropMode = false;
        CropOverlay.Visibility = Visibility.Collapsed;
        CropApplyButton.Visibility = Visibility.Collapsed;
        _zoom = 1;
        ApplyZoom(1, true);
        Dispatcher.BeginInvoke(() => ApplyZoom(1, true), System.Windows.Threading.DispatcherPriority.Loaded);
        WindowState = WindowState.Normal;
        Activate();
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

    private void UpdateBrush()
    {
        if (ColorPreview is null)
        {
            return;
        }

        _customColor = HsvToRgb(_hue, _saturation, _value);
        ColorPreview.Background = new SolidColorBrush(_customColor);
    }

    private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateBrush();

    private void UpdateModeText()
    {
        if (ModeText is null)
        {
            return;
        }

        ModeText.Text = "Draw (always on)";
    }

    private void ViewportHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_cropMode)
        {
            return;
        }

        _isDrawing = true;
        System.Windows.Point point = ToImageLayerPoint(e.GetPosition(ViewportHost));
        _activePolyline = new System.Windows.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(_customColor),
            StrokeThickness = BrushSizeSlider.Value,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        _activePolyline.Points.Add(point);
        DrawLayer.Children.Add(_activePolyline);
        ViewportHost.CaptureMouse();
    }

    private void ViewportHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_cropMode)
        {
            return;
        }

        if (_isDrawing && e.LeftButton == MouseButtonState.Pressed && _activePolyline is not null)
        {
            _activePolyline.Points.Add(ToImageLayerPoint(e.GetPosition(ViewportHost)));
            return;
        }

        if (!_isPanning || e.RightButton != MouseButtonState.Pressed)
        {
            return;
        }

        System.Windows.Point current = e.GetPosition(ViewportHost);
        ImageTranslateTransform.X = _panOrigin.X + (current.X - _panStart.X);
        ImageTranslateTransform.Y = _panOrigin.Y + (current.Y - _panStart.Y);
        ClampPan();
    }

    private void ViewportHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawing)
        {
            _isDrawing = false;
            if (_activePolyline is not null)
            {
                _undoStack.Push(_activePolyline);
                _activePolyline = null;
            }
            ViewportHost.ReleaseMouseCapture();
            return;
        }

        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ViewportHost.ReleaseMouseCapture();
    }

    private void ViewportHost_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_cropMode)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(ViewportHost);
        _panOrigin = new System.Windows.Point(ImageTranslateTransform.X, ImageTranslateTransform.Y);
        ViewportHost.CaptureMouse();
    }

    private void ViewportHost_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ViewportHost.ReleaseMouseCapture();
    }

    private void ViewportHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        System.Windows.Point mouse = e.GetPosition(ViewportHost);
        GeneralTransform? inverse = ImageLayer.RenderTransform.Inverse;
        System.Windows.Point imagePoint = inverse is null ? mouse : inverse.Transform(mouse);
        double newZoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.2, 6);
        _zoom = newZoom;
        ImageScaleTransform.ScaleX = _zoom;
        ImageScaleTransform.ScaleY = _zoom;
        ImageTranslateTransform.X = mouse.X - (imagePoint.X * _zoom);
        ImageTranslateTransform.Y = mouse.Y - (imagePoint.Y * _zoom);
        ClampPan();
        ZoomText.Text = $"{(int)(_zoom * 100)}%";
        e.Handled = true;
    }

    private void ViewportHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateColorPlaneVisuals();
        if (_zoom <= 1.001)
        {
            _zoom = 1;
            ApplyZoom(1, true);
            return;
        }

        ClampPan();
    }

    private void ApplyZoom(double zoom, bool resetPan)
    {
        _zoom = zoom;
        if (BaseImage.Source is not BitmapSource || ViewportHost.ActualWidth < 2 || ViewportHost.ActualHeight < 2)
        {
            return;
        }

        double scale = _zoom;
        ImageScaleTransform.ScaleX = scale;
        ImageScaleTransform.ScaleY = scale;

        if (resetPan)
        {
            double contentWidth = Math.Max(1, BaseImage.ActualWidth * scale);
            double contentHeight = Math.Max(1, BaseImage.ActualHeight * scale);
            ImageTranslateTransform.X = (ViewportHost.ActualWidth - contentWidth) / 2;
            ImageTranslateTransform.Y = (ViewportHost.ActualHeight - contentHeight) / 2;
        }

        ClampPan();
        ZoomText.Text = $"{(int)(_zoom * 100)}%";
    }

    private void ClampPan()
    {
        if (BaseImage.Source is null)
        {
            return;
        }

        double contentWidth = Math.Max(1, BaseImage.ActualWidth * ImageScaleTransform.ScaleX);
        double contentHeight = Math.Max(1, BaseImage.ActualHeight * ImageScaleTransform.ScaleY);
        double minX = Math.Min(0, ViewportHost.ActualWidth - contentWidth);
        double minY = Math.Min(0, ViewportHost.ActualHeight - contentHeight);
        double maxX = Math.Max(0, (ViewportHost.ActualWidth - contentWidth) / 2);
        double maxY = Math.Max(0, (ViewportHost.ActualHeight - contentHeight) / 2);
        ImageTranslateTransform.X = Math.Clamp(ImageTranslateTransform.X, minX, maxX);
        ImageTranslateTransform.Y = Math.Clamp(ImageTranslateTransform.Y, minY, maxY);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewportHost.ActualWidth < 2 || ViewportHost.ActualHeight < 2)
            {
                return;
            }

            string outputPath = TrySaveToFileWithRetry();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            ImageSaved?.Invoke(outputPath);
        }
        catch
        {
            // swallow save edge-case exceptions to avoid hard crash
        }
    }

    private string BuildTimestampPath()
    {
        string directory = Path.GetDirectoryName(_filePath) ?? AppContext.BaseDirectory;
        string baseName = Path.GetFileNameWithoutExtension(_filePath);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string candidate = Path.Combine(directory, $"{baseName}_{timestamp}_edited.png");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (int i = 1; i <= 999; i++)
        {
            string retry = Path.Combine(directory, $"{baseName}_{timestamp}_{i:000}_edited.png");
            if (!File.Exists(retry))
            {
                return retry;
            }
        }

        return Path.Combine(directory, $"{baseName}_{Guid.NewGuid():N}_edited.png");
    }

    private void SaveToFile(string outputPath)
    {
        if (ViewportHost.ActualWidth < 2 || ViewportHost.ActualHeight < 2)
        {
            return;
        }

        RenderTargetBitmap render = new((int)ViewportHost.ActualWidth, (int)ViewportHost.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        render.Render(ViewportHost);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(render));
        using FileStream stream = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private string TrySaveToFileWithRetry()
    {
        for (int attempt = 0; attempt < 7; attempt++)
        {
            string outputPath = BuildTimestampPath();
            try
            {
                SaveToFile(outputPath);
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

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        UIElement element = _undoStack.Pop();
        DrawLayer.Children.Remove(element);
    }

    private void CutModeButton_Click(object sender, RoutedEventArgs e)
    {
        _cropMode = !_cropMode;
        CropOverlay.Visibility = _cropMode ? Visibility.Visible : Visibility.Collapsed;
        CropApplyButton.Visibility = _cropMode ? Visibility.Visible : Visibility.Collapsed;
        CutModeButton.Background = _cropMode
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 176, 86))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(61, 102, 224));
        if (_cropMode)
        {
            EnsureCropRectInside();
        }
    }

    private void CropRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void CropRect_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        e.Handled = true;
    }

    private void CropRect_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void CropRect_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        EnsureCropRectInside();
    }

    private void CropOverlay_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_cropMode)
        {
            EnsureCropRectInside();
        }
    }

    private void CropMoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        EnsureCropRectInside();
        double left = Canvas.GetLeft(CropRect) + e.HorizontalChange;
        double top = Canvas.GetTop(CropRect) + e.VerticalChange;
        Canvas.SetLeft(CropRect, Math.Clamp(left, 0, Math.Max(0, CropOverlay.ActualWidth - CropRect.Width)));
        Canvas.SetTop(CropRect, Math.Clamp(top, 0, Math.Max(0, CropOverlay.ActualHeight - CropRect.Height)));
    }

    

    private void CropTopLeftThumb_DragDelta(object sender, DragDeltaEventArgs e)
        => ResizeCropRect(e.HorizontalChange, e.VerticalChange, -e.HorizontalChange, -e.VerticalChange);

    private void CropTopRightThumb_DragDelta(object sender, DragDeltaEventArgs e)
        => ResizeCropRect(0, e.VerticalChange, e.HorizontalChange, -e.VerticalChange);

    private void CropBottomLeftThumb_DragDelta(object sender, DragDeltaEventArgs e)
        => ResizeCropRect(e.HorizontalChange, 0, -e.HorizontalChange, e.VerticalChange);

    private void CropBottomRightThumb_DragDelta(object sender, DragDeltaEventArgs e)
        => ResizeCropRect(0, 0, e.HorizontalChange, e.VerticalChange);

    private void ResizeCropRect(double leftDelta, double topDelta, double widthDelta, double heightDelta)
    {
        double left = Canvas.GetLeft(CropRect) + leftDelta;
        double top = Canvas.GetTop(CropRect) + topDelta;
        double width = Math.Clamp(CropRect.Width + widthDelta, 40, Math.Max(40, CropOverlay.ActualWidth));
        double height = Math.Clamp(CropRect.Height + heightDelta, 40, Math.Max(40, CropOverlay.ActualHeight));
        left = Math.Clamp(left, 0, Math.Max(0, CropOverlay.ActualWidth - width));
        top = Math.Clamp(top, 0, Math.Max(0, CropOverlay.ActualHeight - height));
        CropRect.Width = width;
        CropRect.Height = height;
        Canvas.SetLeft(CropRect, left);
        Canvas.SetTop(CropRect, top);
    }

    private void EnsureCropRectInside()
    {
        if (CropOverlay.ActualWidth < 2 || CropOverlay.ActualHeight < 2)
        {
            return;
        }

        if (double.IsNaN(Canvas.GetLeft(CropRect)))
        {
            Canvas.SetLeft(CropRect, Math.Max(10, (CropOverlay.ActualWidth - CropRect.Width) / 2));
        }

        if (double.IsNaN(Canvas.GetTop(CropRect)))
        {
            Canvas.SetTop(CropRect, Math.Max(10, (CropOverlay.ActualHeight - CropRect.Height) / 2));
        }

        Canvas.SetLeft(CropRect, Math.Clamp(Canvas.GetLeft(CropRect), 0, Math.Max(0, CropOverlay.ActualWidth - CropRect.Width)));
        Canvas.SetTop(CropRect, Math.Clamp(Canvas.GetTop(CropRect), 0, Math.Max(0, CropOverlay.ActualHeight - CropRect.Height)));
    }

    private void CropApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewportHost.ActualWidth < 2 || ViewportHost.ActualHeight < 2)
        {
            return;
        }

        double left = Canvas.GetLeft(CropRect);
        double top = Canvas.GetTop(CropRect);
        int width = Math.Max(1, (int)Math.Round(CropRect.Width));
        int height = Math.Max(1, (int)Math.Round(CropRect.Height));
        int x = Math.Max(0, (int)Math.Round(left));
        int y = Math.Max(0, (int)Math.Round(top));
        int maxWidth = Math.Max(1, (int)ViewportHost.ActualWidth - x);
        int maxHeight = Math.Max(1, (int)ViewportHost.ActualHeight - y);
        width = Math.Min(width, maxWidth);
        height = Math.Min(height, maxHeight);

        CropOverlay.Visibility = Visibility.Collapsed;
        CropOverlay.UpdateLayout();
        ViewportHost.UpdateLayout();
        RenderTargetBitmap viewRender = new((int)ViewportHost.ActualWidth, (int)ViewportHost.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        viewRender.Render(ViewportHost);
        CroppedBitmap cropped = new(viewRender, new Int32Rect(x, y, width, height));
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using MemoryStream ms = new();
        encoder.Save(ms);
        ms.Position = 0;

        BitmapImage newImage = new();
        newImage.BeginInit();
        newImage.CacheOption = BitmapCacheOption.OnLoad;
        newImage.StreamSource = ms;
        newImage.EndInit();
        newImage.Freeze();

        BaseImage.Source = newImage;
        DrawLayer.Children.Clear();
        _undoStack.Clear();
        _zoom = 1;
        ApplyZoom(1, true);
        Dispatcher.BeginInvoke(() => FitImageToViewportAfterCut(), System.Windows.Threading.DispatcherPriority.Loaded);
        _cropMode = false;
        CropOverlay.Visibility = Visibility.Collapsed;
        CropApplyButton.Visibility = Visibility.Collapsed;
        CutModeButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(61, 102, 224));
    }

    private void FitImageToViewportAfterCut()
    {
        if (BaseImage.Source is null || ViewportHost.ActualWidth < 2 || ViewportHost.ActualHeight < 2)
        {
            return;
        }

        double contentWidth = Math.Max(1, BaseImage.ActualWidth);
        double contentHeight = Math.Max(1, BaseImage.ActualHeight);
        if (contentWidth < 2 || contentHeight < 2)
        {
            return;
        }

        double fillScale = Math.Max(ViewportHost.ActualWidth / contentWidth, ViewportHost.ActualHeight / contentHeight);
        fillScale = Math.Clamp(fillScale, 1, 6);
        ApplyZoom(fillScale, true);
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.S)
        {
            SaveButton_Click(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.Z)
        {
            UndoButton_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _hue = e.NewValue;
        UpdateColorPlaneVisuals();
        UpdateBrush();
    }

    private void ColorPlane_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SetColorFromPlanePoint(e.GetPosition(ColorPlane));
        ColorPlane.CaptureMouse();
        e.Handled = true;
    }

    private void ColorPlane_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ColorPlane.IsMouseCaptured)
        {
            ColorPlane.ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    private void ColorPlane_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !ColorPlane.IsMouseCaptured)
        {
            return;
        }

        SetColorFromPlanePoint(e.GetPosition(ColorPlane));
        e.Handled = true;
    }

    private void SetColorFromPlanePoint(System.Windows.Point p)
    {
        if (ColorPlane.ActualWidth < 2 || ColorPlane.ActualHeight < 2)
        {
            return;
        }

        double x = Math.Clamp(p.X, 0, ColorPlane.ActualWidth);
        double y = Math.Clamp(p.Y, 0, ColorPlane.ActualHeight);
        _saturation = x / ColorPlane.ActualWidth;
        _value = 1 - (y / ColorPlane.ActualHeight);
        ColorPlaneMarker.Margin = new Thickness(x - (ColorPlaneMarker.Width / 2), y - (ColorPlaneMarker.Height / 2), 0, 0);
        UpdateBrush();
    }

    private void UpdateColorPlaneVisuals()
    {
        if (ColorPlaneHueBase is null || ColorPlane is null || ColorPlaneMarker is null)
        {
            return;
        }

        ColorPlaneHueBase.Fill = new SolidColorBrush(HsvToRgb(_hue, 1, 1));
        double x = _saturation * Math.Max(0, ColorPlane.ActualWidth);
        double y = (1 - _value) * Math.Max(0, ColorPlane.ActualHeight);
        ColorPlaneMarker.Margin = new Thickness(x - (ColorPlaneMarker.Width / 2), y - (ColorPlaneMarker.Height / 2), 0, 0);
    }

    private static System.Windows.Media.Color HsvToRgb(double h, double s, double v)
    {
        h = h % 360;
        if (h < 0)
        {
            h += 360;
        }

        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        return System.Windows.Media.Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private System.Windows.Point ToImageLayerPoint(System.Windows.Point viewportPoint)
    {
        GeneralTransform? inverse = ImageLayer.RenderTransform.Inverse;
        if (inverse is null)
        {
            return viewportPoint;
        }

        System.Windows.Point p = inverse.Transform(viewportPoint);
        return new System.Windows.Point(
            Math.Clamp(p.X, 0, Math.Max(1, DrawLayer.ActualWidth)),
            Math.Clamp(p.Y, 0, Math.Max(1, DrawLayer.ActualHeight)));
    }

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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
