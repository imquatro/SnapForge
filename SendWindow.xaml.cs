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
        SendButton_Click(sender, e);
    }
}
