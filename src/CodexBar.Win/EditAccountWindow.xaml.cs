using System.Windows;
using System.Windows.Media;
using CodexBar.Auth;
using CodexBar.Core;

namespace CodexBar.Win;

public partial class EditAccountWindow : Window
{
    private readonly ProviderKind _providerKind;
    private readonly string _originalProviderId;
    private readonly string _originalAccountId;
    private readonly ProviderDefinition _provider;
    private readonly AccountRecord _account;
    private readonly WindowsCredentialSecretStore _secretStore = new();

    public EditAccountResult? Result { get; private set; }

    public EditAccountWindow(ProviderDefinition provider, AccountRecord account)
    {
        InitializeComponent();

        _provider = provider;
        _account = account;
        _providerKind = provider.Kind;
        _originalProviderId = provider.ProviderId;
        _originalAccountId = account.AccountId;
        ProviderIdBox.Text = provider.ProviderId;
        CodexProviderIdBox.Text = provider.CodexProviderId ?? (provider.Kind == ProviderKind.OpenAiCompatible ? "openai" : provider.ProviderId);
        ProviderNameBox.Text = provider.DisplayName;
        BaseUrlBox.Text = provider.BaseUrl ?? "";
        AccountIdBox.Text = account.AccountId;
        AccountLabelBox.Text = account.Label;
        AccountKindBadgeText.Text = provider.Kind == ProviderKind.OpenAiOAuth ? "OpenAI OAuth" : "兼容 Provider";

        if (_providerKind == ProviderKind.OpenAiOAuth)
        {
            ProviderIdBox.IsReadOnly = true;
            CompatiblePanel.Visibility = Visibility.Collapsed;
            ProviderNameBox.IsReadOnly = true;
            OfficialUsagePanel.Visibility = Visibility.Visible;
            OfficialPlanBox.Text = FormatPlan(account);
            FiveHourQuotaBox.Text = OpenAiQuotaDisplayFormatter.FormatDetailedRemaining(account.FiveHourQuota, "5h");
            WeeklyQuotaBox.Text = OpenAiQuotaDisplayFormatter.FormatDetailedRemaining(account.WeeklyQuota, "\u5468");
            OfficialStatusBox.Text = BuildOfficialStatus(account);
            ProbeButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            OfficialUsagePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProviderIdBox.Text))
        {
            ShowStatus("\u65E0\u6CD5\u4FDD\u5B58", "\u8BF7\u8F93\u5165 Provider ID\u3002", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(AccountLabelBox.Text))
        {
            ShowStatus("\u65E0\u6CD5\u4FDD\u5B58", "\u8BF7\u8F93\u5165\u8D26\u53F7\u663E\u793A\u540D\u3002", isError: true);
            return;
        }

        if (_providerKind == ProviderKind.OpenAiCompatible &&
            (string.IsNullOrWhiteSpace(ProviderNameBox.Text) || string.IsNullOrWhiteSpace(BaseUrlBox.Text)))
        {
            ShowStatus("\u65E0\u6CD5\u4FDD\u5B58", "\u8BF7\u8F93\u5165 Provider \u540D\u79F0\u548C Base URL\u3002", isError: true);
            return;
        }

        Result = new EditAccountResult(
            _originalProviderId,
            _originalAccountId,
            ProviderIdBox.Text.Trim(),
            string.IsNullOrWhiteSpace(CodexProviderIdBox.Text) ? null : CodexProviderIdBox.Text.Trim(),
            AccountIdBox.Text.Trim(),
            ProviderNameBox.Text.Trim(),
            BaseUrlBox.Text.Trim(),
            AccountLabelBox.Text.Trim(),
            ApiKeyBox.Password);
        ShowStatus("\u5DF2\u51C6\u5907\u4FDD\u5B58", "\u6B63\u5728\u5173\u95ED\u7A97\u53E3\u5E76\u5199\u5165\u672C\u5730\u914D\u7F6E\u3002", isSuccess: true);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        if (_providerKind != ProviderKind.OpenAiCompatible)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(BaseUrlBox.Text))
        {
            ShowStatus("\u65E0\u6CD5\u6D4B\u8BD5\u8FDE\u63A5", "\u8BF7\u5148\u586B\u5199 Base URL\u3002", isError: true);
            return;
        }

        SetBusy(true);
        try
        {
            ShowStatus("\u6B63\u5728\u6D4B\u8BD5\u8FDE\u63A5", "\u6B63\u5728\u63A2\u6D4B /models \u8FDE\u901A\u60C5\u51B5\u2026");
            var provider = _provider with
            {
                ProviderId = ProviderIdBox.Text.Trim(),
                DisplayName = ProviderNameBox.Text.Trim(),
                BaseUrl = BaseUrlBox.Text.Trim(),
                CodexProviderId = string.IsNullOrWhiteSpace(CodexProviderIdBox.Text) ? null : CodexProviderIdBox.Text.Trim()
            };
            var account = _account with
            {
                ProviderId = provider.ProviderId,
                Label = AccountLabelBox.Text.Trim()
            };

            CompatibleProviderProbeResult result;
            if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
            {
                result = await new CompatibleProviderProbeService(new InlineSecretStore(ApiKeyBox.Password))
                    .ProbeAccountAsync(provider, account);
            }
            else
            {
                result = await new CompatibleProviderProbeService(_secretStore)
                    .ProbeAccountAsync(provider, account);
            }

            var message = string.IsNullOrWhiteSpace(result.SuggestedBaseUrl)
                ? result.Message
                : $"{result.Message}{Environment.NewLine}\u5EFA\u8BAE Base URL\uFF1A{result.SuggestedBaseUrl}";
            ShowStatus(
                result.Success ? "\u8FDE\u63A5\u53EF\u7528" : "\u8FDE\u63A5\u5931\u8D25",
                message,
                isError: !result.Success,
                isSuccess: result.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("\u6D4B\u8BD5\u8FDE\u63A5\u5931\u8D25", ex.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string FormatPlan(AccountRecord account)
    {
        if (account.Tier != AccountTier.Unknown)
        {
            return account.Tier.ToString().ToLowerInvariant();
        }

        return string.IsNullOrWhiteSpace(account.OfficialPlanTypeRaw)
            ? "\u5C1A\u672A\u83B7\u53D6"
            : $"\u672A\u77E5\uFF08{account.OfficialPlanTypeRaw}\uFF09";
    }

    private static string BuildOfficialStatus(AccountRecord account)
    {
        var lines = new List<string>();
        lines.Add(account.OfficialUsageFetchedAt.HasValue
            ? $"\u4E0A\u6B21\u83B7\u53D6\uFF1A{account.OfficialUsageFetchedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
            : "\u4E0A\u6B21\u83B7\u53D6\uFF1A\u5C1A\u672A\u83B7\u53D6");
        if (!string.IsNullOrWhiteSpace(account.OfficialUsageError))
        {
            lines.Add(account.OfficialUsageError);
        }
        else
        {
            lines.Add("\u6765\u6E90\uFF1AOpenAI \u5B98\u65B9\u4E00\u65B9\u989D\u5EA6\u63A5\u53E3\u3002");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void SetBusy(bool busy)
    {
        ProbeButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
    }

    private void ShowStatus(string title, string message, bool isError = false, bool isSuccess = false)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusPanel.Background = CreateBrush(isError ? "#FEF6F6" : isSuccess ? "#F3FBF3" : "#F7FAFF");
        StatusPanel.BorderBrush = CreateBrush(isError ? "#F1B9B9" : isSuccess ? "#B7E0B8" : "#CFE4F9");
        StatusTitleText.Foreground = CreateBrush(isError ? "#C42B1C" : isSuccess ? "#107C10" : "#0F6CBD");
        StatusTitleText.Text = title;
        StatusBodyText.Foreground = CreateBrush(isError ? "#7A2E24" : "#605E5C");
        StatusBodyText.Text = message;
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

public sealed record EditAccountResult(
    string OriginalProviderId,
    string OriginalAccountId,
    string ProviderId,
    string? CodexProviderId,
    string AccountId,
    string ProviderName,
    string BaseUrl,
    string AccountLabel,
    string ApiKey);
