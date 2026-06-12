using System.IO;
using System.Windows;
using System.Windows.Input;
using SnapForge.Services;

namespace SnapForge;

public partial class SendWindow : Window
{
    private readonly string _filePath;
    private readonly WindowDiscoveryService _windowDiscoveryService = new();
    private readonly SendAutomationService _sendAutomationService = new();

    public SendWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        FileNameText.Text = Path.GetFileName(filePath);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        LoadWindows();
    }

    private void LoadWindows()
    {
        WindowsListBox.ItemsSource = _windowDiscoveryService.GetOpenWindows();
        UpdateSelectionPreview(null);
    }

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void WindowsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateSelectionPreview(WindowsListBox.SelectedItem as WindowDiscoveryService.OpenWindowInfo);
    }

    private void UpdateSelectionPreview(WindowDiscoveryService.OpenWindowInfo? selected)
    {
        bool hasSelection = selected is not null;
        SelectedPreviewBorder.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        SendButton.IsEnabled = hasSelection;

        if (!hasSelection || selected is null)
        {
            StatusText.Text = string.Empty;
            return;
        }

        SelectedPreviewIcon.Source = selected.IconSource;
        SelectedPreviewProcess.Text = selected.ProcessName;
        SelectedPreviewTitle.Text = selected.Title;
        StatusText.Text = "Target selected.";
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadWindows();
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowsListBox.SelectedItem is not WindowDiscoveryService.OpenWindowInfo selected)
        {
            StatusText.Text = "Select a window first.";
            return;
        }

        bool sent = _sendAutomationService.TrySendImageToWindow(selected.Handle, _filePath);
        StatusText.Text = sent
            ? $"Sent to {selected.ProcessName}."
            : "Send failed on selected window.";
        if (sent)
        {
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void WindowsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WindowsListBox.SelectedItem is null)
        {
            return;
        }

        SendButton_Click(sender, e);
    }
}
