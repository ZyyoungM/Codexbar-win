using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using CodexBar.Auth;
using CodexBar.Application;
using CodexBar.CodexCompat;
using CodexBar.Core;
using CodexBar.Runtime;

namespace CodexBar.Win;

public enum SwitchLaunchAction
{
    SwitchOnly,
    LaunchCodex,
    RestartCodexDesktop
}

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

    public bool IsCompactCardDensity => _config.Settings.AccountCardDensity == AccountCardDensity.Compact;

    public string RoutingModeBadgeText => AccountDashboardProjectionService.BuildRoutingModeBadgeText(_config.Settings.OpenAiAccountMode);

    public string RoutingDescriptionText => AccountDashboardProjectionService.BuildRoutingDescriptionText(_config.Settings.OpenAiAccountMode);

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
            var refreshedConfig = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1), refreshOfficialUsage: true);
            if (refreshedConfig != _config)
            {
                _config = refreshedConfig;
                ApplyViewState(_homeLocator.Resolve(), _lastUsageDashboard);
                RefreshLastRefreshText(DateTimeOffset.Now);
            }

            ActivityText = "\u5B98\u65B9\u989D\u5EA6\u5DF2\u540C\u6B65\u3002";
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

    public async Task RefreshAsync(
        string? startActivity = null,
        string? completedActivity = null,
        bool refreshOfficialUsage = true)
    {
        using var _ = EnterBusy();
        await _refreshGate.WaitAsync();
        try
        {
            var refreshedAt = DateTimeOffset.Now;
            ErrorText = "";
            ActivityText = startActivity ?? "\u6B63\u5728\u5237\u65B0...";
            _config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1), refreshOfficialUsage);
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
        => await SwitchOnlyAsync(item);

    public async Task SwitchOnlyAsync(AccountListItem item)
        => await ActivateSelectionAsync(ToSelection(item), SwitchLaunchAction.SwitchOnly);

    public async Task SwitchAndLaunchCodexAsync(AccountListItem item)
        => await ActivateSelectionAsync(ToSelection(item), SwitchLaunchAction.LaunchCodex);

    public async Task SwitchAndRestartCodexDesktopAsync(AccountListItem item)
        => await ActivateSelectionAsync(ToSelection(item), SwitchLaunchAction.RestartCodexDesktop);

    public async Task<CodexDesktopProcessStatus> GetCodexDesktopStatusAsync()
    {
        var config = await _appConfigStore.LoadAsync();
        return new CodexDesktopProcessService().GetStatus(config.Settings.CodexDesktopPath);
    }

    public async Task<bool> IsRestartConfirmationSuppressedAsync()
    {
        var config = await _appConfigStore.LoadAsync();
        return config.Settings.SuppressRestartConfirmation;
    }

    public async Task SetRestartConfirmationSuppressedAsync(bool suppressed)
    {
        var config = await _appConfigStore.LoadAsync();
        _config = config with
        {
            Settings = config.Settings with
            {
                SuppressRestartConfirmation = suppressed
            }
        };
        await _appConfigStore.SaveAsync(_config);
    }

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
                await SwitchAndLaunchCodexAsync(item);
                return;
            }

            var activeSelection = ActiveSelectionResolver.Resolve(_config);
            if (activeSelection.Status == ActiveSelectionResolutionStatus.MissingSelection)
            {
                ErrorText = "\u8BF7\u5148\u9009\u62E9\u4E00\u4E2A\u8D26\u53F7\uFF0C\u6216\u5148\u70B9\u51FB\u201C\u5207\u6362\u201D\u6FC0\u6D3B\u8D26\u53F7\u540E\u518D\u542F\u52A8 Codex\u3002";
                return;
            }

            if (activeSelection.Status == ActiveSelectionResolutionStatus.MissingAccount ||
                activeSelection.Selection is null)
            {
                ErrorText = "\u5F53\u524D\u6FC0\u6D3B\u8D26\u53F7\u5DF2\u4E0D\u5B58\u5728\uFF0C\u8BF7\u5148\u91CD\u65B0\u9009\u62E9\u4E00\u4E2A\u8D26\u53F7\u3002";
                return;
            }

            await ActivateSelectionAsync(activeSelection.Selection, SwitchLaunchAction.LaunchCodex);
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.launch_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u542F\u52A8 Codex \u5931\u8D25";
        }
    }

    public async Task RestartActiveCodexDesktopAsync()
    {
        using var _ = EnterBusy();
        try
        {
            ErrorText = "";
            ActivityText = "\u6B63\u5728\u51C6\u5907\u91CD\u542F Codex Desktop...";
            _config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1), refreshOfficialUsage: false);
            var activeSelection = ActiveSelectionResolver.Resolve(_config);
            if (activeSelection.Status == ActiveSelectionResolutionStatus.MissingSelection)
            {
                ErrorText = "\u8BF7\u5148\u9009\u62E9\u4E00\u4E2A\u8D26\u53F7\uFF0C\u6216\u5148\u70B9\u51FB\u201C\u5207\u6362\u201D\u6FC0\u6D3B\u8D26\u53F7\u540E\u518D\u91CD\u542F Codex\u3002";
                return;
            }

            if (activeSelection.Status == ActiveSelectionResolutionStatus.MissingAccount ||
                activeSelection.Selection is null)
            {
                ErrorText = "\u5F53\u524D\u6FC0\u6D3B\u8D26\u53F7\u5DF2\u4E0D\u5B58\u5728\uFF0C\u8BF7\u5148\u91CD\u65B0\u9009\u62E9\u4E00\u4E2A\u8D26\u53F7\u3002";
                return;
            }

            await ActivateSelectionAsync(activeSelection.Selection, SwitchLaunchAction.RestartCodexDesktop);
        }
        catch (Exception ex)
        {
            _logger.Error("flyout.restart_active_failed", ex);
            ErrorText = DiagnosticLogger.Redact(ex.Message);
            ActivityText = "\u91CD\u542F Codex Desktop \u5931\u8D25";
        }
    }

    public async Task ProbeCompatibleApisAsync(AccountListItem? item)
    {
        using var _ = EnterBusy();
        try
        {
            ErrorText = "";
            ActivityText = "\u6B63\u5728\u63A2\u6D4B API \u8FDE\u901A\u60C5\u51B5...";
            var probe = await NewHealthRefreshWorkflow().ProbeCompatibleApisAsync(item is null ? null : ToSelection(item));
            if (probe.CompatibleProbeCount == 0)
            {
                ErrorText = "\u6CA1\u6709\u53EF\u63A2\u6D4B\u7684\u517C\u5BB9 Provider \u8D26\u53F7\u3002";
                ActivityText = "\u63A2\u6D4B\u672A\u6267\u884C";
                return;
            }

            _config = probe.UpdatedConfig;
            ApplyViewState(_homeLocator.Resolve(), _lastUsageDashboard);
            RefreshLastRefreshText(DateTimeOffset.Now);
            ActivityText = $"\u63A2\u6D4B\u5B8C\u6210\uFF1A{probe.CompatibleProbeSuccessCount}/{probe.CompatibleProbeCount} \u53EF\u7528";
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
            var refresh = await NewHealthRefreshWorkflow().RefreshQuotaAndApisAsync();
            _config = refresh.UpdatedConfig;
            ApplyViewState(_homeLocator.Resolve(), _lastUsageDashboard);
            RefreshLastRefreshText(DateTimeOffset.Now);

            var summaryParts = new List<string>();
            if (refresh.OfficialAccountCount > 0)
            {
                summaryParts.Add(refresh.OfficialFailedCount == 0
                    ? $"\u5B98\u65B9\u989D\u5EA6 {refresh.OfficialAccountCount}/{refresh.OfficialAccountCount} \u5DF2\u5237\u65B0"
                    : $"\u5B98\u65B9\u989D\u5EA6 {refresh.OfficialAccountCount - refresh.OfficialFailedCount}/{refresh.OfficialAccountCount} \u6210\u529F");
            }

            if (refresh.CompatibleProbeCount > 0)
            {
                summaryParts.Add($"API {refresh.CompatibleProbeSuccessCount}/{refresh.CompatibleProbeCount} \u53EF\u7528");
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
            var refresh = await NewHealthRefreshWorkflow().RefreshOfficialQuotaAsync(item is null ? null : ToSelection(item));
            if (refresh.OfficialAccountCount == 0)
            {
                ErrorText = "\u6CA1\u6709\u53EF\u5237\u65B0\u7684 OpenAI \u5B98\u65B9\u8D26\u53F7\u3002";
                ActivityText = "\u5237\u65B0\u672A\u6267\u884C";
                return;
            }

            _config = refresh.UpdatedConfig;
            ApplyViewState(_homeLocator.Resolve(), _lastUsageDashboard);
            RefreshLastRefreshText(DateTimeOffset.Now);

            ActivityText = refresh.OfficialFailedCount == 0
                ? (refresh.OfficialAccountCount == 1 ? "\u5B98\u65B9\u989D\u5EA6\u5DF2\u5237\u65B0\u3002" : "\u5B98\u65B9\u989D\u5EA6\u5DF2\u6279\u91CF\u5237\u65B0\u3002")
                : $"\u5B98\u65B9\u989D\u5EA6\u5237\u65B0\u5B8C\u6210\uFF0C{refresh.OfficialFailedCount}/{refresh.OfficialAccountCount} \u5931\u8D25";
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
                RoutingModeText = AccountDashboardProjectionService.BuildRoutingModeText(mode);
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
            RoutingModeText = AccountDashboardProjectionService.BuildRoutingModeText(mode);
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

    public async Task AddOpenAiOAuthAsync(
        OAuthTokens tokens,
        string label,
        OpenAiWorkspaceDescriptor? selectedWorkspaceHint = null)
    {
        using var _ = EnterBusy();
        ActivityText = "\u6B63\u5728\u4FDD\u5B58 OpenAI \u8D26\u53F7...";
        var identity = OAuthIdentityExtractor.Extract(tokens);
        var tokenAccountId = tokens.AccountId;
        var discoveredWorkspaces = await OpenAiWorkspaceDiscovery.DiscoverAsync(tokens, identity);
        var workspace = OpenAiWorkspaceDiscovery.ResolveCurrentForSave(discoveredWorkspaces, tokens, selectedWorkspaceHint);
        tokens = workspace.ApplyTo(tokens);
        var displayLabel = OpenAiWorkspaceLabelFormatter.ShouldGenerate(label, identity)
            ? OpenAiWorkspaceLabelFormatter.Build(identity, workspace, label)
            : label.Trim();

        _config = await _appConfigStore.LoadAsync();
        _config = await BackfillOAuthIdentitiesAsync(_config);
        var accountId = OpenAiOAuthAccountKey.ResolveAccountId(_config, tokens, identity);
        var existingAccount = _config.Accounts.FirstOrDefault(a => a.ProviderId == "openai" && a.AccountId == accountId);
        var credentialRef = existingAccount?.CredentialRef ?? $"oauth:openai:{accountId}";
        await _secretStore.WriteTokensAsync(credentialRef, tokens);
        _logger.Info("oauth.openai.save", new
        {
            email = identity.Email,
            subjectPresent = !string.IsNullOrWhiteSpace(identity.SubjectId),
            tokenAccountId,
            selectedWorkspaceId = workspace.WorkspaceId,
            selectedWorkspaceName = workspace.WorkspaceName,
            selectedWorkspaceType = workspace.WorkspaceType,
            selectedSeatType = workspace.SeatType,
            discoveredWorkspaceCount = discoveredWorkspaces.Count,
            discoveredWorkspaces = discoveredWorkspaces.Select(item => new
            {
                item.WorkspaceId,
                item.WorkspaceName,
                item.WorkspaceType,
                item.SeatType,
                item.IsCurrent
            }).ToList(),
            localAccountId = accountId,
            existingAccountMatched = existingAccount is not null
        });

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
                Email = identity.Email ?? existingAccount?.Email,
                SubjectId = identity.SubjectId ?? existingAccount?.SubjectId,
                OpenAiAccountId = OpenAiOAuthAccountKey.NormalizeOpenAiAccountId(tokens) ?? existingAccount?.OpenAiAccountId,
                WorkspaceId = workspace.WorkspaceId,
                WorkspaceName = workspace.WorkspaceName,
                WorkspaceType = workspace.WorkspaceType ?? existingAccount?.WorkspaceType,
                SeatType = workspace.SeatType ?? existingAccount?.SeatType,
                QuotaScopeKey = workspace.QuotaScopeKey ?? existingAccount?.QuotaScopeKey,
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
            var tokenCountResetAt = provider.Kind == ProviderKind.OpenAiCompatible && result.ResetTokenCountRequested
                ? DateTimeOffset.UtcNow
                : (DateTimeOffset?)null;
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

                    if (!string.Equals(a.AccountId, result.OriginalAccountId, StringComparison.OrdinalIgnoreCase))
                    {
                        return updated;
                    }

                    updated = updated with { Label = result.AccountLabel };
                    return tokenCountResetAt.HasValue
                        ? updated with { TokenCountResetAt = tokenCountResetAt }
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

            var completedMessage = result.ResetTokenCountRequested
                ? "\u8D26\u53F7\u4FEE\u6539\u5DF2\u4FDD\u5B58\uFF0Ctoken \u8BA1\u6570\u5DF2\u91CD\u7F6E\u3002"
                : "\u8D26\u53F7\u4FEE\u6539\u5DF2\u4FDD\u5B58\u3002";
            await RefreshAsync("\u6B63\u5728\u5237\u65B0\u8D26\u53F7\u5217\u8868...", completedMessage);
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
        => await NewHydrationService().BackfillOAuthIdentitiesAsync(config);

    private async Task<AppConfig> NormalizeManualOrderAsync(AppConfig config)
        => await NewHydrationService().NormalizeManualOrderAsync(config);

    private async Task<AppConfig> LoadHydratedConfigAsync(TimeSpan officialUsageMinRefreshInterval, bool refreshOfficialUsage)
        => await NewHydrationService().HydrateAsync(officialUsageMinRefreshInterval, refreshOfficialUsage);

    private AppConfigHydrationService NewHydrationService()
        => new(_appConfigStore, _secretStore);

    private AccountActivationWorkflow NewActivationWorkflow()
        => new(_appPaths, _appConfigStore, _secretStore, _secretStore, _homeLocator);

    private AccountHealthRefreshWorkflow NewHealthRefreshWorkflow()
        => new(_appConfigStore, _secretStore, _secretStore);

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

    private static CodexSelection ToSelection(AccountListItem item)
        => new() { ProviderId = item.ProviderId, AccountId = item.AccountId };

    private static CodexSelection RefreshSelectionTimestamp(CodexSelection selection)
        => new() { ProviderId = selection.ProviderId, AccountId = selection.AccountId };

    private async Task ActivateSelectionAsync(
        CodexSelection selection,
        SwitchLaunchAction action)
    {
        using var _ = EnterBusy();
        try
        {
            ErrorText = "";
            ActivityText = action switch
            {
                SwitchLaunchAction.LaunchCodex => "\u6B63\u5728\u5207\u6362\u8D26\u53F7\u5E76\u542F\u52A8 Codex...",
                SwitchLaunchAction.RestartCodexDesktop => "\u6B63\u5728\u5207\u6362\u8D26\u53F7\u5E76\u91CD\u542F Codex Desktop...",
                _ => "\u6B63\u5728\u5207\u6362\u8D26\u53F7..."
            };
            _config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1), refreshOfficialUsage: false);
            var requestedSelection = RefreshSelectionTimestamp(selection);
            var activation = await NewActivationWorkflow().ActivateAsync(_config, requestedSelection);
            var result = activation.SwitchResult;

            if (!result.ValidationPassed)
            {
                ErrorText = result.Message;
                return;
            }

            _config = activation.UpdatedConfig;
            await RefreshAsync("\u6B63\u5728\u5237\u65B0\u9762\u677F...", "\u8D26\u53F7\u5DF2\u5207\u6362\u3002");

            switch (action)
            {
                case SwitchLaunchAction.LaunchCodex:
                    await LaunchCurrentCodexAsync("\u5DF2\u6309\u6240\u9009\u8D26\u53F7\u542F\u52A8 Codex\u3002");
                    break;
                case SwitchLaunchAction.RestartCodexDesktop:
                    await RestartCurrentCodexDesktopAsync(
                        "\u5DF2\u6309\u6240\u9009\u8D26\u53F7\u91CD\u542F Codex Desktop\u3002");
                    break;
                default:
                    ActivityText = "\u8D26\u53F7\u5DF2\u5207\u6362\u3002\u53EA\u5F71\u54CD\u65B0\u4F1A\u8BDD\uFF1B\u5DF2\u8FD0\u884C\u7684 Codex \u4E0D\u4F1A\u88AB\u6539\u5199\u3002";
                    break;
            }

            if (activation.GatewayDecision.WasRerouted)
            {
                StatusText = $"{StatusText}\n{activation.GatewayDecision.Message}";
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

    private async Task RestartCurrentCodexDesktopAsync(string successPrefix)
    {
        using var _ = EnterBusy();
        var processService = new CodexDesktopProcessService();
        var status = processService.GetStatus(_config.Settings.CodexDesktopPath);
        _logger.Info("flyout.restart_detected_processes", new
        {
            processes = status.Processes.Select(process => new
            {
                process.ProcessId,
                process.ParentProcessId,
                process.ProcessName,
                process.ExecutablePath,
                process.HasMainWindow
            }).ToList()
        });
        if (!status.IsRunning)
        {
            ActivityText = "\u672A\u68C0\u6D4B\u5230\u8FD0\u884C\u4E2D\u7684 Codex Desktop\uFF0C\u6539\u4E3A\u542F\u52A8\u65B0\u7684 Codex...";
            await LaunchCurrentCodexAsync("\u672A\u68C0\u6D4B\u5230\u8FD0\u884C\u4E2D\u7684 Codex Desktop\uFF0C\u5DF2\u6309\u5F53\u524D\u8D26\u53F7\u542F\u52A8 Codex\u3002");
            return;
        }

        ActivityText = "正在关闭 Codex 并清理后台进程...";
        var closeResult = await processService.RequestCloseAsync(
            status,
            _config.Settings.CodexDesktopPath,
            TimeSpan.FromMilliseconds(250));
        var remainingAfterClose = processService.GetStatus(_config.Settings.CodexDesktopPath);
        _logger.Info("flyout.restart_close_result", new
        {
            closeResult.CloseRequested,
            closeResult.AllExited,
            closeResult.Message,
            remainingProcesses = remainingAfterClose.Processes.Select(process => new
            {
                process.ProcessId,
                process.ParentProcessId,
                process.ProcessName,
                process.ExecutablePath,
                process.HasMainWindow
            }).ToList()
        });
        if (!closeResult.AllExited)
        {
            ActivityText = "正在结束 Codex 后台进程...";
            var terminateResult = await processService.TerminateAfterUserConfirmationAsync(
                remainingAfterClose,
                _config.Settings.CodexDesktopPath,
                TimeSpan.FromSeconds(4));
            var remainingAfterTerminate = processService.GetStatus(_config.Settings.CodexDesktopPath);
            _logger.Info("flyout.restart_terminate_result", new
            {
                terminateResult.TerminateRequested,
                terminateResult.AllExited,
                terminateResult.Message,
                terminateResult.AttemptedRootProcessIds,
                terminateResult.Errors,
                remainingProcesses = remainingAfterTerminate.Processes.Select(process => new
                {
                    process.ProcessId,
                    process.ParentProcessId,
                    process.ProcessName,
                    process.ExecutablePath,
                    process.HasMainWindow
                }).ToList()
            });
            if (!terminateResult.AllExited)
            {
                ErrorText = terminateResult.Message;
                ActivityText = "\u5DF2\u505C\u6B62\u91CD\u542F\uFF1ACodex Desktop \u4ECD\u5728\u540E\u53F0\u8FD0\u884C";
                return;
            }
        }

        ActivityText = "\u6B63\u5728\u91CD\u65B0\u542F\u52A8 Codex Desktop...";
        var launchEnvironment = await CodexLaunchEnvironmentBuilder.BuildAsync(_config, _secretStore);
        var launchResult = await new CodexLaunchService().LaunchAsync(_config.Settings, launchEnvironment);
        if (!launchResult.Launched)
        {
            ErrorText = $"\u91CD\u542F Codex Desktop \u5931\u8D25\uFF1A{launchResult.Message}";
            ActivityText = "\u91CD\u542F Codex Desktop \u5931\u8D25";
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
        var projection = new AccountDashboardProjectionService().Build(_config, home, usageDashboard);
        StatusText = projection.StatusText;
        QuotaStatusText = projection.QuotaStatusText;
        RoutingModeText = projection.RoutingModeText;
        FootnoteText = projection.FootnoteText;
        RaiseRoutingModePropertiesChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompactCardDensity)));

        Accounts.Clear();
        foreach (var account in projection.Accounts)
        {
            Accounts.Add(ToAccountListItem(account));
        }

        UsageText = projection.UsageText;
        ActiveAccount = ToActiveAccountSnapshot(projection.ActiveAccount);
    }

    private static AccountListItem ToAccountListItem(AccountProjectionItem item)
        => new()
        {
            ProviderId = item.ProviderId,
            AccountId = item.AccountId,
            Name = item.Name,
            ProviderBadge = item.ProviderBadge,
            TierBadgeText = item.TierBadgeText,
            CompactMetaText = item.CompactMetaText,
            Subtitle = item.Subtitle,
            IsActive = item.IsActive,
            IsOpenAi = item.IsOpenAi,
            NeedsReauthorization = item.NeedsReauthorization,
            CanProbe = item.CanProbe,
            CanRefreshOfficialQuota = item.CanRefreshOfficialQuota,
            StatusText = item.StatusText,
            StatusBrush = item.StatusBrush,
            DailyTokens = item.DailyTokens,
            WeeklyTokens = item.WeeklyTokens,
            MonthlyTokens = item.MonthlyTokens,
            FiveHourUsedPercent = item.FiveHourUsedPercent,
            WeeklyUsedPercent = item.WeeklyUsedPercent,
            FiveHourQuotaLabel = item.FiveHourQuotaLabel,
            WeeklyQuotaLabel = item.WeeklyQuotaLabel,
            FiveHourQuotaInlineLabel = item.FiveHourQuotaInlineLabel,
            WeeklyQuotaInlineLabel = item.WeeklyQuotaInlineLabel,
            FiveHourAvailableText = item.FiveHourAvailableText,
            WeeklyAvailableText = item.WeeklyAvailableText,
            FiveHourProgressBrush = item.FiveHourProgressBrush,
            WeeklyProgressBrush = item.WeeklyProgressBrush
        };

    private static ActiveAccountSnapshot ToActiveAccountSnapshot(ActiveAccountProjection item)
        => new()
        {
            Title = item.Title,
            AccountTypeLabel = item.AccountTypeLabel,
            ProviderBadge = item.ProviderBadge,
            TierBadgeText = item.TierBadgeText,
            StatusText = item.StatusText,
            StatusBrush = item.StatusBrush,
            Subtitle = item.Subtitle,
            PrimaryMetric = item.PrimaryMetric,
            SecondaryMetric = item.SecondaryMetric,
            DailyTokens = item.DailyTokens,
            WeeklyTokens = item.WeeklyTokens,
            MonthlyTokens = item.MonthlyTokens,
            ShowQuotaBars = item.ShowQuotaBars,
            ShowTokenGrid = item.ShowTokenGrid,
            FiveHourUsedPercent = item.FiveHourUsedPercent,
            WeeklyUsedPercent = item.WeeklyUsedPercent,
            FiveHourQuotaLabel = item.FiveHourQuotaLabel,
            WeeklyQuotaLabel = item.WeeklyQuotaLabel,
            FiveHourQuotaInlineLabel = item.FiveHourQuotaInlineLabel,
            WeeklyQuotaInlineLabel = item.WeeklyQuotaInlineLabel,
            FiveHourAvailableText = item.FiveHourAvailableText,
            WeeklyAvailableText = item.WeeklyAvailableText,
            FiveHourProgressBrush = item.FiveHourProgressBrush,
            WeeklyProgressBrush = item.WeeklyProgressBrush,
            IsOpenAi = item.IsOpenAi,
            HasSelection = item.HasSelection
        };

    private static List<T> Upsert<T>(IEnumerable<T> source, Func<T, bool> predicate, T item)
    {
        var list = source.Where(entry => !predicate(entry)).ToList();
        list.Add(item);
        return list;
    }

    private static int NextManualOrder(AppConfig config)
        => config.Accounts.Count == 0 ? 1 : config.Accounts.Max(account => account.ManualOrder) + 1;

    private void RaiseRoutingModePropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutomaticRouting)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsManualRouting)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoutingModeBadgeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoutingDescriptionText)));
    }

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
    public string CompactMetaText { get; init; } = "";
    public required string Subtitle { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsOpenAi { get; init; }
    public required bool NeedsReauthorization { get; init; }
    public required bool CanProbe { get; init; }
    public required bool CanRefreshOfficialQuota { get; init; }
    public required string StatusText { get; init; }
    public required string StatusBrush { get; init; }
    public required string DailyTokens { get; init; }
    public required string WeeklyTokens { get; init; }
    public required string MonthlyTokens { get; init; }
    public required int FiveHourUsedPercent { get; init; }
    public required int WeeklyUsedPercent { get; init; }
    public required string FiveHourQuotaLabel { get; init; }
    public required string WeeklyQuotaLabel { get; init; }
    public string FiveHourQuotaInlineLabel { get; init; } = "5h@--";
    public string WeeklyQuotaInlineLabel { get; init; } = "week@--";
    public required string FiveHourAvailableText { get; init; }
    public required string WeeklyAvailableText { get; init; }
    public required string FiveHourProgressBrush { get; init; }
    public required string WeeklyProgressBrush { get; init; }

    public bool HasTierBadge => !string.IsNullOrWhiteSpace(TierBadgeText);

    public bool HasCompactMetaText => !string.IsNullOrWhiteSpace(CompactMetaText);

    public bool ShowQuotaBars => IsOpenAi;

    public bool ShowTokenGrid => !IsOpenAi;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public bool CanActivate => !IsActive && !NeedsReauthorization;

    public bool CanLaunch => IsActive && !NeedsReauthorization;

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
    public string FiveHourQuotaLabel { get; init; } = "5h 额度";
    public string WeeklyQuotaLabel { get; init; } = "周额度";
    public string FiveHourQuotaInlineLabel { get; init; } = "5h@--";
    public string WeeklyQuotaInlineLabel { get; init; } = "week@--";
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
