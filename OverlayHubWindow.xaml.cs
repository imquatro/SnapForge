using System.IO;
using System.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.Models;
using SnapForge.Services;

namespace SnapForge;

public partial class OverlayHubWindow : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly SettingsService _settingsService = new();
    private readonly UserSettings _settings;
    private readonly ObservableCollection<ScreenshotItem> _items;
    private ImageViewerPopupWindow? _viewerWindow;
    private SimpleImageEditorWindow? _editorWindow;
    private System.Windows.Controls.Border? _selectedTileBorder;
    private double _tileSize = 150;
    public double TileSize
    {
        get => _tileSize;
        set
        {
            if (Math.Abs(_tileSize - value) < 0.1)
            {
                return;
            }

            _tileSize = value;
            OnPropertyChanged();
        }
    }

    public OverlayHubWindow(IReadOnlyList<ScreenshotItem> items)
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        _items = new ObservableCollection<ScreenshotItem>(items);
        DataContext = this;
        ItemsList.ItemsSource = _items;
        SimpleImageEditorWindow.ImageSaved += OnEditorImageSaved;
        Closed += (_, _) => SimpleImageEditorWindow.ImageSaved -= OnEditorImageSaved;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
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
            System.Windows.MessageBox.Show("Could not open capture folder.", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border border || border.Tag is not string filePath || !File.Exists(filePath))
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && IsButtonClick(source))
        {
            return;
        }

        SetSelectedTile(border);
        if (_editorWindow is not null && _editorWindow.IsVisible)
        {
            _editorWindow.ShowImage(filePath);
        }
        else
        {
            OpenInViewer(filePath);
        }
        e.Handled = true;
    }

    private void OpenTileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string filePath } || !File.Exists(filePath))
        {
            return;
        }

        if (sender is DependencyObject source && FindTileBorder(source) is System.Windows.Controls.Border border)
        {
            SetSelectedTile(border);
        }

        OpenInViewer(filePath);
    }

    private void EditTileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string filePath } || !File.Exists(filePath))
        {
            return;
        }

        OpenInEditor(filePath);
    }

    private void DeleteTileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string filePath } || !File.Exists(filePath))
        {
            return;
        }

        bool shouldDelete = !_settings.AskBeforeDeleteImage;
        bool disableAsking = false;

        if (!shouldDelete)
        {
            DeleteConfirmWindow confirm = new(Path.GetFileName(filePath))
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
            File.Delete(filePath);
            ScreenshotItem? target = _items.FirstOrDefault(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                _items.Remove(target);
            }

            if (disableAsking && _settings.AskBeforeDeleteImage)
            {
                _settings.AskBeforeDeleteImage = false;
                _settingsService.Save(_settings);
            }
        }
        catch
        {
            System.Windows.MessageBox.Show("Could not delete this image.", "SnapForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenInViewer(string filePath)
    {
        if (_editorWindow is not null)
        {
            _editorWindow.Close();
            _editorWindow = null;
        }

        if (_viewerWindow is null || !_viewerWindow.IsVisible)
        {
            _viewerWindow = new ImageViewerPopupWindow(filePath)
            {
                Left = Left + Width + 12,
                Top = Top
            };
            _viewerWindow.Closed += (_, _) => _viewerWindow = null;
            _viewerWindow.Show();
            return;
        }

        _viewerWindow.ShowImage(filePath);
    }

    private void OpenInEditor(string filePath)
    {
        if (_viewerWindow is not null)
        {
            _viewerWindow.Close();
            _viewerWindow = null;
        }

        if (_editorWindow is null || !_editorWindow.IsVisible)
        {
            _editorWindow = new SimpleImageEditorWindow(filePath)
            {
                Left = Left + Width + 12,
                Top = Top
            };
            _editorWindow.Closed += (_, _) => _editorWindow = null;
            _editorWindow.Show();
            return;
        }

        _editorWindow.ShowImage(filePath);
    }

    private void SendTileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string filePath } || !File.Exists(filePath))
        {
            return;
        }

        if (sender is DependencyObject source && FindTileBorder(source) is System.Windows.Controls.Border border)
        {
            SetSelectedTile(border);
        }

        SendWindow sendWindow = new(filePath)
        {
            Left = Left + Width + 12,
            Top = Top + 50
        };
        sendWindow.Show();
    }

    private void Tile_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border border)
        {
            return;
        }

        SetHoverOverlayVisibility(border, border == _selectedTileBorder);
    }

    private void Tile_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border border)
        {
            return;
        }

        SetHoverOverlayVisibility(border, false);
    }

    private static void SetHoverOverlayVisibility(System.Windows.Controls.Border tileBorder, bool visible)
    {
        if (tileBorder.Child is not FrameworkElement root)
        {
            return;
        }

        if (root.FindName("HoverOverlay") is System.Windows.Controls.Border overlay)
        {
            overlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SetSelectedTile(System.Windows.Controls.Border border)
    {
        if (_selectedTileBorder is not null)
        {
            _selectedTileBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 60, 88));
            _selectedTileBorder.BorderThickness = new Thickness(1);
            SetHoverOverlayVisibility(_selectedTileBorder, false);
        }

        _selectedTileBorder = border;
        _selectedTileBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 210, 84));
        _selectedTileBorder.BorderThickness = new Thickness(2);
        SetHoverOverlayVisibility(_selectedTileBorder, true);
    }

    private static System.Windows.Controls.Border? FindTileBorder(DependencyObject source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Border border && border.Tag is string)
            {
                return border;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void OnEditorImageSaved(string savedPath)
    {
        Dispatcher.Invoke(() =>
        {
            if (!File.Exists(savedPath))
            {
                return;
            }

            _items.Insert(0, new ScreenshotItem
            {
                FilePath = savedPath,
                FileName = Path.GetFileName(savedPath),
                CreatedAt = File.GetCreationTime(savedPath),
                Thumbnail = CreateThumb(savedPath)
            });

            while (_items.Count > 80)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        });
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

    private void GalleryScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        TileSize = Math.Clamp(TileSize + (e.Delta > 0 ? 12 : -12), 150, 300);
        e.Handled = true;
    }

    private void ResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + e.HorizontalChange, 380, 1100);
        Height = Math.Clamp(Height + e.VerticalChange, 300, 920);
    }

    private void OnPropertyChanged([CallerMemberName] string? memberName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));

    private static bool IsButtonClick(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Button)
            {
                return true;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
