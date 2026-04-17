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
        private set => SetField(ref _activityText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set => SetField(ref _errorText, value);
    }

    public string QuotaStatusText
    {
        get => _quotaStatusText;
        private set => SetField(ref _quotaStatusText, value);
    }

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set => SetField(ref _lastRefreshText, value);
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
            _config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1));
            var home = _homeLocator.Resolve();
            var usageDashboard = await new UsageAttributionService(
                new UsageScanner(),
                new SwitchJournalStore(_appPaths.SwitchJournalPath))
                .BuildDashboardAsync(_config, home, DateTimeOffset.Now);
            var active = _config.ActiveSelection is null
                ? "\u5F53\u524D\u672A\u6FC0\u6D3B\u8D26\u53F7"
                : $"\u5F53\u524D\u6FC0\u6D3B\uFF1A{_config.ActiveSelection.ProviderId}/{_config.ActiveSelection.AccountId}";
            StatusText = $"{active}\n{home.RootPath}";
            QuotaStatusText = BuildQuotaStatusText(_config);

            Accounts.Clear();
            foreach (var account in OrderedAccounts(_config, usageDashboard))
            {
                var provider = _config.Providers.FirstOrDefault(p => p.ProviderId == account.ProviderId);
                var usage = usageDashboard.Accounts.FirstOrDefault(summary => summary.ProviderId == account.ProviderId && summary.AccountId == account.AccountId);
                var marker =
                    _config.ActiveSelection?.ProviderId == account.ProviderId &&
                    _config.ActiveSelection?.AccountId == account.AccountId
                        ? "[\u5F53\u524D] "
                        : "";
                var usageSuffix = _config.Settings.AccountSortMode == AccountSortMode.Usage && usage is not null
                    ? $"\uFF08\u8FD130\u5929 {usage.Last30Days.TotalTokens:n0}\uFF09"
                    : "";
                var officialSuffix = BuildOfficialUsageSuffix(account);
                Accounts.Add(new AccountListItem(
                    account.ProviderId,
                    account.AccountId,
                    $"{marker}{provider?.DisplayName ?? account.ProviderId} / {account.Label}{officialSuffix}{usageSuffix}"));
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

            LastRefreshText = $"\u4E0A\u6B21\u5237\u65B0\uFF1A{refreshedAt:yyyy-MM-dd HH:mm:ss}";
            ActivityText = completedActivity ?? $"\u5237\u65B0\u5B8C\u6210\uFF1A{refreshedAt:HH:mm:ss}";
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
            _config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1));
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

            await ActivateSelectionAsync(new AccountListItem(activeAccount.ProviderId, activeAccount.AccountId, activeAccount.Label), forceLaunch: true);
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.launch_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u542F\u52A8 Codex \u5931\u8D25";
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
            var provider = _config.Providers.FirstOrDefault(p => p.ProviderId == result.ProviderId);
            var account = _config.Accounts.FirstOrDefault(a => a.ProviderId == result.ProviderId && a.AccountId == result.AccountId);
            if (provider is null || account is null)
            {
                ErrorText = "\u6240\u9009\u8D26\u53F7\u5DF2\u4E0D\u5B58\u5728\u3002";
                return;
            }

            if (provider.Kind == ProviderKind.OpenAiCompatible && !string.IsNullOrWhiteSpace(result.ApiKey))
            {
                await _secretStore.WriteSecretAsync(account.CredentialRef, result.ApiKey);
            }

            _config = _config with
            {
                Providers = _config.Providers.Select(p =>
                    p.ProviderId == result.ProviderId && p.Kind == ProviderKind.OpenAiCompatible
                        ? p with { DisplayName = result.ProviderName, BaseUrl = result.BaseUrl }
                        : p).ToList(),
                Accounts = _config.Accounts.Select(a =>
                    a.ProviderId == result.ProviderId && a.AccountId == result.AccountId
                        ? a with { Label = result.AccountLabel }
                        : a).ToList()
            };

            await _appConfigStore.SaveAsync(_config);
            await RefreshAsync("\u6B63\u5728\u5237\u65B0\u8D26\u53F7\u5217\u8868...", "\u8D26\u53F7\u4FEE\u6539\u5DF2\u4FDD\u5B58\u3002");
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.edit_account_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u4FDD\u5B58\u4FEE\u6539\u5931\u8D25";
        }
    }

    public async Task MoveAsync(AccountListItem item, int direction)
    {
        using var _ = EnterBusy();
        ActivityText = direction < 0 ? "\u6B63\u5728\u4E0A\u79FB\u8D26\u53F7..." : "\u6B63\u5728\u4E0B\u79FB\u8D26\u53F7...";
        _config = await _appConfigStore.LoadAsync();
        _config = await NormalizeManualOrderAsync(_config);
        var ordered = OrderedAccountsByManualOrder(_config).ToList();
        var index = ordered.FindIndex(a => a.ProviderId == item.ProviderId && a.AccountId == item.AccountId);
        var targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= ordered.Count)
        {
            return;
        }

        var current = ordered[index];
        var target = ordered[targetIndex];
        _config = _config with
        {
            Accounts = _config.Accounts.Select(account =>
            {
                if (account.ProviderId == current.ProviderId && account.AccountId == current.AccountId)
                {
                    return account with { ManualOrder = target.ManualOrder };
                }

                if (account.ProviderId == target.ProviderId && account.AccountId == target.AccountId)
                {
                    return account with { ManualOrder = current.ManualOrder };
                }

                return account;
            }).ToList()
        };

        await _appConfigStore.SaveAsync(_config);
        await RefreshAsync("\u6B63\u5728\u5237\u65B0\u6392\u5E8F...", "\u8D26\u53F7\u987A\u5E8F\u5DF2\u66F4\u65B0\u3002");
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

    private async Task<AppConfig> LoadHydratedConfigAsync(TimeSpan officialUsageMinRefreshInterval)
    {
        var config = await _appConfigStore.LoadAsync();
        config = await BackfillOAuthIdentitiesAsync(config);
        config = await NormalizeManualOrderAsync(config);

        var officialUsageRefresh = await new OpenAiOfficialUsageService(_secretStore)
            .RefreshAsync(config, officialUsageMinRefreshInterval);
        if (officialUsageRefresh.Changed)
        {
            await _appConfigStore.SaveAsync(officialUsageRefresh.Config);
        }

        return officialUsageRefresh.Config;
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
            _config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1));
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
                var launchResult = await new CodexLaunchService().LaunchIfConfiguredAsync(_config.Settings);
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
        var launchResult = await new CodexLaunchService().LaunchAsync(_config.Settings);
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

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
        if (account.Tier != AccountTier.Unknown)
        {
            return account.Tier.ToString().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(account.OfficialPlanTypeRaw))
        {
            return $"\u5957\u9910:{account.OfficialPlanTypeRaw}";
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
    {
        var openAiAccounts = config.Accounts
            .Where(OpenAiQuotaPolicy.IsOpenAiOAuthAccount)
            .ToList();
        if (openAiAccounts.Count == 0)
        {
            return "";
        }

        var failed = openAiAccounts
            .Where(account => !string.IsNullOrWhiteSpace(account.OfficialUsageError))
            .ToList();
        if (failed.Count == 0)
        {
            var withQuota = openAiAccounts.Count(OpenAiQuotaPolicy.HasAnyOfficialQuota);
            return withQuota == 0
                ? ""
                : $"OpenAI \u5B98\u65B9\u989D\u5EA6\u5DF2\u540C\u6B65\uFF1A{withQuota}/{openAiAccounts.Count} \u4E2A\u8D26\u53F7\u3002\u6309\u7528\u91CF\u6392\u5E8F\u548C\u805A\u5408\u8DEF\u7531\u4F1A\u4F18\u5148\u9009\u62E9 5h / \u5468\u538B\u529B\u66F4\u4F4E\u7684\u8D26\u53F7\u3002";
        }

        var reauthCount = failed.Count(OpenAiQuotaPolicy.NeedsReauth);
        var sample = string.Join(", ", failed.Take(2).Select(account => account.Label));
        var suffix = failed.Count > 2 ? "\uFF0C..." : "";
        var reauthHint = reauthCount > 0
            ? $" \u5176\u4E2D {reauthCount} \u4E2A\u9700\u8981\u91CD\u65B0\u767B\u5F55\u3002"
            : "";
        return $"OpenAI \u5B98\u65B9\u989D\u5EA6\u5237\u65B0\u5931\u8D25\uFF1A{failed.Count}/{openAiAccounts.Count} \u4E2A\u8D26\u53F7\u53D7\u5F71\u54CD\uFF0C\u793A\u4F8B\uFF1A{sample}{suffix}\u3002{reauthHint}\u6CA1\u6709\u989D\u5EA6\u5FEB\u7167\u65F6\uFF0C\u4F1A\u81EA\u52A8\u56DE\u9000\u5230\u672C\u5730\u4F7F\u7528\u91CF\u6392\u5E8F\u548C\u805A\u5408\u8DEF\u7531\u3002";
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

public sealed record AccountListItem(string ProviderId, string AccountId, string Display);

public sealed record AccountEditContext(ProviderDefinition Provider, AccountRecord Account);
