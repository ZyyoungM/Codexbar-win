using System.Windows;
using CodexBar.Core;

namespace CodexBar.Win;

public partial class EditAccountWindow : Window
{
    private readonly ProviderKind _providerKind;

    public EditAccountResult? Result { get; private set; }

    public EditAccountWindow(ProviderDefinition provider, AccountRecord account)
    {
        InitializeComponent();

        _providerKind = provider.Kind;
        ProviderIdBox.Text = provider.ProviderId;
        ProviderNameBox.Text = provider.DisplayName;
        BaseUrlBox.Text = provider.BaseUrl ?? "";
        AccountIdBox.Text = account.AccountId;
        AccountLabelBox.Text = account.Label;

        if (_providerKind == ProviderKind.OpenAiOAuth)
        {
            ProviderNameBox.IsReadOnly = true;
            BaseUrlBox.Visibility = Visibility.Collapsed;
            BaseUrlLabel.Visibility = Visibility.Collapsed;
            OfficialUsagePanel.Visibility = Visibility.Visible;
            OfficialPlanBox.Text = FormatPlan(account);
            FiveHourQuotaBox.Text = OpenAiQuotaDisplayFormatter.FormatDetailedRemaining(account.FiveHourQuota, "5h");
            WeeklyQuotaBox.Text = OpenAiQuotaDisplayFormatter.FormatDetailedRemaining(account.WeeklyQuota, "\u5468");
            OfficialStatusBox.Text = BuildOfficialStatus(account);
            ApiKeyBox.Visibility = Visibility.Collapsed;
            ApiKeyLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AccountLabelBox.Text))
        {
            System.Windows.MessageBox.Show(this, "\u8BF7\u8F93\u5165\u8D26\u53F7\u663E\u793A\u540D\u3002", "CodexBar");
            return;
        }

        if (_providerKind == ProviderKind.OpenAiCompatible &&
            (string.IsNullOrWhiteSpace(ProviderNameBox.Text) || string.IsNullOrWhiteSpace(BaseUrlBox.Text)))
        {
            System.Windows.MessageBox.Show(this, "\u8BF7\u8F93\u5165 Provider \u540D\u79F0\u548C Base URL\u3002", "CodexBar");
            return;
        }

        Result = new EditAccountResult(
            ProviderIdBox.Text.Trim(),
            AccountIdBox.Text.Trim(),
            ProviderNameBox.Text.Trim(),
            BaseUrlBox.Text.Trim(),
            AccountLabelBox.Text.Trim(),
            ApiKeyBox.Password);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

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
}

public sealed record EditAccountResult(
    string ProviderId,
    string AccountId,
    string ProviderName,
    string BaseUrl,
    string AccountLabel,
    string ApiKey);
