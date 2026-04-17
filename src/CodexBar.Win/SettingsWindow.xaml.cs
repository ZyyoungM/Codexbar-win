using System.Diagnostics;
using System.Windows;
using CodexBar.Auth;
using CodexBar.CodexCompat;
using CodexBar.Core;
using CodexBar.Runtime;

namespace CodexBar.Win;

public partial class SettingsWindow : Window
{
    private sealed record OptionItem<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppPaths _appPaths;
    private readonly AppConfigStore _configStore;
    private readonly StartupRegistration _startup = new();
    private readonly WindowsCredentialSecretStore _secretStore = new();
    private AppConfig _config = AppConfigStore.DefaultConfig();

    public SettingsWindow()
    {
        InitializeComponent();
        _appPaths = AppPaths.Resolve();
        _appPaths.EnsureDirectories();
        _configStore = new AppConfigStore(_appPaths.ConfigPath);

        AccountSortModeBox.ItemsSource = BuildAccountSortModeOptions();
        ActivationBehaviorBox.ItemsSource = BuildActivationBehaviorOptions();
        OpenAiAccountModeBox.ItemsSource = BuildOpenAiModeOptions();
        Loaded += async (_, _) => await LoadConfigAsync();
    }

    private async Task LoadConfigAsync()
    {
        _config = await _configStore.LoadAsync();
        var home = new CodexHomeLocator().Resolve();
        PathsText.Text = $"\u5E94\u7528\u72B6\u6001\u76EE\u5F55\uFF1A{_appPaths.AppRoot}\nCODEX_HOME\uFF1A{home.RootPath}";

        SelectOption(AccountSortModeBox, _config.Settings.AccountSortMode);
        SelectOption(ActivationBehaviorBox, _config.Settings.ActivationBehavior);
        SelectOption(OpenAiAccountModeBox, _config.Settings.OpenAiAccountMode);
        CodexDesktopPathBox.Text = _config.Settings.CodexDesktopPath ?? "";
        CodexCliPathBox.Text = _config.Settings.CodexCliPath ?? "";
        StartupBox.IsChecked = _startup.IsEnabled();
        StatusText.Text = "\u5C31\u7EEA\u3002";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveConfigAsync(closeAfterSave: true);
    }

    private async Task SaveConfigAsync(bool closeAfterSave)
    {
        var executable = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            _startup.SetEnabled(StartupBox.IsChecked == true, executable);
        }

        _config = _config with
        {
            Settings = _config.Settings with
            {
                AccountSortMode = SelectedValue(AccountSortModeBox, AccountSortMode.Manual),
                ActivationBehavior = SelectedValue(ActivationBehaviorBox, ActivationBehavior.WriteConfigOnly),
                OpenAiAccountMode = SelectedValue(OpenAiAccountModeBox, OpenAiAccountMode.ManualSwitch),
                CodexDesktopPath = EmptyToNull(CodexDesktopPathBox.Text),
                CodexCliPath = EmptyToNull(CodexCliPathBox.Text)
            }
        };

        await _configStore.SaveAsync(_config);
        StatusText.Text = "\u5DF2\u4FDD\u5B58\u3002\u5982\u679C\u4F60\u5728\u5176\u4ED6\u5730\u65B9\u4FEE\u6539\u4E86\u8D26\u53F7\u6570\u636E\uFF0C\u8BF7\u5237\u65B0\u4E3B\u9762\u677F\u3002";

        if (closeAfterSave)
        {
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => Close();

    private void BrowseDesktop_Click(object sender, RoutedEventArgs e)
    {
        var path = PickExecutable("\u9009\u62E9 Codex Desktop \u53EF\u6267\u884C\u6587\u4EF6");
        if (path is not null)
        {
            CodexDesktopPathBox.Text = path;
        }
    }

    private void BrowseCli_Click(object sender, RoutedEventArgs e)
    {
        var path = PickExecutable("\u9009\u62E9 Codex CLI \u53EF\u6267\u884C\u6587\u4EF6");
        if (path is not null)
        {
            CodexCliPathBox.Text = path;
        }
    }

    private void DetectDesktop_Click(object sender, RoutedEventArgs e)
    {
        var detected = new CodexDesktopLocator().Locate(EmptyToNull(CodexDesktopPathBox.Text));
        if (detected is not null)
        {
            CodexDesktopPathBox.Text = detected;
            DetectionText.Text = $"\u5DF2\u63A2\u6D4B\u5230 Codex Desktop\uFF1A{detected}";
            return;
        }

        DetectionText.Text = "\u672A\u627E\u5230 Codex Desktop\u3002";
    }

    private async void DetectCli_Click(object sender, RoutedEventArgs e)
    {
        var detected = await new CodexCliLocator().LocateAsync(EmptyToNull(CodexCliPathBox.Text));
        if (detected is not null)
        {
            CodexCliPathBox.Text = detected.Path;
            DetectionText.Text = $"\u5DF2\u63A2\u6D4B\u5230 Codex CLI\uFF1A{detected.Path}\n\u7248\u672C\uFF1A{detected.Version ?? "\uFF08\u672A\u77E5\uFF09"}";
            return;
        }

        DetectionText.Text = "\u672A\u627E\u5230 Codex CLI\u3002";
    }

    private async void LaunchCodex_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveConfigAsync(closeAfterSave: false);
            if (!await TryRewriteActiveSelectionAsync())
            {
                return;
            }

            var launchResult = await new CodexLaunchService().LaunchAsync(_config.Settings);
            StatusText.Text = launchResult.Launched
                ? launchResult.Message
                : $"\u542F\u52A8 Codex \u5931\u8D25\uFF1A{launchResult.Message}";
        }
        catch (Exception ex)
        {
            StatusText.Text = DiagnosticLogger.Redact(ex.Message);
        }
    }

    private async Task<bool> TryRewriteActiveSelectionAsync()
    {
        _config = await _configStore.LoadAsync();
        if (_config.ActiveSelection is null)
        {
            StatusText.Text = "\u8BF7\u5148\u5728\u4E3B\u9762\u677F\u9009\u62E9\u4E00\u4E2A\u8D26\u53F7\u5E76\u70B9\u51FB\u201C\u4F7F\u7528\u201D\uFF0C\u7136\u540E\u518D\u542F\u52A8 Codex\u3002";
            return false;
        }

        var decision = await new OpenAiAggregateGatewayService(_appPaths, _secretStore)
            .ResolveSelectionAsync(_config, _config.ActiveSelection);
        var service = new CodexActivationService(
            new CodexHomeLocator(),
            new CodexConfigStore(),
            new CodexAuthStore(),
            new CodexStateTransaction(_appPaths),
            new CodexIntegrityChecker(),
            _secretStore,
            _secretStore);
        var result = await service.ActivateAsync(_config, decision.ResolvedSelection);
        var journalMessage = decision.WasRerouted
            ? $"{decision.Message} {result.Message}"
            : result.Message;
        await new SwitchJournalStore(_appPaths.SwitchJournalPath)
            .AppendAsync(result.Selection, result.ValidationPassed ? "ok" : "failed", journalMessage);

        if (!result.ValidationPassed)
        {
            StatusText.Text = $"\u540C\u6B65\u5F53\u524D\u8D26\u53F7\u5931\u8D25\uFF1A{result.Message}";
            return false;
        }

        var activatedSelection = result.Selection;
        _config = _config with
        {
            ActiveSelection = activatedSelection,
            Accounts = _config.Accounts
                .Select(account => account.ProviderId == activatedSelection.ProviderId && account.AccountId == activatedSelection.AccountId
                    ? account with { LastUsedAt = DateTimeOffset.UtcNow }
                    : account)
                .ToList()
        };
        await _configStore.SaveAsync(_config);
        return true;
    }

    private async void ExportAccounts_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveConfigAsync(closeAfterSave: false);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "\u5BFC\u51FA\u8D26\u53F7 CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = IncludeSecretsBox.IsChecked == true ? "codexbar-accounts-with-secrets.csv" : "codexbar-accounts.csv"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            await new AccountCsvService(_secretStore, _secretStore)
                .ExportAsync(_config, dialog.FileName, new AccountCsvExportOptions(IncludeSecretsBox.IsChecked == true));
            StatusText.Text = IncludeSecretsBox.IsChecked == true
                ? $"\u5DF2\u5BFC\u51FA\u5305\u542B\u5BC6\u94A5\u7684\u8D26\u53F7\u6587\u4EF6\uFF1A{dialog.FileName}"
                : $"\u5DF2\u5BFC\u51FA\u8D26\u53F7\u5143\u6570\u636E\uFF1A{dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = DiagnosticLogger.Redact(ex.Message);
        }
    }

    private async void ImportAccounts_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "\u5BFC\u5165\u8D26\u53F7 CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _config = await _configStore.LoadAsync();
            var (updatedConfig, result) = await new AccountCsvService(_secretStore, _secretStore)
                .ImportAsync(_config, dialog.FileName);
            _config = updatedConfig;
            await _configStore.SaveAsync(_config);

            var warnings = result.Warnings.Count == 0 ? "" : "\n" + string.Join("\n", result.Warnings);
            StatusText.Text = $"\u5DF2\u5BFC\u5165 Provider\uFF1A{result.ProvidersImported}\uFF1B\u8D26\u53F7\uFF1A{result.AccountsImported}\uFF1B\u5BC6\u94A5\uFF1A{result.SecretsImported}\u3002{warnings}\n\u8BF7\u5237\u65B0\u4E3B\u9762\u677F\u4EE5\u67E5\u770B\u65B0\u5BFC\u5165\u7684\u8D26\u53F7\u3002";
        }
        catch (Exception ex)
        {
            StatusText.Text = DiagnosticLogger.Redact(ex.Message);
        }
    }

    private static string? PickExecutable(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "\u53EF\u6267\u884C\u6587\u4EF6 (*.exe)|*.exe|All files (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<OptionItem<AccountSortMode>> BuildAccountSortModeOptions()
        =>
        [
            new(AccountSortMode.Manual, "\u6309\u624B\u52A8\u987A\u5E8F"),
            new(AccountSortMode.Usage, "\u6309\u7528\u91CF\u4E0E\u5269\u4F59\u989D\u5EA6")
        ];

    private static IReadOnlyList<OptionItem<ActivationBehavior>> BuildActivationBehaviorOptions()
        =>
        [
            new(ActivationBehavior.WriteConfigOnly, "\u53EA\u6539\u914D\u7F6E\uFF08\u4E0D\u542F\u52A8 Codex\uFF09"),
            new(ActivationBehavior.LaunchNewCodex, "\u6FC0\u6D3B\u540E\u542F\u52A8\u65B0\u7684 Codex")
        ];

    private static IReadOnlyList<OptionItem<OpenAiAccountMode>> BuildOpenAiModeOptions()
        =>
        [
            new(OpenAiAccountMode.ManualSwitch, "\u624B\u52A8\u5207\u6362"),
            new(OpenAiAccountMode.AggregateGateway, "\u805A\u5408\u7F51\u5173")
        ];

    private static void SelectOption<T>(System.Windows.Controls.ComboBox comboBox, T value)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<OptionItem<T>>()
            .FirstOrDefault(item => EqualityComparer<T>.Default.Equals(item.Value, value));
    }

    private static T SelectedValue<T>(System.Windows.Controls.ComboBox comboBox, T fallback)
        => comboBox.SelectedItem is OptionItem<T> option ? option.Value : fallback;
}
