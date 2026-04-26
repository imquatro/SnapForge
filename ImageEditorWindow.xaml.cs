using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using WColor = System.Windows.Media.Color;
using WColors = System.Windows.Media.Colors;
using WBrushes = System.Windows.Media.Brushes;

namespace SnapForge;

public partial class ImageEditorWindow : Window
{
    public static event Action<string>? ImageSaved;
    private readonly string _filePath;
    private readonly Stack<EditorSnapshot> _undoStack = [];
    private Border? _selectedTextBorder;
    private Border? _draggingTextBorder;
    private System.Windows.Point _dragStartPoint;
    private System.Windows.Point _dragStartOrigin;
    private bool _isTextMode;
    private bool _isRestoringSnapshot;
    private double _zoom = 1;

    public ImageEditorWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        BaseImage.Source = LoadBitmapSafe(filePath);
        ColorComboBox.SelectedIndex = 0;
        InkLayer.EditingMode = InkCanvasEditingMode.Ink;
        UpdateBrush();
        ApplyZoom(1);
        PushSnapshot();
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

    private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateBrush();

    private void ColorComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateBrush();
        ApplyStyleToSelectedText();
    }

    private void TextSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyStyleToSelectedText();

    private void TextWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_selectedTextBorder is null)
        {
            return;
        }

        _selectedTextBorder.Width = e.NewValue;
        PushSnapshot();
    }

    private void UpdateBrush()
    {
        if (ColorComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem selected)
        {
            return;
        }

        WColor color = selected.Content?.ToString() switch
        {
            "Green" => WColors.LimeGreen,
            "Blue" => WColors.DeepSkyBlue,
            "Yellow" => WColors.Gold,
            "White" => WColors.White,
            _ => WColors.OrangeRed
        };

        InkLayer.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color,
            Width = BrushSizeSlider.Value,
            Height = BrushSizeSlider.Value
        };
    }

    private void BrushModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isTextMode = false;
        InkLayer.EditingMode = InkCanvasEditingMode.Ink;
    }

    private void TextModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isTextMode = true;
        InkLayer.EditingMode = InkCanvasEditingMode.None;
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count <= 1)
        {
            return;
        }

        _undoStack.Pop();
        RestoreSnapshot(_undoStack.Peek());
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (EditorSurface.ActualWidth < 1 || EditorSurface.ActualHeight < 1)
        {
            return;
        }

        string outputPath = BuildTimestampPath();
        SaveToFile(outputPath);
        ImageSaved?.Invoke(outputPath);
    }

    private void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        using Forms.SaveFileDialog dialog = new();
        dialog.Filter = "PNG Image|*.png";
        dialog.FileName = $"{Path.GetFileNameWithoutExtension(_filePath)}_edited.png";
        if (dialog.ShowDialog() != Forms.DialogResult.OK || EditorSurface.ActualWidth < 1 || EditorSurface.ActualHeight < 1)
        {
            return;
        }

        SaveToFile(dialog.FileName);
        ImageSaved?.Invoke(dialog.FileName);
    }

    private string BuildTimestampPath()
    {
        string directory = Path.GetDirectoryName(_filePath) ?? AppContext.BaseDirectory;
        string baseName = Path.GetFileNameWithoutExtension(_filePath);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(directory, $"{baseName}_{timestamp}_edited.png");
    }

    private void SaveToFile(string outputPath)
    {
        RenderTargetBitmap render = new((int)EditorSurface.ActualWidth, (int)EditorSurface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        render.Render(EditorSurface);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(render));
        using FileStream stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private void InkLayer_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isTextMode)
        {
            return;
        }

        System.Windows.Point pos = e.GetPosition(TextLayer);
        AddTextBox(pos.X, pos.Y, "Type text", true);
        e.Handled = true;
    }

    private void AddTextBox(double left, double top, string text, bool pushSnapshot)
    {
        Border container = new()
        {
            Width = TextWidthSlider.Value,
            MinHeight = 50,
            BorderBrush = WBrushes.DeepSkyBlue,
            BorderThickness = new Thickness(1.2),
            Background = new SolidColorBrush(WColor.FromArgb(90, 12, 18, 28)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6)
        };

        Grid grid = new();
        System.Windows.Controls.TextBox textBox = new()
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 40,
            FontSize = TextSizeSlider.Value,
            Foreground = new SolidColorBrush(GetSelectedColor()),
            Background = WBrushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        textBox.TextChanged += (_, _) => { if (!_isRestoringSnapshot) PushSnapshot(); };
        textBox.LostFocus += (_, _) => { if (!_isRestoringSnapshot) PushSnapshot(); };

        Thumb resize = new()
        {
            Width = 12,
            Height = 12,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = System.Windows.Input.Cursors.SizeNWSE,
            Background = new SolidColorBrush(WColor.FromArgb(180, 87, 141, 255))
        };
        resize.DragDelta += (_, args) =>
        {
            container.Width = Math.Max(120, container.Width + args.HorizontalChange);
            container.Height = Math.Max(50, container.ActualHeight + args.VerticalChange);
            if (!_isRestoringSnapshot)
            {
                PushSnapshot();
            }
        };

        grid.Children.Add(textBox);
        grid.Children.Add(resize);
        container.Child = grid;
        container.MouseLeftButtonDown += TextBorder_MouseLeftButtonDown;
        container.MouseMove += TextBorder_MouseMove;
        container.MouseLeftButtonUp += TextBorder_MouseLeftButtonUp;

        TextLayer.Children.Add(container);
        Canvas.SetLeft(container, left);
        Canvas.SetTop(container, top);
        SelectTextBorder(container);
        if (pushSnapshot && !_isRestoringSnapshot)
        {
            PushSnapshot();
        }
    }

    private void TextBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        _draggingTextBorder = border;
        _dragStartPoint = e.GetPosition(TextLayer);
        _dragStartOrigin = new System.Windows.Point(Canvas.GetLeft(border), Canvas.GetTop(border));
        border.CaptureMouse();
        SelectTextBorder(border);
    }

    private void TextBorder_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_draggingTextBorder is null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        System.Windows.Point current = e.GetPosition(TextLayer);
        double newLeft = _dragStartOrigin.X + (current.X - _dragStartPoint.X);
        double newTop = _dragStartOrigin.Y + (current.Y - _dragStartPoint.Y);
        Canvas.SetLeft(_draggingTextBorder, Math.Max(0, newLeft));
        Canvas.SetTop(_draggingTextBorder, Math.Max(0, newTop));
    }

    private void TextBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_draggingTextBorder is null)
        {
            return;
        }

        _draggingTextBorder.ReleaseMouseCapture();
        _draggingTextBorder = null;
        PushSnapshot();
    }

    private void SelectTextBorder(Border border)
    {
        if (_selectedTextBorder is not null)
        {
            _selectedTextBorder.BorderBrush = WBrushes.DeepSkyBlue;
        }

        _selectedTextBorder = border;
        _selectedTextBorder.BorderBrush = WBrushes.Gold;
    }

    private void DeleteTextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTextBorder is null)
        {
            return;
        }

        TextLayer.Children.Remove(_selectedTextBorder);
        _selectedTextBorder = null;
        PushSnapshot();
    }

    private void ApplyStyleToSelectedText()
    {
        if (_selectedTextBorder?.Child is not Grid grid ||
            grid.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault() is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        textBox.FontSize = TextSizeSlider.Value;
        textBox.Foreground = new SolidColorBrush(GetSelectedColor());
        _selectedTextBorder.Width = TextWidthSlider.Value;
    }

    private WColor GetSelectedColor()
    {
        if (ColorComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem selected)
        {
            return WColors.OrangeRed;
        }

        return selected.Content?.ToString() switch
        {
            "Green" => WColors.LimeGreen,
            "Blue" => WColors.DeepSkyBlue,
            "Yellow" => WColors.Gold,
            "White" => WColors.White,
            _ => WColors.OrangeRed
        };
    }

    private void PushSnapshot() => _undoStack.Push(CaptureSnapshot());

    private EditorSnapshot CaptureSnapshot()
    {
        EditorSnapshot snapshot = new()
        {
            Strokes = InkLayer.Strokes.Clone(),
            TextItems = []
        };

        foreach (Border border in TextLayer.Children.OfType<Border>())
        {
            if (border.Child is not Grid grid ||
                grid.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault() is not System.Windows.Controls.TextBox textBox)
            {
                continue;
            }

            snapshot.TextItems.Add(new TextOverlayState
            {
                Left = Canvas.GetLeft(border),
                Top = Canvas.GetTop(border),
                Width = border.Width,
                Height = border.ActualHeight > 0 ? border.ActualHeight : 70,
                Text = textBox.Text,
                FontSize = textBox.FontSize,
                Color = ((SolidColorBrush)textBox.Foreground).Color
            });
        }

        return snapshot;
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        _isRestoringSnapshot = true;
        InkLayer.Strokes = snapshot.Strokes.Clone();
        TextLayer.Children.Clear();
        _selectedTextBorder = null;

        foreach (TextOverlayState state in snapshot.TextItems)
        {
            AddTextBox(state.Left, state.Top, state.Text, false);
            if (TextLayer.Children.OfType<Border>().LastOrDefault() is Border border &&
                border.Child is Grid grid &&
                grid.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault() is System.Windows.Controls.TextBox textBox)
            {
                border.Width = state.Width;
                border.Height = state.Height;
                textBox.FontSize = state.FontSize;
                textBox.Foreground = new SolidColorBrush(state.Color);
            }
        }

        _isRestoringSnapshot = false;
    }

    private sealed class EditorSnapshot
    {
        public required StrokeCollection Strokes { get; init; }
        public required List<TextOverlayState> TextItems { get; init; }
    }

    private sealed class TextOverlayState
    {
        public required double Left { get; init; }
        public required double Top { get; init; }
        public required double Width { get; init; }
        public required double Height { get; init; }
        public required string Text { get; init; }
        public required double FontSize { get; init; }
        public required WColor Color { get; init; }
    }

    private void ApplyZoom(double value)
    {
        _zoom = Math.Clamp(value, 0.2, 5);
        EditorScaleTransform.ScaleX = _zoom;
        EditorScaleTransform.ScaleY = _zoom;
        ZoomText.Text = $"{(int)(_zoom * 100)}%";
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoom * 1.1);

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoom / 1.1);

    private void EditorScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        ApplyZoom(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
        e.Handled = true;
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
}
