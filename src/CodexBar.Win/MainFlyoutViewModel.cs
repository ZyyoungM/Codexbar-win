using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using CodexBar.Auth;
using CodexBar.CodexCompat;
using CodexBar.Core;
using CodexBar.Runtime;

namespace CodexBar.Win;

public sealed class MainFlyoutViewModel : INotifyPropertyChanged
{
    private readonly AppPaths _appPaths = AppPaths.Resolve();
    private readonly AppConfigStore _appConfigStore;
    private readonly WindowsCredentialSecretStore _secretStore = new();
    private readonly CodexHomeLocator _homeLocator = new();
    private readonly DiagnosticLogger _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private int _busyDepth;
    private string _statusText = "\u6B63\u5728\u52A0\u8F7D...";
    private string _activityText = "";
    private string _usageText = "";
    private string _quotaStatusText = "";
    private string _lastRefreshText = "\u4E0A\u6B21\u5237\u65B0\uFF1A\u5C1A\u672A\u5237\u65B0";
    private string _errorText = "";
    private AppConfig _config = AppConfigStore.DefaultConfig();
    private UsageDashboard _lastUsageDashboard = new();
    private ActiveAccountSnapshot _activeAccount = ActiveAccountSnapshot.Empty;
    private string _routingModeText = "OpenAI \u8DEF\u7531\uFF1A\u624B\u52A8\u5207\u6362";
    private string _footnoteText = "\u5207\u6362\u4EC5\u5F71\u54CD\u65B0\u4F1A\u8BDD \u00B7 \u73B0\u6709\u4F1A\u8BDD\u4FDD\u6301\u4E0D\u53D8";

    public MainFlyoutViewModel()
    {
        _appPaths.EnsureDirectories();
        _appConfigStore = new AppConfigStore(_appPaths.ConfigPath);
        _logger = new DiagnosticLogger(_appPaths);
    }

    public ObservableCollection<AccountListItem> Accounts { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string UsageText
    {
        get => _usageText;
        private set => SetField(ref _usageText, value);
    }

    public bool CanInteract => _busyDepth == 0;

    public string ActivityText
    {
        get => _activityText;
        private set
        {
            if (SetField(ref _activityText, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActivityText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasInlineActivityText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDetailedActivityText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDetailedFeedbackText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFeedbackText)));
            }
        }
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetField(ref _errorText, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrorText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDetailedFeedbackText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFeedbackText)));
            }
        }
    }

    public string QuotaStatusText
    {
        get => _quotaStatusText;
        private set
        {
            if (SetField(ref _quotaStatusText, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasQuotaStatusText)));
            }
        }
    }

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set => SetField(ref _lastRefreshText, value);
    }

    public ActiveAccountSnapshot ActiveAccount
    {
        get => _activeAccount;
        private set
        {
            if (SetField(ref _activeAccount, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveAccount)));
            }
        }
    }

    public bool HasActiveAccount => ActiveAccount.HasSelection;

    public string RoutingModeText
    {
        get => _routingModeText;
        private set => SetField(ref _routingModeText, value);
    }

    public string FootnoteText
    {
        get => _footnoteText;
        private set => SetField(ref _footnoteText, value);
    }

    public bool HasActivityText => !string.IsNullOrWhiteSpace(ActivityText);

    public bool HasInlineActivityText => HasActivityText && !ActivityText.Contains('\n');

    public bool HasDetailedActivityText => HasActivityText && ActivityText.Contains('\n');

    public bool HasDetailedFeedbackText => HasDetailedActivityText || HasErrorText;

    public bool HasErrorText => !string.IsNullOrWhiteSpace(ErrorText);

    public bool HasFeedbackText => HasActivityText || HasErrorText;

    public bool HasQuotaStatusText => !string.IsNullOrWhiteSpace(QuotaStatusText);

    public bool IsAutomaticRouting => _config.Settings.OpenAiAccountMode == OpenAiAccountMode.AggregateGateway;

    public bool IsManualRouting => !IsAutomaticRouting;

    public string RoutingModeBadgeText => BuildRoutingModeBadgeText(_config.Settings.OpenAiAccountMode);

    public string RoutingDescriptionText => BuildRoutingDescriptionText(_config.Settings.OpenAiAccountMode);

    public async Task LoadInitialAsync()
    {
        using var _ = EnterBusy();
        await _refreshGate.WaitAsync();
        try
        {
            ErrorText = "";
            ActivityText = "\u6B63\u5728\u52A0\u8F7D\u914D\u7F6E...";
            _config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1), refreshOfficialUsage: false);
            var home = _homeLocator.Resolve();
            var usageDashboard = await new UsageAttributionService(
                new UsageScanner(),
                new SwitchJournalStore(_appPaths.SwitchJournalPath))
                .BuildDashboardAsync(_config, home, DateTimeOffset.Now);
            ApplyViewState(home, usageDashboard);
            RefreshLastRefreshText(DateTimeOffset.Now);
            ActivityText = "";
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.initial_load_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u52A0\u8F7D\u5931\u8D25";
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task RefreshOfficialQuotaInBackgroundAsync()
    {
        if (!ShouldRefreshOfficialUsage(_config, TimeSpan.FromMinutes(1)))
        {
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            ActivityText = "\u6B63\u5728\u540C\u6B65\u5B98\u65B9\u989D\u5EA6...";
            var officialUsageRefresh = await new OpenAiOfficialUsageService(_secretStore)
                .RefreshAsync(_config, TimeSpan.FromMinutes(1));
            if (officialUsageRefresh.Changed)
            {
                _config = await MergeOfficialUsageAccountsAsync(officialUsageRefresh.Config.Accounts);
                ApplyViewState(_homeLocator.Resolve(), _lastUsageDashboard);
                RefreshLastRefreshText(DateTimeOffset.Now);
            }

            ActivityText = officialUsageRefresh.AccountsRefreshed > 0
                ? "\u5B98\u65B9\u989D\u5EA6\u5DF2\u540C\u6B65\u3002"
                : "";
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.background_official_refresh_failed", ex);
            ActivityText = "";
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task RefreshAsync(string? startActivity = null, string? completedActivity = null)
    {
        using var _ = EnterBusy();
        await _refreshGate.WaitAsync();
        try
        {
            var refreshedAt = DateTimeOffset.Now;
            ErrorText = "";
            ActivityText = startActivity ?? "\u6B63\u5728\u5237\u65B0...";
            _config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1), refreshOfficialUsage: true);
            var home = _homeLocator.Resolve();
            var usageDashboard = await new UsageAttributionService(
                new UsageScanner(),
                new SwitchJournalStore(_appPaths.SwitchJournalPath))
                .BuildDashboardAsync(_config, home, DateTimeOffset.Now);
            ApplyViewState(home, usageDashboard);
            RefreshLastRefreshText(refreshedAt);
            ActivityText = completedActivity ?? "";
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.refresh_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u5237\u65B0\u5931\u8D25";
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task UseAsync(AccountListItem item)
        => await ActivateSelectionAsync(item, forceLaunch: false);

    public async Task LaunchCodexAsync(AccountListItem? item)
    {
        using var _ = EnterBusy();
        try
        {
            ErrorText = "";
            ActivityText = "\u6B63\u5728\u51C6\u5907\u542F\u52A8 Codex...";
            _config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1), refreshOfficialUsage: false);
            if (item is not null)
            {
                await ActivateSelectionAsync(item, forceLaunch: true);
                return;
            }

            if (_config.ActiveSelection is null)
            {
                ErrorText = "\u8BF7\u5148\u9009\u62E9\u4E00\u4E2A\u8D26\u53F7\uFF0C\u6216\u5148\u70B9\u51FB\u201C\u4F7F\u7528\u201D\u6FC0\u6D3B\u8D26\u53F7\u540E\u518D\u542F\u52A8 Codex\u3002";
                return;
            }

            var activeAccount = _config.Accounts.FirstOrDefault(account =>
                string.Equals(account.ProviderId, _config.ActiveSelection.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(account.AccountId, _config.ActiveSelection.AccountId, StringComparison.OrdinalIgnoreCase));
            if (activeAccount is null)
            {
                ErrorText = "\u5F53\u524D\u6FC0\u6D3B\u8D26\u53F7\u5DF2\u4E0D\u5B58\u5728\uFF0C\u8BF7\u5148\u91CD\u65B0\u9009\u62E9\u4E00\u4E2A\u8D26\u53F7\u3002";
                return;
            }

            await ActivateSelectionAsync(new AccountListItem
            {
                ProviderId = activeAccount.ProviderId,
                AccountId = activeAccount.AccountId,
                Name = activeAccount.Label,
                ProviderBadge = "",
                TierBadgeText = BuildAccountTierBadgeText(activeAccount),
                Subtitle = "",
                IsActive = true,
                IsOpenAi = OpenAiQuotaPolicy.IsOpenAiOAuthAccount(activeAccount),
                CanProbe = false,
                CanRefreshOfficialQuota = OpenAiQuotaPolicy.IsOpenAiOAuthAccount(activeAccount),
                StatusText = "",
                StatusBrush = "#107C10",
                DailyTokens = "0",
                WeeklyTokens = "0",
                MonthlyTokens = "0",
                FiveHourUsedPercent = 0,
                WeeklyUsedPercent = 0,
                FiveHourAvailableText = "",
                WeeklyAvailableText = "",
                FiveHourProgressBrush = "#107C10",
                WeeklyProgressBrush = "#107C10"
            }, forceLaunch: true);
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.launch_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u542F\u52A8 Codex \u5931\u8D25";
        }
    }

    public async Task ProbeCompatibleApisAsync(AccountListItem? item)
    {
        using var _ = EnterBusy();
        try
        {
            ErrorText = "";
            ActivityText = "\u6B63\u5728\u63A2\u6D4B API \u8FDE\u901A\u60C5\u51B5...";
            _config = await _appConfigStore.LoadAsync();

            var compatibleProviderIds = _config.Providers
                .Where(provider => provider.Kind == ProviderKind.OpenAiCompatible)
                .Select(provider => provider.ProviderId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var accounts = _config.Accounts
                .Where(account => compatibleProviderIds.Contains(account.ProviderId))
                .ToList();

            if (item is not null && compatibleProviderIds.Contains(item.ProviderId))
            {
                accounts = accounts
                    .Where(account => account.ProviderId == item.ProviderId && account.AccountId == item.AccountId)
                    .ToList();
            }

            if (accounts.Count == 0)
            {
                ErrorText = "\u6CA1\u6709\u53EF\u63A2\u6D4B\u7684\u517C\u5BB9 Provider \u8D26\u53F7\u3002";
                ActivityText = "\u63A2\u6D4B\u672A\u6267\u884C";
                return;
            }

            var results = await new CompatibleProviderProbeService(_secretStore)
                .ProbeAsync(_config, accounts);
            var successCount = results.Count(result => result.Success);
            _config = await ApplyCompatibleProbeResultsAsync(_config, results);
            ApplyViewState(_homeLocator.Resolve(), _lastUsageDashboard);
            RefreshLastRefreshText(DateTimeOffset.Now);
            ActivityText = $"\u63A2\u6D4B\u5B8C\u6210\uFF1A{successCount}/{results.Count} \u53EF\u7528";
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.probe_compatible_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "API \u63A2\u6D4B\u5931\u8D25";
        }
    }

    public async Task RefreshQuotaAndApisAsync()
    {
        using var _ = EnterBusy();
        try
        {
            ErrorText = "";
            ActivityText = "\u6B63\u5728\u5237\u65B0\u989D\u5EA6/API...";
            _config = await _appConfigStore.LoadAsync();

            var officialAccounts = _config.Accounts
                .Where(OpenAiQuotaPolicy.IsOpenAiOAuthAccount)
                .ToList();
            var refreshedAccounts = new List<AccountRecord>(officialAccounts.Count);
            if (officialAccounts.Count > 0)
            {
                var officialUsageService = new OpenAiOfficialUsageService(_secretStore);
                foreach (var account in officialAccounts)
                {
                    refreshedAccounts.Add(await officialUsageService.RefreshAccountAsync(account));
                }

                _config = await MergeOfficialUsageAccountsAsync(refreshedAccounts);
            }

            var compatibleProviderIds = _config.Providers
                .Where(provider => provider.Kind == ProviderKind.OpenAiCompatible)
                .Select(provider => provider.ProviderId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var compatibleAccounts = _config.Accounts
                .Where(account => compatibleProviderIds.Contains(account.ProviderId))
                .ToList();

            var probeResults = compatibleAccounts.Count == 0
                ? []
                : await new CompatibleProviderProbeService(_secretStore).ProbeAsync(_config, compatibleAccounts);
            _config = await ApplyCompatibleProbeResultsAsync(_config, probeResults);
            ApplyViewState(_homeLocator.Resolve(), _lastUsageDashboard);
            RefreshLastRefreshText(DateTimeOffset.Now);

            var summaryParts = new List<string>();
            if (officialAccounts.Count > 0)
            {
                var officialFailedCount = refreshedAccounts.Count(account => !string.IsNullOrWhiteSpace(account.OfficialUsageError));
                summaryParts.Add(officialFailedCount == 0
                    ? $"\u5B98\u65B9\u989D\u5EA6 {refreshedAccounts.Count}/{refreshedAccounts.Count} \u5DF2\u5237\u65B0"
                    : $"\u5B98\u65B9\u989D\u5EA6 {refreshedAccounts.Count - officialFailedCount}/{refreshedAccounts.Count} \u6210\u529F");
            }

            if (probeResults.Count > 0)
            {
                var probeSuccessCount = probeResults.Count(result => result.Success);
                summaryParts.Add($"API {probeSuccessCount}/{probeResults.Count} \u53EF\u7528");
            }

            if (summaryParts.Count == 0)
            {
                ActivityText = "\u6CA1\u6709\u53EF\u5237\u65B0\u7684\u5B98\u65B9\u8D26\u53F7\u6216\u53EF\u63A2\u6D4B\u7684 API \u8D26\u53F7\u3002";
            }
            else
            {
                ActivityText = "\u5237\u65B0\u5B8C\u6210\uFF1A" + string.Join(" \u00B7 ", summaryParts);
            }

            ErrorText = "";
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.refresh_quota_and_api_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u5237\u65B0\u989D\u5EA6/API \u5931\u8D25";
        }
    }

    public async Task RefreshOfficialQuotaAsync(AccountListItem? item)
    {
        using var _ = EnterBusy();
        try
        {
            ErrorText = "";
            ActivityText = "\u6B63\u5728\u5237\u65B0\u5B98\u65B9\u989D\u5EA6...";
            _config = await _appConfigStore.LoadAsync();

            var targetAccounts = _config.Accounts
                .Where(OpenAiQuotaPolicy.IsOpenAiOAuthAccount)
                .Where(account => item is null ||
                    (string.Equals(account.ProviderId, item.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(account.AccountId, item.AccountId, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (targetAccounts.Count == 0)
            {
                ErrorText = "\u6CA1\u6709\u53EF\u5237\u65B0\u7684 OpenAI \u5B98\u65B9\u8D26\u53F7\u3002";
                ActivityText = "\u5237\u65B0\u672A\u6267\u884C";
                return;
            }

            var service = new OpenAiOfficialUsageService(_secretStore);
            var refreshedAccounts = new List<AccountRecord>(targetAccounts.Count);
            foreach (var account in targetAccounts)
            {
                refreshedAccounts.Add(await service.RefreshAccountAsync(account));
            }

            _config = await MergeOfficialUsageAccountsAsync(refreshedAccounts);
            ApplyViewState(_homeLocator.Resolve(), _lastUsageDashboard);
            RefreshLastRefreshText(DateTimeOffset.Now);

            var failedCount = refreshedAccounts.Count(account => !string.IsNullOrWhiteSpace(account.OfficialUsageError));
            ActivityText = failedCount == 0
                ? (refreshedAccounts.Count == 1 ? "\u5B98\u65B9\u989D\u5EA6\u5DF2\u5237\u65B0\u3002" : "\u5B98\u65B9\u989D\u5EA6\u5DF2\u6279\u91CF\u5237\u65B0\u3002")
                : $"\u5B98\u65B9\u989D\u5EA6\u5237\u65B0\u5B8C\u6210\uFF0C{failedCount}/{refreshedAccounts.Count} \u5931\u8D25";
            ErrorText = "";
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.refresh_official_quota_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u5237\u65B0\u5B98\u65B9\u989D\u5EA6\u5931\u8D25";
        }
    }

    public async Task SetRoutingModeAsync(OpenAiAccountMode mode)
    {
        using var _ = EnterBusy();
        try
        {
            ErrorText = "";
            _config = await _appConfigStore.LoadAsync();
            if (_config.Settings.OpenAiAccountMode == mode)
            {
                RoutingModeText = BuildRoutingModeText(mode);
                RaiseRoutingModePropertiesChanged();
                ActivityText = mode == OpenAiAccountMode.AggregateGateway
                    ? "\u5DF2\u4FDD\u6301\u81EA\u52A8\u5207\u6362\u3002"
                    : "\u5DF2\u4FDD\u6301\u624B\u52A8\u5207\u6362\u3002";
                return;
            }

            ActivityText = mode == OpenAiAccountMode.AggregateGateway
                ? "\u6B63\u5728\u5207\u6362\u4E3A\u81EA\u52A8\u8DEF\u7531..."
                : "\u6B63\u5728\u5207\u6362\u4E3A\u624B\u52A8\u5207\u6362...";
            _config = _config with
            {
                Settings = _config.Settings with
                {
                    OpenAiAccountMode = mode
                }
            };
            await _appConfigStore.SaveAsync(_config);
            RoutingModeText = BuildRoutingModeText(mode);
            RaiseRoutingModePropertiesChanged();
            ActivityText = mode == OpenAiAccountMode.AggregateGateway
                ? "\u5DF2\u5207\u6362\u5230\u81EA\u52A8\u5207\u6362\u3002"
                : "\u5DF2\u5207\u6362\u5230\u624B\u52A8\u5207\u6362\u3002";
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.set_routing_mode_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u5207\u6362\u8DEF\u7531\u6A21\u5F0F\u5931\u8D25";
        }
    }

    public async Task AddCompatibleAsync(AddCompatibleResult result)
    {
        using var _ = EnterBusy();
        ActivityText = "\u6B63\u5728\u4FDD\u5B58\u517C\u5BB9 Provider...";
        var credentialRef = $"api-key:{result.ProviderId}:{result.AccountId}";
        await _secretStore.WriteSecretAsync(credentialRef, result.ApiKey);

        _config = await _appConfigStore.LoadAsync();
        var existingAccount = _config.Accounts.FirstOrDefault(a => a.ProviderId == result.ProviderId && a.AccountId == result.AccountId);
        _config = _config with
        {
            Providers = Upsert(_config.Providers, p => p.ProviderId == result.ProviderId, new ProviderDefinition
            {
                ProviderId = result.ProviderId,
                CodexProviderId = result.CodexProviderId,
                DisplayName = result.ProviderName,
                Kind = ProviderKind.OpenAiCompatible,
                AuthMode = AuthMode.ApiKey,
                BaseUrl = result.BaseUrl,
                WireApi = WireApi.Responses,
                SupportsMultiAccount = true
            }),
            Accounts = Upsert(_config.Accounts, a => a.ProviderId == result.ProviderId && a.AccountId == result.AccountId, new AccountRecord
            {
                ProviderId = result.ProviderId,
                AccountId = result.AccountId,
                Label = result.AccountLabel,
                CredentialRef = credentialRef,
                Status = AccountStatus.Active,
                CreatedAt = existingAccount?.CreatedAt ?? DateTimeOffset.UtcNow,
                ManualOrder = existingAccount?.ManualOrder ?? NextManualOrder(_config)
            })
        };

        await _appConfigStore.SaveAsync(_config);
        await RefreshAsync("\u6B63\u5728\u5237\u65B0\u8D26\u53F7\u5217\u8868...", "\u517C\u5BB9 Provider \u5DF2\u4FDD\u5B58\u3002");
    }

    public async Task AddOpenAiOAuthAsync(OAuthTokens tokens, string label)
    {
        using var _ = EnterBusy();
        ActivityText = "\u6B63\u5728\u4FDD\u5B58 OpenAI \u8D26\u53F7...";
        var identity = OAuthIdentityExtractor.Extract(tokens);
        var accountId = tokens.AccountId ?? identity.SubjectId ?? identity.Email ?? Guid.NewGuid().ToString("N");
        var displayLabel = string.IsNullOrWhiteSpace(label) || string.Equals(label, "OpenAI", StringComparison.OrdinalIgnoreCase)
            ? identity.BestDisplayName(label)
            : label;
        var credentialRef = $"oauth:openai:{accountId}";
        await _secretStore.WriteTokensAsync(credentialRef, tokens);

        _config = await _appConfigStore.LoadAsync();
        var existingAccount = _config.Accounts.FirstOrDefault(a => a.ProviderId == "openai" && a.AccountId == accountId);
        _config = _config with
        {
            Providers = Upsert(_config.Providers, p => p.ProviderId == "openai", new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth,
                WireApi = WireApi.Responses,
                SupportsMultiAccount = true
            }),
            Accounts = Upsert(_config.Accounts, a => a.ProviderId == "openai" && a.AccountId == accountId, new AccountRecord
            {
                ProviderId = "openai",
                AccountId = accountId,
                Label = displayLabel,
                Email = identity.Email,
                SubjectId = identity.SubjectId,
                CredentialRef = credentialRef,
                Status = AccountStatus.Active,
                CreatedAt = existingAccount?.CreatedAt ?? DateTimeOffset.UtcNow,
                ManualOrder = existingAccount?.ManualOrder ?? NextManualOrder(_config)
            })
        };

        await _appConfigStore.SaveAsync(_config);
        await RefreshAsync("\u6B63\u5728\u5237\u65B0\u8D26\u53F7\u5217\u8868...", "OpenAI \u8D26\u53F7\u5DF2\u4FDD\u5B58\u3002");
    }

    public async Task<AccountEditContext?> GetEditContextAsync(AccountListItem item)
    {
        _config = await _appConfigStore.LoadAsync();
        var provider = _config.Providers.FirstOrDefault(p => p.ProviderId == item.ProviderId);
        var account = _config.Accounts.FirstOrDefault(a => a.ProviderId == item.ProviderId && a.AccountId == item.AccountId);
        return provider is null || account is null ? null : new AccountEditContext(provider, account);
    }

    public async Task EditAsync(EditAccountResult result)
    {
        using var _ = EnterBusy();
        try
        {
            ErrorText = "";
            ActivityText = "\u6B63\u5728\u4FDD\u5B58\u4FEE\u6539...";
            _config = await _appConfigStore.LoadAsync();
            var provider = _config.Providers.FirstOrDefault(p =>
                string.Equals(p.ProviderId, result.OriginalProviderId, StringComparison.OrdinalIgnoreCase));
            var account = _config.Accounts.FirstOrDefault(a =>
                string.Equals(a.ProviderId, result.OriginalProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.AccountId, result.OriginalAccountId, StringComparison.OrdinalIgnoreCase));
            if (provider is null || account is null)
            {
                ErrorText = "\u6240\u9009\u8D26\u53F7\u5DF2\u4E0D\u5B58\u5728\u3002";
                return;
            }

            var nextProviderId = provider.Kind == ProviderKind.OpenAiCompatible
                ? result.ProviderId.Trim()
                : result.OriginalProviderId;
            if (provider.Kind == ProviderKind.OpenAiCompatible && string.IsNullOrWhiteSpace(nextProviderId))
            {
                ErrorText = "Provider ID \u4E0D\u80FD\u4E3A\u7A7A\u3002";
                return;
            }

            var providerIdChanged = provider.Kind == ProviderKind.OpenAiCompatible &&
                !string.Equals(result.OriginalProviderId, nextProviderId, StringComparison.OrdinalIgnoreCase);
            if (providerIdChanged && _config.Providers.Any(p =>
                    !string.Equals(p.ProviderId, result.OriginalProviderId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.ProviderId, nextProviderId, StringComparison.OrdinalIgnoreCase)))
            {
                var hint = string.Equals(nextProviderId, "openai", StringComparison.OrdinalIgnoreCase)
                    ? "\u5982\u679C\u53EA\u662F\u60F3\u8BA9 Codex \u6309 openai \u8FC7\u6EE4\u5386\u53F2\uFF0C\u8BF7\u4FDD\u6301 Provider ID \u4E3A\u672C\u5730\u552F\u4E00\u503C\uFF0C\u5E76\u628A Codex Provider ID \u8BBE\u4E3A openai\u3002"
                    : "";
                ErrorText = $"Provider ID \u201C{nextProviderId}\u201D \u5DF2\u5B58\u5728\u3002{hint}";
                return;
            }

            var affectedAccounts = provider.Kind == ProviderKind.OpenAiCompatible
                ? _config.Accounts
                    .Where(a => string.Equals(a.ProviderId, result.OriginalProviderId, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : [account];
            var preservedSecrets = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (provider.Kind == ProviderKind.OpenAiCompatible)
            {
                foreach (var affectedAccount in affectedAccounts)
                {
                    preservedSecrets[affectedAccount.AccountId] = await _secretStore.ReadSecretAsync(affectedAccount.CredentialRef);
                }
            }

            _config = _config with
            {
                ActiveSelection = RewriteEditedProviderSelection(_config.ActiveSelection, result.OriginalProviderId, nextProviderId),
                Providers = _config.Providers.Select(p =>
                {
                    if (!string.Equals(p.ProviderId, result.OriginalProviderId, StringComparison.OrdinalIgnoreCase) ||
                        p.Kind != ProviderKind.OpenAiCompatible)
                    {
                        return p;
                    }

                    return p with
                    {
                        ProviderId = nextProviderId,
                        CodexProviderId = result.CodexProviderId,
                        DisplayName = result.ProviderName,
                        BaseUrl = result.BaseUrl
                    };
                }).ToList(),
                Accounts = _config.Accounts.Select(a =>
                {
                    if (!string.Equals(a.ProviderId, result.OriginalProviderId, StringComparison.OrdinalIgnoreCase))
                    {
                        return a;
                    }

                    if (provider.Kind != ProviderKind.OpenAiCompatible)
                    {
                        return string.Equals(a.AccountId, result.OriginalAccountId, StringComparison.OrdinalIgnoreCase)
                            ? a with { Label = result.AccountLabel }
                            : a;
                    }

                    var updated = a with
                    {
                        ProviderId = nextProviderId,
                        CredentialRef = CompatibleCredentialRef(nextProviderId, a.AccountId)
                    };

                    return string.Equals(a.AccountId, result.OriginalAccountId, StringComparison.OrdinalIgnoreCase)
                        ? updated with { Label = result.AccountLabel }
                        : updated;
                }).ToList(),
                Profiles = _config.Profiles.Select(profile =>
                    string.Equals(profile.ProviderId, result.OriginalProviderId, StringComparison.OrdinalIgnoreCase) &&
                    provider.Kind == ProviderKind.OpenAiCompatible
                        ? profile with { ProviderId = nextProviderId }
                        : profile).ToList()
            };

            if (provider.Kind == ProviderKind.OpenAiCompatible)
            {
                foreach (var affectedAccount in affectedAccounts)
                {
                    var secret = string.Equals(affectedAccount.AccountId, result.OriginalAccountId, StringComparison.OrdinalIgnoreCase) &&
                                 !string.IsNullOrWhiteSpace(result.ApiKey)
                        ? result.ApiKey
                        : preservedSecrets.GetValueOrDefault(affectedAccount.AccountId);
                    var shouldWrite = providerIdChanged ||
                                      (string.Equals(affectedAccount.AccountId, result.OriginalAccountId, StringComparison.OrdinalIgnoreCase) &&
                                       !string.IsNullOrWhiteSpace(result.ApiKey));
                    if (shouldWrite && !string.IsNullOrWhiteSpace(secret))
                    {
                        await _secretStore.WriteSecretAsync(CompatibleCredentialRef(nextProviderId, affectedAccount.AccountId), secret);
                    }
                }
            }

            await _appConfigStore.SaveAsync(_config);
            if (providerIdChanged)
            {
                await new SwitchJournalStore(_appPaths.SwitchJournalPath)
                    .RenameProviderAsync(result.OriginalProviderId, nextProviderId);

                foreach (var affectedAccount in affectedAccounts)
                {
                    await _secretStore.DeleteSecretAsync(affectedAccount.CredentialRef);
                }
            }

            await RefreshAsync("\u6B63\u5728\u5237\u65B0\u8D26\u53F7\u5217\u8868...", "\u8D26\u53F7\u4FEE\u6539\u5DF2\u4FDD\u5B58\u3002");
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.edit_account_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u4FDD\u5B58\u4FEE\u6539\u5931\u8D25";
        }
    }

    private static CodexSelection? RewriteEditedProviderSelection(
        CodexSelection? selection,
        string originalProviderId,
        string nextProviderId)
    {
        if (selection is null ||
            string.Equals(originalProviderId, nextProviderId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selection.ProviderId, originalProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return selection;
        }

        return selection with { ProviderId = nextProviderId };
    }

    private static string CompatibleCredentialRef(string providerId, string accountId)
        => $"api-key:{providerId}:{accountId}";

    public async Task MoveAsync(AccountListItem item, int direction)
    {
        var index = Accounts.IndexOf(item);
        var targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= Accounts.Count)
        {
            return;
        }

        Accounts.Move(index, targetIndex);
        await PersistAccountOrderAsync(Accounts.ToList(), "\u8D26\u53F7\u987A\u5E8F\u5DF2\u66F4\u65B0\u3002");
    }

    public async Task ReorderAsync(AccountListItem item, int targetIndex)
    {
        var currentIndex = Accounts.IndexOf(item);
        if (currentIndex < 0)
        {
            return;
        }

        targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, Accounts.Count - 1));
        if (currentIndex == targetIndex)
        {
            return;
        }

        Accounts.Move(currentIndex, targetIndex);
        await PersistAccountOrderAsync(Accounts.ToList(), "\u8D26\u53F7\u987A\u5E8F\u5DF2\u66F4\u65B0\u3002");
    }

    public async Task PersistAccountOrderAsync(IReadOnlyList<AccountListItem> orderedItems, string completedActivity)
    {
        using var _ = EnterBusy();
        ErrorText = "";
        _config = await _appConfigStore.LoadAsync();
        _config = await NormalizeManualOrderAsync(_config);

        var nextOrder = 1;
        var manualOrderMap = orderedItems.ToDictionary(
            item => (item.ProviderId, item.AccountId),
            _ => nextOrder++,
            EqualityComparer<(string ProviderId, string AccountId)>.Default);

        _config = _config with
        {
            Accounts = _config.Accounts
                .Select(account =>
                {
                    var key = (account.ProviderId, account.AccountId);
                    return manualOrderMap.TryGetValue(key, out var manualOrder)
                        ? account with { ManualOrder = manualOrder }
                        : account;
                })
                .ToList()
        };

        await _appConfigStore.SaveAsync(_config);
        ActivityText = completedActivity;
    }

    private async Task<AppConfig> BackfillOAuthIdentitiesAsync(AppConfig config)
    {
        var changed = false;
        var accounts = new List<AccountRecord>(config.Accounts.Count);
        foreach (var account in config.Accounts)
        {
            if (account.ProviderId != "openai" || !account.CredentialRef.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase))
            {
                accounts.Add(account);
                continue;
            }

            var tokens = await _secretStore.ReadTokensAsync(account.CredentialRef);
            if (tokens is null)
            {
                accounts.Add(account);
                continue;
            }

            var identity = OAuthIdentityExtractor.Extract(tokens);
            var label = account.Label;
            if (string.IsNullOrWhiteSpace(label) || string.Equals(label, "OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                label = identity.BestDisplayName(account.Label);
            }

            var updated = account with
            {
                Label = label,
                Email = account.Email ?? identity.Email,
                SubjectId = account.SubjectId ?? identity.SubjectId
            };
            changed |= updated != account;
            accounts.Add(updated);
        }

        if (!changed)
        {
            return config;
        }

        var updatedConfig = config with { Accounts = accounts };
        await _appConfigStore.SaveAsync(updatedConfig);
        return updatedConfig;
    }

    private async Task<AppConfig> NormalizeManualOrderAsync(AppConfig config)
    {
        var changed = false;
        var next = 1;
        var used = new HashSet<int>();
        var accounts = new List<AccountRecord>(config.Accounts.Count);

        foreach (var account in config.Accounts)
        {
            var order = account.ManualOrder;
            if (order <= 0 || used.Contains(order))
            {
                while (used.Contains(next))
                {
                    next++;
                }

                order = next;
                changed = true;
            }

            used.Add(order);
            next = Math.Max(next, order + 1);
            accounts.Add(order == account.ManualOrder ? account : account with { ManualOrder = order });
        }

        if (!changed)
        {
            return config;
        }

        var updated = config with { Accounts = accounts };
        await _appConfigStore.SaveAsync(updated);
        return updated;
    }

    private async Task<AppConfig> LoadHydratedConfigAsync(TimeSpan officialUsageMinRefreshInterval, bool refreshOfficialUsage)
    {
        var config = await _appConfigStore.LoadAsync();
        config = await BackfillOAuthIdentitiesAsync(config);
        config = await NormalizeManualOrderAsync(config);

        if (!refreshOfficialUsage)
        {
            return config;
        }

        var officialUsageRefresh = await new OpenAiOfficialUsageService(_secretStore)
            .RefreshAsync(config, officialUsageMinRefreshInterval);
        if (officialUsageRefresh.Changed)
        {
            return await MergeOfficialUsageAccountsAsync(officialUsageRefresh.Config.Accounts);
        }

        return await _appConfigStore.LoadAsync();
    }

    private async Task<AppConfig> MergeOfficialUsageAccountsAsync(IEnumerable<AccountRecord> refreshedAccounts)
    {
        var refreshedMap = refreshedAccounts.ToDictionary(
            account => (account.ProviderId, account.AccountId),
            account => account,
            EqualityComparer<(string ProviderId, string AccountId)>.Default);

        var latest = await _appConfigStore.LoadAsync();
        if (refreshedMap.Count == 0)
        {
            return latest;
        }

        var merged = latest with
        {
            Accounts = latest.Accounts
                .Select(account =>
                {
                    var key = (account.ProviderId, account.AccountId);
                    return refreshedMap.TryGetValue(key, out var refreshed)
                        ? MergeOfficialUsageFields(account, refreshed)
                        : account;
                })
                .ToList()
        };

        if (merged != latest)
        {
            await _appConfigStore.SaveAsync(merged);
        }

        return merged;
    }

    private async Task<AppConfig> ApplyCompatibleProbeResultsAsync(
        AppConfig config,
        IReadOnlyList<CompatibleProviderProbeResult> results)
    {
        if (results.Count == 0)
        {
            return config;
        }

        var resultMap = results.ToDictionary(
            result => (result.ProviderId, result.AccountId),
            result => result,
            EqualityComparer<(string ProviderId, string AccountId)>.Default);

        var updated = config with
        {
            Accounts = config.Accounts
                .Select(account =>
                {
                    var key = (account.ProviderId, account.AccountId);
                    if (!resultMap.TryGetValue(key, out var result))
                    {
                        return account;
                    }

                    return account with
                    {
                        Status = result.Success ? AccountStatus.Active : AccountStatus.NeedsReauth
                    };
                })
                .ToList()
        };

        if (updated != config)
        {
            await _appConfigStore.SaveAsync(updated);
        }

        return updated;
    }

    private static AccountRecord MergeOfficialUsageFields(AccountRecord current, AccountRecord refreshed)
        => current with
        {
            Tier = refreshed.Tier,
            OfficialPlanTypeRaw = refreshed.OfficialPlanTypeRaw,
            FiveHourQuota = refreshed.FiveHourQuota,
            WeeklyQuota = refreshed.WeeklyQuota,
            OfficialUsageFetchedAt = refreshed.OfficialUsageFetchedAt,
            OfficialUsageError = refreshed.OfficialUsageError,
            Status = refreshed.Status
        };

    private void RefreshLastRefreshText(DateTimeOffset now)
    {
        var refreshedAt = ResolveActiveRefreshTimestamp(_config) ?? now;
        LastRefreshText = BuildRelativeRefreshText(refreshedAt, now);
    }

    private static DateTimeOffset? ResolveActiveRefreshTimestamp(AppConfig config)
    {
        if (config.ActiveSelection is null)
        {
            return null;
        }

        var activeAccount = config.Accounts.FirstOrDefault(account =>
            string.Equals(account.ProviderId, config.ActiveSelection.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(account.AccountId, config.ActiveSelection.AccountId, StringComparison.OrdinalIgnoreCase));
        if (activeAccount is null)
        {
            return null;
        }

        return OpenAiQuotaPolicy.IsOpenAiOAuthAccount(activeAccount)
            ? activeAccount.OfficialUsageFetchedAt
            : null;
    }

    private static bool ShouldRefreshOfficialUsage(AppConfig config, TimeSpan minRefreshInterval)
    {
        var now = DateTimeOffset.UtcNow;
        return config.Accounts
            .Where(OpenAiQuotaPolicy.IsOpenAiOAuthAccount)
            .Any(account => !account.OfficialUsageFetchedAt.HasValue || now - account.OfficialUsageFetchedAt.Value >= minRefreshInterval);
    }

    private async Task ActivateSelectionAsync(AccountListItem item, bool forceLaunch)
    {
        using var _ = EnterBusy();
        try
        {
            ErrorText = "";
            ActivityText = forceLaunch
                ? "\u6B63\u5728\u5207\u6362\u8D26\u53F7\u5E76\u542F\u52A8 Codex..."
                : "\u6B63\u5728\u5207\u6362\u8D26\u53F7...";
            _config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1), refreshOfficialUsage: false);
            var requestedSelection = new CodexSelection { ProviderId = item.ProviderId, AccountId = item.AccountId };
            var aggregateDecision = await new OpenAiAggregateGatewayService(_appPaths, _secretStore)
                .ResolveSelectionAsync(_config, requestedSelection);
            var selection = aggregateDecision.ResolvedSelection;
            var service = new CodexActivationService(
                _homeLocator,
                new CodexConfigStore(),
                new CodexAuthStore(),
                new CodexStateTransaction(_appPaths),
                new CodexIntegrityChecker(),
                _secretStore,
                _secretStore);

            var result = await service.ActivateAsync(_config, selection);
            var journalMessage = aggregateDecision.WasRerouted
                ? $"{aggregateDecision.Message} {result.Message}"
                : result.Message;
            await new SwitchJournalStore(_appPaths.SwitchJournalPath)
                .AppendAsync(result.Selection, result.ValidationPassed ? "ok" : "failed", journalMessage);

            if (!result.ValidationPassed)
            {
                ErrorText = result.Message;
                return;
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
            await _appConfigStore.SaveAsync(_config);
            await RefreshAsync("\u6B63\u5728\u5237\u65B0\u9762\u677F...", "\u8D26\u53F7\u5DF2\u5207\u6362\u3002");

            if (forceLaunch)
            {
                await LaunchCurrentCodexAsync("\u5DF2\u6309\u6240\u9009\u8D26\u53F7\u542F\u52A8 Codex\u3002");
            }
            else
            {
                var launchEnvironment = await CodexLaunchEnvironmentBuilder.BuildAsync(_config, _secretStore);
                var launchResult = await new CodexLaunchService().LaunchIfConfiguredAsync(_config.Settings, launchEnvironment);
                if (launchResult.Attempted && !launchResult.Launched)
                {
                    ErrorText = $"\u5DF2\u6FC0\u6D3B\uFF0C\u4F46\u542F\u52A8 Codex \u5931\u8D25\uFF1A{launchResult.Message}";
                    ActivityText = "\u8D26\u53F7\u5DF2\u5207\u6362\uFF0C\u4F46\u542F\u52A8 Codex \u5931\u8D25";
                }
            }

            if (aggregateDecision.WasRerouted)
            {
                StatusText = $"{StatusText}\n{aggregateDecision.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.activate_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u5207\u6362\u8D26\u53F7\u5931\u8D25";
        }
    }

    private async Task LaunchCurrentCodexAsync(string successPrefix)
    {
        using var _ = EnterBusy();
        ActivityText = "\u6B63\u5728\u542F\u52A8 Codex...";
        var launchEnvironment = await CodexLaunchEnvironmentBuilder.BuildAsync(_config, _secretStore);
        var launchResult = await new CodexLaunchService().LaunchAsync(_config.Settings, launchEnvironment);
        if (!launchResult.Launched)
        {
            ErrorText = $"\u542F\u52A8 Codex \u5931\u8D25\uFF1A{launchResult.Message}";
            ActivityText = "\u542F\u52A8 Codex \u5931\u8D25";
            return;
        }

        StatusText = $"{StatusText}\n{successPrefix}";
        ActivityText = successPrefix;
    }

    public async Task DeleteAsync(AccountListItem item)
    {
        using var _ = EnterBusy();
        ActivityText = "\u6B63\u5728\u5220\u9664\u8D26\u53F7...";
        _config = await _appConfigStore.LoadAsync();
        var account = _config.Accounts.FirstOrDefault(a => a.ProviderId == item.ProviderId && a.AccountId == item.AccountId);
        if (account is null)
        {
            return;
        }

        await _secretStore.DeleteSecretAsync(account.CredentialRef);
        await _secretStore.DeleteTokensAsync(account.CredentialRef);

        var active = _config.ActiveSelection?.ProviderId == item.ProviderId && _config.ActiveSelection?.AccountId == item.AccountId;
        _config = _config with
        {
            ActiveSelection = active ? null : _config.ActiveSelection,
            Accounts = _config.Accounts
                .Where(a => !(a.ProviderId == item.ProviderId && a.AccountId == item.AccountId))
                .ToList()
        };

        await _appConfigStore.SaveAsync(_config);
        await RefreshAsync("\u6B63\u5728\u5237\u65B0\u8D26\u53F7\u5217\u8868...", "\u8D26\u53F7\u5DF2\u5220\u9664\u3002");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private IDisposable EnterBusy()
    {
        _busyDepth++;
        if (_busyDepth == 1)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanInteract)));
        }

        return new BusyScope(this);
    }

    private void ExitBusy()
    {
        if (_busyDepth == 0)
        {
            return;
        }

        _busyDepth--;
        if (_busyDepth == 0)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanInteract)));
        }
    }

    private void ApplyViewState(CodexHomeState home, UsageDashboard usageDashboard)
    {
        _lastUsageDashboard = usageDashboard;
        var active = _config.ActiveSelection is null
            ? "\u5F53\u524D\u672A\u6FC0\u6D3B\u8D26\u53F7"
            : $"\u5F53\u524D\u6FC0\u6D3B\uFF1A{_config.ActiveSelection.ProviderId}/{_config.ActiveSelection.AccountId}";
        StatusText = $"{active}\n{home.RootPath}";
        QuotaStatusText = BuildQuotaStatusText(_config);
        RoutingModeText = BuildRoutingModeText(_config.Settings.OpenAiAccountMode);
        FootnoteText = "\u5207\u6362\u4EC5\u5F71\u54CD\u65B0\u4F1A\u8BDD\u00B7\u73B0\u6709\u4F1A\u8BDD\u4FDD\u6301\u4E0D\u53D8";
        RaiseRoutingModePropertiesChanged();

        Accounts.Clear();
        foreach (var account in OrderedAccounts(_config, usageDashboard))
        {
            var provider = _config.Providers.FirstOrDefault(p => p.ProviderId == account.ProviderId);
            var usage = usageDashboard.Accounts.FirstOrDefault(summary => summary.ProviderId == account.ProviderId && summary.AccountId == account.AccountId);
            var isActive =
                _config.ActiveSelection?.ProviderId == account.ProviderId &&
                _config.ActiveSelection?.AccountId == account.AccountId;
            var useCompactTokenUnit = provider?.Kind == ProviderKind.OpenAiCompatible;
            var fiveHourUsedPercent = ClampUsagePercent(account.FiveHourQuota);
            var weeklyUsedPercent = ClampUsagePercent(account.WeeklyQuota);
            Accounts.Add(new AccountListItem
            {
                ProviderId = account.ProviderId,
                AccountId = account.AccountId,
                Name = account.Label,
                ProviderBadge = BuildAccountProviderBadge(provider, account),
                TierBadgeText = BuildAccountTierBadgeText(account),
                Subtitle = BuildAccountSubtitle(provider, account),
                IsActive = isActive,
                IsOpenAi = OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account),
                CanProbe = provider?.Kind == ProviderKind.OpenAiCompatible,
                CanRefreshOfficialQuota = OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account),
                StatusText = BuildAccountStatusText(provider, account),
                StatusBrush = BuildAccountStatusBrush(provider, account),
                DailyTokens = FormatTokenCount(usage?.Today.TotalTokens ?? 0, useCompactTokenUnit),
                WeeklyTokens = FormatTokenCount(usage?.Last7Days.TotalTokens ?? 0, useCompactTokenUnit),
                MonthlyTokens = FormatTokenCount(usage?.Last30Days.TotalTokens ?? 0, useCompactTokenUnit),
                FiveHourUsedPercent = fiveHourUsedPercent,
                WeeklyUsedPercent = weeklyUsedPercent,
                FiveHourAvailableText = BuildAvailableQuotaText(account.FiveHourQuota),
                WeeklyAvailableText = BuildAvailableQuotaText(account.WeeklyQuota),
                FiveHourProgressBrush = BuildUsageBrush(fiveHourUsedPercent),
                WeeklyProgressBrush = BuildUsageBrush(weeklyUsedPercent)
            });
        }

        UsageText =
            $"\u4ECA\u65E5\uFF1A{usageDashboard.Today.TotalTokens:n0} tokens\n" +
            $"\u8FD1 7 \u5929\uFF1A{usageDashboard.Last7Days.TotalTokens:n0} tokens\n" +
            $"\u8FD1 30 \u5929\uFF1A{usageDashboard.Last30Days.TotalTokens:n0} tokens\n" +
            $"\u7D2F\u8BA1\uFF1A{usageDashboard.Lifetime.TotalTokens:n0} tokens";
        if (usageDashboard.UnattributedSessions > 0)
        {
            UsageText += $"\n\u672A\u5F52\u56E0\u4F1A\u8BDD\uFF1A{usageDashboard.UnattributedSessions:n0}";
        }

        ActiveAccount = BuildActiveAccountSnapshot(_config, usageDashboard);
    }

    private static List<T> Upsert<T>(IEnumerable<T> source, Func<T, bool> predicate, T item)
    {
        var list = source.Where(entry => !predicate(entry)).ToList();
        list.Add(item);
        return list;
    }

    private static IEnumerable<AccountRecord> OrderedAccounts(AppConfig config, UsageDashboard usageDashboard)
    {
        var usageByAccount = usageDashboard.Accounts.ToDictionary(
            account => (account.ProviderId, account.AccountId),
            account => account,
            EqualityComparer<(string ProviderId, string AccountId)>.Default);

        return config.Settings.AccountSortMode == AccountSortMode.Usage
            ? config.Accounts
                .OrderBy(account => OpenAiQuotaPolicy.DisplaySortBucket(account))
                .ThenBy(account => OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account) ? OpenAiQuotaPolicy.UsedPercentOrMax(account.FiveHourQuota) : int.MaxValue)
                .ThenBy(account => OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account) ? OpenAiQuotaPolicy.UsedPercentOrMax(account.WeeklyQuota) : int.MaxValue)
                .ThenByDescending(account => usageByAccount.TryGetValue((account.ProviderId, account.AccountId), out var usage) ? usage.Last30Days.TotalTokens : 0)
                .ThenByDescending(account => usageByAccount.TryGetValue((account.ProviderId, account.AccountId), out var usage) ? usage.Today.TotalTokens : 0)
                .ThenBy(account => OpenAiQuotaPolicy.RoutingStatusRank(account))
                .ThenByDescending(account => account.LastUsedAt ?? DateTimeOffset.MinValue)
                .ThenBy(account => account.ManualOrder <= 0 ? int.MaxValue : account.ManualOrder)
                .ThenBy(account => account.ProviderId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(account => account.Label, StringComparer.OrdinalIgnoreCase)
            : config.Accounts
                .OrderBy(account => account.ManualOrder <= 0 ? int.MaxValue : account.ManualOrder)
                .ThenBy(account => account.ProviderId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(account => account.Label, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<AccountRecord> OrderedAccountsByManualOrder(AppConfig config)
        => config.Accounts
            .OrderBy(account => account.ManualOrder <= 0 ? int.MaxValue : account.ManualOrder)
            .ThenBy(account => account.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.Label, StringComparer.OrdinalIgnoreCase);

    private static int NextManualOrder(AppConfig config)
        => config.Accounts.Count == 0 ? 1 : config.Accounts.Max(account => account.ManualOrder) + 1;

    private void RaiseRoutingModePropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutomaticRouting)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsManualRouting)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoutingModeBadgeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoutingDescriptionText)));
    }

    private static string BuildRoutingModeBadgeText(OpenAiAccountMode mode)
        => mode == OpenAiAccountMode.AggregateGateway ? "\u81EA\u52A8" : "\u624B\u52A8";

    private static string BuildRoutingDescriptionText(OpenAiAccountMode mode)
        => mode == OpenAiAccountMode.AggregateGateway
            ? "\u81EA\u52A8\u6839\u636E\u72B6\u6001\u3001\u989D\u5EA6\u4FE1\u606F\u4E0E\u672C\u5730\u4F7F\u7528\u91CF\u9009\u62E9\u66F4\u5408\u9002\u7684 Provider / \u8D26\u53F7"
            : "\u59CB\u7EC8\u4F7F\u7528\u5F53\u524D\u624B\u52A8\u9009\u4E2D\u7684 Provider / \u8D26\u53F7";

    private static string BuildAccountProviderBadge(ProviderDefinition? provider, AccountRecord account)
    {
        if (OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account))
        {
            return "OpenAI";
        }

        return string.IsNullOrWhiteSpace(provider?.DisplayName) ? "\u517C\u5BB9" : provider.DisplayName;
    }

    private static string BuildAccountSubtitle(ProviderDefinition? provider, AccountRecord account)
    {
        var subtitle = OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account)
            ? (string.IsNullOrWhiteSpace(account.Email) ? "OpenAI OAuth \u8D26\u53F7" : account.Email!)
            : provider is null ? account.AccountId : BuildCompatibleSubtitle(provider, account);

        return IsDuplicateAccountText(account.Label, subtitle) ? "" : subtitle;
    }

    private static bool IsDuplicateAccountText(string primary, string secondary)
    {
        if (string.IsNullOrWhiteSpace(primary) || string.IsNullOrWhiteSpace(secondary))
        {
            return false;
        }

        return string.Equals(primary.Trim(), secondary.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAccountTierBadgeText(AccountRecord account)
        => OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account)
            ? FormatTier(account) ?? ""
            : "";

    private static string BuildAccountStatusText(ProviderDefinition? provider, AccountRecord account)
    {
        if (provider?.Kind == ProviderKind.OpenAiOAuth || OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account))
        {
            if (!string.IsNullOrWhiteSpace(account.OfficialUsageError))
            {
                return "\u5B98\u65B9\u989D\u5EA6\u5237\u65B0\u5931\u8D25";
            }

            return account.OfficialUsageFetchedAt.HasValue
                ? "\u5B98\u65B9\u989D\u5EA6\u5237\u65B0\u6210\u529F"
                : "\u5B98\u65B9\u989D\u5EA6\u5C1A\u672A\u5237\u65B0";
        }

        return account.Status switch
        {
            AccountStatus.Active => "Provider \u53EF\u7528",
            AccountStatus.NeedsReauth => "Provider \u4E0D\u53EF\u7528",
            AccountStatus.Revoked => "Provider \u4E0D\u53EF\u7528",
            _ => "Provider \u5F85\u68C0\u67E5"
        };
    }

    private static string BuildAccountStatusBrush(ProviderDefinition? provider, AccountRecord account)
    {
        if (provider?.Kind == ProviderKind.OpenAiOAuth || OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account))
        {
            if (!string.IsNullOrWhiteSpace(account.OfficialUsageError))
            {
                return "#C42B1C";
            }

            return account.OfficialUsageFetchedAt.HasValue ? "#107C10" : "#9E9E9E";
        }

        return account.Status switch
        {
            AccountStatus.Active => "#107C10",
            AccountStatus.NeedsReauth => "#C42B1C",
            AccountStatus.Revoked => "#C42B1C",
            _ => "#9E9E9E"
        };
    }

    private static int ClampUsagePercent(QuotaUsageSnapshot snapshot)
    {
        if (!snapshot.HasValue)
        {
            return 0;
        }

        var usedPercent = OpenAiQuotaPolicy.UsedPercentOrMax(snapshot);
        return usedPercent == int.MaxValue ? 0 : Math.Clamp(usedPercent, 0, 100);
    }

    private static string BuildUsageBrush(int usedPercent)
        => usedPercent < 50
            ? "#107C10"
            : usedPercent < 80
                ? "#F9A825"
                : "#C42B1C";

    private static string BuildAvailableQuotaText(QuotaUsageSnapshot snapshot)
    {
        if (!snapshot.HasValue)
        {
            return "\u5F85\u83B7\u53D6";
        }

        return $"{OpenAiQuotaDisplayFormatter.FormatRemainingValue(snapshot)} \u53EF\u7528";
    }

    private static string BuildOfficialUsageSuffix(AccountRecord account)
    {
        if (!string.Equals(account.ProviderId, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var parts = new List<string>();
        var tier = FormatTier(account);
        if (!string.IsNullOrWhiteSpace(tier))
        {
            parts.Add(tier);
        }

        var fiveHour = OpenAiQuotaDisplayFormatter.FormatCompactRemaining(account.FiveHourQuota, "5h");
        if (!string.IsNullOrWhiteSpace(fiveHour))
        {
            parts.Add(fiveHour);
        }

        var weekly = OpenAiQuotaDisplayFormatter.FormatCompactRemaining(account.WeeklyQuota, "\u5468");
        if (!string.IsNullOrWhiteSpace(weekly))
        {
            parts.Add(weekly);
        }

        if (!string.IsNullOrWhiteSpace(account.OfficialUsageError))
        {
            parts.Add(BuildQuotaErrorTag(account));
        }

        if (parts.Count == 0)
        {
            return "";
        }

        return $" [{string.Join(" | ", parts)}]";
    }

    private static string? FormatTier(AccountRecord account)
    {
        if (!string.IsNullOrWhiteSpace(account.OfficialPlanTypeRaw))
        {
            var normalized = account.OfficialPlanTypeRaw.Trim().ToLowerInvariant();
            if (normalized.Contains("pro", StringComparison.Ordinal) && normalized.Contains("10", StringComparison.Ordinal))
            {
                return "pro 10x";
            }

            if (normalized.Contains("pro", StringComparison.Ordinal) && normalized.Contains("5", StringComparison.Ordinal))
            {
                return "pro 5x";
            }

            if (normalized.Contains("plus", StringComparison.Ordinal))
            {
                return "plus";
            }

            if (normalized.Contains("go", StringComparison.Ordinal))
            {
                return "go";
            }

            if (normalized.Contains("free", StringComparison.Ordinal))
            {
                return "free";
            }

            if (normalized.Contains("pro", StringComparison.Ordinal))
            {
                return "pro";
            }
        }

        if (account.Tier != AccountTier.Unknown)
        {
            return account.Tier.ToString().ToLowerInvariant();
        }

        return null;
    }

    private static string BuildQuotaErrorTag(AccountRecord account)
    {
        if (OpenAiQuotaPolicy.NeedsReauth(account))
        {
            return "\u9700\u8981\u91CD\u65B0\u767B\u5F55";
        }

        return OpenAiQuotaPolicy.HasAnyOfficialQuota(account)
            ? "\u989D\u5EA6\u5FEB\u7167\u5DF2\u8FC7\u671F"
            : "\u989D\u5EA6\u83B7\u53D6\u5931\u8D25";
    }

    private static string BuildQuotaStatusText(AppConfig config)
        => "";

    private static string BuildRelativeRefreshText(DateTimeOffset refreshedAt, DateTimeOffset now)
    {
        var delta = now - refreshedAt;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta < TimeSpan.FromSeconds(10))
        {
            return "\u4E0A\u6B21\u5237\u65B0\uFF1A\u521A\u521A";
        }

        if (delta < TimeSpan.FromMinutes(1))
        {
            return $"\u4E0A\u6B21\u5237\u65B0\uFF1A{Math.Max(1, (int)delta.TotalSeconds)} \u79D2\u524D";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"\u4E0A\u6B21\u5237\u65B0\uFF1A{Math.Max(1, (int)delta.TotalMinutes)} \u5206\u949F\u524D";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"\u4E0A\u6B21\u5237\u65B0\uFF1A{Math.Max(1, (int)delta.TotalHours)} \u5C0F\u65F6\u524D";
        }

        return $"\u4E0A\u6B21\u5237\u65B0\uFF1A{Math.Max(1, (int)delta.TotalDays)} \u5929\u524D";
    }

    private static string BuildRoutingModeText(OpenAiAccountMode mode)
        => mode == OpenAiAccountMode.AggregateGateway
            ? "OpenAI \u8DEF\u7531\uFF1A\u805A\u5408\u7F51\u5173"
            : "OpenAI \u8DEF\u7531\uFF1A\u624B\u52A8\u5207\u6362";

    private static ActiveAccountSnapshot BuildActiveAccountSnapshot(AppConfig config, UsageDashboard usageDashboard)
    {
        if (config.ActiveSelection is null)
        {
            return ActiveAccountSnapshot.Empty with
            {
                Title = "\u5F53\u524D\u672A\u6FC0\u6D3B\u8D26\u53F7",
                Subtitle = "\u8BF7\u5148\u5728\u4E3B\u6D6E\u7A97\u9009\u62E9\u4E00\u4E2A\u8D26\u53F7\u5E76\u70B9\u51FB\u201C\u4F7F\u7528\u201D\u3002",
                PrimaryMetric = "\u4ECA\u65E5 0 tokens",
                SecondaryMetric = "\u8FD1 7 \u5929 0 tokens"
            };
        }

        var account = config.Accounts.FirstOrDefault(item =>
            string.Equals(item.ProviderId, config.ActiveSelection.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.AccountId, config.ActiveSelection.AccountId, StringComparison.OrdinalIgnoreCase));
        var provider = config.Providers.FirstOrDefault(item =>
            string.Equals(item.ProviderId, config.ActiveSelection.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (account is null || provider is null)
        {
            return ActiveAccountSnapshot.Empty with
            {
                Title = "\u5F53\u524D\u6FC0\u6D3B\u8D26\u53F7\u5DF2\u4E0D\u5B58\u5728",
                Subtitle = "\u8BF7\u5237\u65B0\u4E3B\u6D6E\u7A97\u540E\u91CD\u65B0\u9009\u62E9\u3002",
                PrimaryMetric = "\u8BF7\u5237\u65B0"
            };
        }

        var usage = usageDashboard.Accounts.FirstOrDefault(item =>
            string.Equals(item.ProviderId, account.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.AccountId, account.AccountId, StringComparison.OrdinalIgnoreCase));
        var useCompactTokenUnit = provider.Kind == ProviderKind.OpenAiCompatible;
        var daily = FormatTokenCount(usage?.Today.TotalTokens ?? 0, useCompactTokenUnit);
        var weekly = FormatTokenCount(usage?.Last7Days.TotalTokens ?? 0, useCompactTokenUnit);
        var monthly = FormatTokenCount(usage?.Last30Days.TotalTokens ?? 0, useCompactTokenUnit);
        var subtitle = BuildAccountSubtitle(provider, account);

        if (provider.Kind == ProviderKind.OpenAiOAuth)
        {
            var fiveHourUsedPercent = ClampUsagePercent(account.FiveHourQuota);
            var weeklyUsedPercent = ClampUsagePercent(account.WeeklyQuota);
            return new ActiveAccountSnapshot
            {
                Title = account.Label,
                AccountTypeLabel = "OpenAI",
                ProviderBadge = "OpenAI",
                TierBadgeText = BuildAccountTierBadgeText(account),
                StatusText = BuildAccountStatusText(provider, account),
                StatusBrush = BuildAccountStatusBrush(provider, account),
                Subtitle = subtitle,
                PrimaryMetric = OpenAiQuotaDisplayFormatter.FormatCompactRemaining(account.FiveHourQuota, "5h") ?? "5h \u989D\u5EA6\u5C1A\u672A\u83B7\u53D6",
                SecondaryMetric = OpenAiQuotaDisplayFormatter.FormatCompactRemaining(account.WeeklyQuota, "\u5468")
                    ?? (!string.IsNullOrWhiteSpace(account.OfficialUsageError)
                        ? BuildQuotaErrorTag(account)
                        : "\u5B98\u65B9\u989D\u5EA6\u5FEB\u7167\u5C1A\u672A\u83B7\u53D6"),
                DailyTokens = daily,
                WeeklyTokens = weekly,
                MonthlyTokens = monthly,
                ShowQuotaBars = true,
                FiveHourUsedPercent = fiveHourUsedPercent,
                WeeklyUsedPercent = weeklyUsedPercent,
                FiveHourAvailableText = BuildAvailableQuotaText(account.FiveHourQuota),
                WeeklyAvailableText = BuildAvailableQuotaText(account.WeeklyQuota),
                FiveHourProgressBrush = BuildUsageBrush(fiveHourUsedPercent),
                WeeklyProgressBrush = BuildUsageBrush(weeklyUsedPercent),
                IsOpenAi = true,
                HasSelection = true
            };
        }

        return new ActiveAccountSnapshot
        {
            Title = account.Label,
            AccountTypeLabel = "\u517C\u5BB9 Provider",
            ProviderBadge = "\u517C\u5BB9",
            TierBadgeText = "",
            StatusText = BuildAccountStatusText(provider, account),
            StatusBrush = BuildAccountStatusBrush(provider, account),
            Subtitle = subtitle,
            PrimaryMetric = $"\u4ECA\u65E5 {daily} tokens",
            SecondaryMetric = $"\u8FD1 7 \u5929 {weekly} tokens",
            DailyTokens = daily,
            WeeklyTokens = weekly,
            MonthlyTokens = monthly,
            ShowTokenGrid = true,
            IsOpenAi = false,
            HasSelection = true
        };
    }

    private static string BuildCompatibleSubtitle(ProviderDefinition provider, AccountRecord account)
    {
        if (!string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            return $"{provider.DisplayName} · {provider.BaseUrl}";
        }

        return $"{provider.DisplayName} · {account.AccountId}";
    }

    private static string FormatTokenCount(long tokens, bool useCompactUnit = false)
    {
        if (!useCompactUnit || Math.Abs(tokens) < 10_000)
        {
            return tokens.ToString("n0");
        }

        var absolute = Math.Abs((double)tokens);
        var units = new (double Divisor, string Suffix)[]
        {
            (1_000_000_000d, "B"),
            (1_000_000d, "M"),
            (1_000d, "K")
        };

        foreach (var unit in units)
        {
            if (absolute < unit.Divisor)
            {
                continue;
            }

            var value = tokens / unit.Divisor;
            var decimals = Math.Abs(value) >= 100 ? 0 : Math.Abs(value) >= 10 ? 1 : 2;
            var formatted = value.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture)
                .TrimEnd('0')
                .TrimEnd('.');
            return $"{formatted}{unit.Suffix}";
        }

        return tokens.ToString("n0");
    }

    private static string FormatProbeResult(CompatibleProviderProbeResult result)
    {
        var marker = result.Success ? "\u2713" : "\u2717";
        var elapsedMs = Math.Max(1, (int)Math.Round(result.Elapsed.TotalMilliseconds));
        var status = result.StatusCode.HasValue ? $"HTTP {result.StatusCode.Value}" : "\u65E0 HTTP \u72B6\u6001";
        var suggestion = string.IsNullOrWhiteSpace(result.SuggestedBaseUrl)
            ? ""
            : $" \u5EFA\u8BAE\uFF1A{result.SuggestedBaseUrl}";
        return $"{marker} {result.ProviderId}/{result.AccountId} {status} {elapsedMs}ms\uFF1A{result.Message}{suggestion}";
    }

    private sealed class BusyScope : IDisposable
    {
        private MainFlyoutViewModel? _owner;

        public BusyScope(MainFlyoutViewModel owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            _owner?.ExitBusy();
            _owner = null;
        }
    }
}

public sealed record AccountListItem
{
    public required string ProviderId { get; init; }
    public required string AccountId { get; init; }
    public required string Name { get; init; }
    public required string ProviderBadge { get; init; }
    public required string TierBadgeText { get; init; }
    public required string Subtitle { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsOpenAi { get; init; }
    public required bool CanProbe { get; init; }
    public required bool CanRefreshOfficialQuota { get; init; }
    public required string StatusText { get; init; }
    public required string StatusBrush { get; init; }
    public required string DailyTokens { get; init; }
    public required string WeeklyTokens { get; init; }
    public required string MonthlyTokens { get; init; }
    public required int FiveHourUsedPercent { get; init; }
    public required int WeeklyUsedPercent { get; init; }
    public required string FiveHourAvailableText { get; init; }
    public required string WeeklyAvailableText { get; init; }
    public required string FiveHourProgressBrush { get; init; }
    public required string WeeklyProgressBrush { get; init; }

    public bool HasTierBadge => !string.IsNullOrWhiteSpace(TierBadgeText);

    public bool ShowQuotaBars => IsOpenAi;

    public bool ShowTokenGrid => !IsOpenAi;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public bool CanActivate => !IsActive;

    public bool CanLaunch => IsActive;

    public double FiveHourUsedRatio => FiveHourUsedPercent / 100d;

    public double WeeklyUsedRatio => WeeklyUsedPercent / 100d;
}

public sealed record AccountEditContext(ProviderDefinition Provider, AccountRecord Account);

public sealed record ActiveAccountSnapshot
{
    public static ActiveAccountSnapshot Empty { get; } = new();
    public string Title { get; init; } = "";
    public string AccountTypeLabel { get; init; } = "";
    public string ProviderBadge { get; init; } = "";
    public string TierBadgeText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string StatusBrush { get; init; } = "#9E9E9E";
    public string Subtitle { get; init; } = "";
    public string PrimaryMetric { get; init; } = "";
    public string SecondaryMetric { get; init; } = "";
    public string DailyTokens { get; init; } = "0";
    public string WeeklyTokens { get; init; } = "0";
    public string MonthlyTokens { get; init; } = "0";
    public bool ShowQuotaBars { get; init; }
    public bool ShowTokenGrid { get; init; }
    public int FiveHourUsedPercent { get; init; }
    public int WeeklyUsedPercent { get; init; }
    public string FiveHourAvailableText { get; init; } = "";
    public string WeeklyAvailableText { get; init; } = "";
    public string FiveHourProgressBrush { get; init; } = "#107C10";
    public string WeeklyProgressBrush { get; init; } = "#107C10";
    public double FiveHourUsedRatio => FiveHourUsedPercent / 100d;
    public double WeeklyUsedRatio => WeeklyUsedPercent / 100d;
    public bool HasProviderBadge => !string.IsNullOrWhiteSpace(ProviderBadge);
    public bool HasTierBadge => !string.IsNullOrWhiteSpace(TierBadgeText);
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
    public bool IsOpenAi { get; init; }
    public bool HasSelection { get; init; }
}
