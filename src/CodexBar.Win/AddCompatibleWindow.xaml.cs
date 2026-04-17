using System.Windows;

namespace CodexBar.Win;

public partial class AddCompatibleWindow : Window
{
    public AddCompatibleResult? Result { get; private set; }

    public AddCompatibleWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProviderIdBox.Text) ||
            string.IsNullOrWhiteSpace(BaseUrlBox.Text) ||
            string.IsNullOrWhiteSpace(AccountIdBox.Text) ||
            string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            System.Windows.MessageBox.Show(this, "\u8BF7\u586B\u5199\u6240\u6709\u5E26 * \u7684\u5FC5\u586B\u9879\uFF1AProvider ID\u3001Base URL\u3001\u8D26\u53F7 ID \u548C API Key\u3002", "CodexBar");
            return;
        }

        var providerId = ProviderIdBox.Text.Trim();
        var accountId = AccountIdBox.Text.Trim();
        var providerName = string.IsNullOrWhiteSpace(ProviderNameBox.Text)
            ? providerId
            : ProviderNameBox.Text.Trim();
        var accountLabel = string.IsNullOrWhiteSpace(AccountLabelBox.Text)
            ? accountId
            : AccountLabelBox.Text.Trim();

        Result = new AddCompatibleResult(
            providerId,
            providerName,
            BaseUrlBox.Text.Trim(),
            accountId,
            accountLabel,
            ApiKeyBox.Password);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}

public sealed record AddCompatibleResult(
    string ProviderId,
    string ProviderName,
    string BaseUrl,
    string AccountId,
    string AccountLabel,
    string ApiKey);
