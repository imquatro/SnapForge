using System.Windows;
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
}
