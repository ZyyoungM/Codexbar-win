using System.Windows;
using System.Windows.Media;
using CodexBar.Auth;
using CodexBar.Core;

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
            ShowStatus(
                "\u8BF7\u5B8C\u5584\u5FC5\u586B\u9879",
                "\u9700\u8981\u586B\u5199 Provider ID\u3001Base URL\u3001\u8D26\u53F7 ID \u548C API Key \u540E\u624D\u80FD\u4FDD\u5B58\u3002",
                isError: true);
            return;
        }

        var providerId = ProviderIdBox.Text.Trim();
        var accountId = AccountIdBox.Text.Trim();
        var providerName = string.IsNullOrWhiteSpace(ProviderNameBox.Text)
            ? providerId
            : ProviderNameBox.Text.Trim();
        var codexProviderId = string.IsNullOrWhiteSpace(CodexProviderIdBox.Text)
            ? null
            : CodexProviderIdBox.Text.Trim();
        var accountLabel = string.IsNullOrWhiteSpace(AccountLabelBox.Text)
            ? accountId
            : AccountLabelBox.Text.Trim();

        Result = new AddCompatibleResult(
            providerId,
            codexProviderId,
            providerName,
            BaseUrlBox.Text.Trim(),
            accountId,
            accountLabel,
            ApiKeyBox.Password);
        ShowStatus("\u5DF2\u51C6\u5907\u4FDD\u5B58", "\u6B63\u5728\u5173\u95ED\u7A97\u53E3\u5E76\u5199\u5165\u672C\u5730\u914D\u7F6E\u3002");
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BaseUrlBox.Text) || string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            ShowStatus(
                "\u65E0\u6CD5\u6D4B\u8BD5\u8FDE\u63A5",
                "\u8BF7\u5148\u586B\u5199 Base URL \u548C API Key\u3002",
                isError: true);
            return;
        }

        SetBusy(true);
        try
        {
            ShowStatus("\u6B63\u5728\u6D4B\u8BD5\u8FDE\u63A5", "\u6B63\u5728\u63A2\u6D4B /models \u8FDE\u901A\u60C5\u51B5\u2026");
            var provider = new ProviderDefinition
            {
                ProviderId = string.IsNullOrWhiteSpace(ProviderIdBox.Text) ? "compatible" : ProviderIdBox.Text.Trim(),
                CodexProviderId = string.IsNullOrWhiteSpace(CodexProviderIdBox.Text) ? "openai" : CodexProviderIdBox.Text.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(ProviderNameBox.Text) ? "Compatible API" : ProviderNameBox.Text.Trim(),
                Kind = ProviderKind.OpenAiCompatible,
                BaseUrl = BaseUrlBox.Text.Trim(),
                AuthMode = AuthMode.ApiKey,
                WireApi = WireApi.Responses,
                SupportsMultiAccount = true
            };
            var account = new AccountRecord
            {
                ProviderId = provider.ProviderId,
                AccountId = string.IsNullOrWhiteSpace(AccountIdBox.Text) ? "default" : AccountIdBox.Text.Trim(),
                Label = string.IsNullOrWhiteSpace(AccountLabelBox.Text) ? "Default" : AccountLabelBox.Text.Trim(),
                CredentialRef = "inline-probe"
            };
            var result = await new CompatibleProviderProbeService(new InlineSecretStore(ApiKeyBox.Password))
                .ProbeAccountAsync(provider, account);

            var message = string.IsNullOrWhiteSpace(result.SuggestedBaseUrl)
                ? result.Message
                : $"{result.Message}\n\u5EFA\u8BAE Base URL\uFF1A{result.SuggestedBaseUrl}";
            ShowStatus(
                result.Success ? "\u8FDE\u63A5\u53EF\u7528" : "\u8FDE\u63A5\u5931\u8D25",
                message,
                isError: !result.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(
                "\u6D4B\u8BD5\u8FDE\u63A5\u5931\u8D25",
                ex.Message,
                isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ProbeButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
    }

    private void ShowStatus(string title, string message, bool isError = false)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusPanel.Background = isError
            ? CreateBrush("#FEF6F6")
            : CreateBrush("#F7FAFF");
        StatusPanel.BorderBrush = isError
            ? CreateBrush("#F1B9B9")
            : CreateBrush("#CFE4F9");
        StatusTitleText.Foreground = isError
            ? CreateBrush("#C42B1C")
            : CreateBrush("#0F6CBD");
        StatusTitleText.Text = title;
        StatusBodyText.Text = message;
        StatusBodyText.Foreground = CreateBrush(isError ? "#7A2E24" : "#605E5C");
    }

    private static SolidColorBrush CreateBrush(string hex)
        => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);

    private sealed class InlineSecretStore(string secret) : ISecretStore
    {
        public Task WriteSecretAsync(string credentialRef, string secretValue, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string?> ReadSecretAsync(string credentialRef, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(secret);

        public Task DeleteSecretAsync(string credentialRef, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

public sealed record AddCompatibleResult(
    string ProviderId,
    string? CodexProviderId,
    string ProviderName,
    string BaseUrl,
    string AccountId,
    string AccountLabel,
    string ApiKey);
