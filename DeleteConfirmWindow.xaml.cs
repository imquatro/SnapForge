using System.Windows;

namespace SnapForge;

public partial class DeleteConfirmWindow : Window
{
    public bool ShouldDelete { get; private set; }
    public bool DoNotAskAgain => NotAskAgainCheckBox.IsChecked == true;

    public DeleteConfirmWindow(string fileName)
    {
        InitializeComponent();
        MessageText.Text = $"Delete '{fileName}' from local folder?";
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldDelete = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldDelete = false;
        DialogResult = false;
        Close();
    }
}
