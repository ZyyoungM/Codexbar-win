using System.Windows;

namespace CodexBar.Win;

public partial class RestartCodexConfirmationDialog : Window
{
    public RestartCodexConfirmationDialog()
    {
        InitializeComponent();
    }

    public bool DoNotAskAgain => DoNotAskAgainBox.IsChecked == true;

    private void Confirm_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
