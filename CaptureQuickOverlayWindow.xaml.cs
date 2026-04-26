using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapForge.Models;
using SnapForge.Services;

namespace SnapForge;

public partial class CaptureQuickOverlayWindow : Window
{
    private string _filePath;
    private readonly OverlayQuickSettingsService _overlaySettingsService = new();
    private readonly OverlayQuickSettings _overlaySettings;
    private readonly DispatcherTimer _dismissTimer = new();
    private bool _isDragging;
    private System.Windows.Point _dragStartMouse;
    private System.Windows.Point _dragStartWindow;

    public CaptureQuickOverlayWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        _overlaySettings = _overlaySettingsService.Load();

        Opacity = _overlaySettings.Opacity;
        OpacitySlider.Value = _overlaySettings.Opacity;
        AutoDismissCheckBox.IsChecked = _overlaySettings.AutoDismiss;
        DismissMsSlider.Value = _overlaySettings.AutoDismissMs;

        FileNameText.Text = Path.GetFileName(_filePath);
        TrySetPreviewImage(_filePath);

        PositionFromSettings();
        SetupDismissTimer();
        LocationChanged += (_, _) => PersistPosition();
    }

    public void UpdateCapture(string filePath)
    {
        _filePath = filePath;
        FileNameText.Text = Path.GetFileName(_filePath);
        TrySetPreviewImage(_filePath);
        RestartDismissTimer();
    }

    private void TrySetPreviewImage(string filePath)
    {
        try
        {
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
        }
        catch
        {
            PreviewImage.Source = null;
        }
    }

    private void PositionFromSettings()
    {
        Left = SystemParameters.WorkArea.Left + 16;
        Top = SystemParameters.WorkArea.Top + ((SystemParameters.WorkArea.Height - Height) / 2);
    }

    private void PersistPosition()
    {
        _overlaySettings.Left = Left;
        _overlaySettings.Top = Top;
        _overlaySettingsService.Save(_overlaySettings);
    }

    private void SetupDismissTimer()
    {
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            if (AutoDismissCheckBox.IsChecked == true)
            {
                Close();
            }
        };
        RestartDismissTimer();
    }

    private void RestartDismissTimer()
    {
        _dismissTimer.Stop();
        if (AutoDismissCheckBox.IsChecked != true)
        {
            return;
        }

        _dismissTimer.Interval = TimeSpan.FromMilliseconds(DismissMsSlider.Value);
        _dismissTimer.Start();
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (IsInteractiveControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _isDragging = true;
        _dragStartMouse = PointToScreen(e.GetPosition(this));
        _dragStartWindow = new System.Windows.Point(Left, Top);
        RootBorder.CaptureMouse();
    }

    private void RootBorder_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        System.Windows.Point mouse = PointToScreen(e.GetPosition(this));
        Left = _dragStartWindow.X + (mouse.X - _dragStartMouse.X);
        Top = _dragStartWindow.Y + (mouse.Y - _dragStartMouse.Y);
    }

    private void RootBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        RootBorder.ReleaseMouseCapture();
        PersistPosition();
    }

    private static bool IsInteractiveControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Button ||
                source is System.Windows.Controls.Slider ||
                source is System.Windows.Controls.CheckBox ||
                source is System.Windows.Controls.TextBox ||
                source is System.Windows.Controls.ComboBox ||
                source is System.Windows.Controls.Primitives.Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void SettingsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        Height = SettingsPanel.Visibility == Visibility.Visible ? 320 : 228;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + e.HorizontalChange, 300, 720);
        Height = Math.Clamp(Height + e.VerticalChange, 210, 700);
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ImageViewerPopupWindow viewer = new(_filePath)
            {
                Left = Left + Width + 12,
                Top = Top
            };
            viewer.Show();
        }
        catch
        {
            Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true });
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SimpleImageEditorWindow editor = new(_filePath)
            {
                Left = Left + Width + 12,
                Top = Top
            };
            editor.Show();
        }
        catch
        {
            System.Windows.MessageBox.Show("Editor could not open for this image.", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        new SendWindow(_filePath).Show();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(_filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            System.Windows.Clipboard.SetImage(bitmap);
        }
        catch
        {
            // ignore clipboard failures
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Opacity = e.NewValue;
        _overlaySettings.Opacity = e.NewValue;
        _overlaySettingsService.Save(_overlaySettings);
    }

    private void AutoDismiss_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _overlaySettings.AutoDismiss = AutoDismissCheckBox.IsChecked == true;
        _overlaySettingsService.Save(_overlaySettings);
        RestartDismissTimer();
    }

    private void DismissMsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _overlaySettings.AutoDismissMs = (int)e.NewValue;
        _overlaySettingsService.Save(_overlaySettings);
        RestartDismissTimer();
    }

    protected override void OnClosed(EventArgs e)
    {
        _dismissTimer.Stop();
        base.OnClosed(e);
    }
}
