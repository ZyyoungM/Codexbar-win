using System.Text;
using CodexBar.Auth;
using CodexBar.CodexCompat;
using CodexBar.Core;
using CodexBar.Runtime;

namespace CodexBar.Api;

public sealed class FrontendBackendService
{
    private readonly AppPaths _appPaths = AppPaths.Resolve();
    private readonly AppConfigStore _appConfigStore;
    private readonly WindowsCredentialSecretStore _secretStore = new();
    private readonly CodexHomeLocator _homeLocator = new();
    private readonly StartupRegistration _startup = new();
    private readonly ProbeStatusStore _probeStatusStore;

    public FrontendBackendService(ProbeStatusStore probeStatusStore)
    {
        _probeStatusStore = probeStatusStore;
        _appPaths.EnsureDirectories();
        _appConfigStore = new AppConfigStore(_appPaths.ConfigPath);
    }

    public async Task<FrontendDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var refreshedAt = DateTimeOffset.Now;
        var config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1), cancellationToken);
        var home = _homeLocator.Resolve();
        var usageDashboard = await new UsageAttributionService(
                new UsageScanner(),
                new SwitchJournalStore(_appPaths.SwitchJournalPath))
            .BuildDashboardAsync(config, home, DateTimeOffset.Now, cancellationToken);

        var usageByAccount = usageDashboard.Accounts.ToDictionary(
            item => (item.ProviderId, item.AccountId),
            item => item,
            EqualityComparer<(string ProviderId, string AccountId)>.Default);

        var accounts = OrderedAccounts(config, usageDashboard)
            .Select(account => ToDashboardAccount(config, account, usageByAccount))
            .ToList();

        return new FrontendDashboardDto(
            home.RootPath,
            config.Settings.OpenAiAccountMode == OpenAiAccountMode.AggregateGateway ? "auto" : "manual",
            config.ModelSettings.Model,
            config.ModelSettings.ModelReasoningEffort,
            refreshedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            BuildQuotaStatusText(config),
            "切换仅影响新会话 · 现有会话保持不变",
            accounts);
    }

    public async Task<FrontendSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var config = await _appConfigStore.LoadAsync(cancellationToken);
        var home = _homeLocator.Resolve();
        var gatewayPreview = await BuildGatewayPreviewAsync(config, cancellationToken);

        return new FrontendSettingsDto(
            _appPaths.AppRoot,
            home.RootPath,
            config.Settings.CodexDesktopPath ?? "",
            config.Settings.CodexCliPath ?? "",
            config.Settings.AccountSortMode == AccountSortMode.Usage ? "usage" : "manual",
            config.Settings.ActivationBehavior == ActivationBehavior.LaunchNewCodex ? "launch" : "write-only",
            config.Settings.OpenAiAccountMode == OpenAiAccountMode.AggregateGateway ? "gateway" : "manual",
            _startup.IsEnabled(),
            gatewayPreview);
    }

    public async Task<FrontendCommandResult> SaveSettingsAsync(
        FrontendSettingsSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var config = await _appConfigStore.LoadAsync(cancellationToken);
        config = config with
        {
            Settings = config.Settings with
            {
                CodexDesktopPath = EmptyToNull(request.CodexDesktopPath),
                CodexCliPath = EmptyToNull(request.CodexCliPath),
                AccountSortMode = string.Equals(request.AccountSortMode, "usage", StringComparison.OrdinalIgnoreCase)
                    ? AccountSortMode.Usage
                    : AccountSortMode.Manual,
                ActivationBehavior = string.Equals(request.ActivationBehavior, "launch", StringComparison.OrdinalIgnoreCase)
                    ? ActivationBehavior.LaunchNewCodex
                    : ActivationBehavior.WriteConfigOnly,
                OpenAiAccountMode = string.Equals(request.OpenAiAccountMode, "gateway", StringComparison.OrdinalIgnoreCase)
                    ? OpenAiAccountMode.AggregateGateway
                    : OpenAiAccountMode.ManualSwitch
            }
        };

        await _appConfigStore.SaveAsync(config, cancellationToken);

        var startupMessage = request.StartupEnabled != _startup.IsEnabled()
            ? " 启动项开关仍需桌面宿主来写入注册表，本轮前端/API 连接未改动该注册表值。"
            : "";

        return new FrontendCommandResult(true, $"设置已保存。{startupMessage}".Trim());
    }

    public FrontendCommandResult DetectDesktop(string configuredPath)
    {
        var detected = new CodexDesktopLocator().Locate(EmptyToNull(configuredPath));
        return string.IsNullOrWhiteSpace(detected)
            ? new FrontendCommandResult(false, "未找到 Codex Desktop。")
            : new FrontendCommandResult(true, detected);
    }

    public async Task<FrontendCommandResult> DetectCliAsync(string configuredPath, CancellationToken cancellationToken = default)
    {
        var detected = await new CodexCliLocator().LocateAsync(EmptyToNull(configuredPath), cancellationToken);
        return detected is null
            ? new FrontendCommandResult(false, "未找到 Codex CLI。")
            : new FrontendCommandResult(true, string.IsNullOrWhiteSpace(detected.Version) ? detected.Path : $"{detected.Path}\n版本：{detected.Version}");
    }

    public async Task<FrontendCommandResult> LaunchTargetAsync(FrontendLaunchRequest request, CancellationToken cancellationToken = default)
    {
        var config = await _appConfigStore.LoadAsync(cancellationToken);
        if (!await RewriteActiveSelectionAsync(config, cancellationToken))
        {
            return new FrontendCommandResult(false, "请先在主浮窗激活一个账号后再测试启动。");
        }

        config = await _appConfigStore.LoadAsync(cancellationToken);
        var launchEnvironment = await CodexLaunchEnvironmentBuilder.BuildAsync(config, _secretStore, cancellationToken);
        var settings = config.Settings with
        {
            ActivationBehavior = ActivationBehavior.LaunchNewCodex,
            CodexDesktopPath = string.Equals(request.Target, "desktop", StringComparison.OrdinalIgnoreCase)
                ? EmptyToNull(request.CodexDesktopPath)
                : null,
            CodexCliPath = string.Equals(request.Target, "cli", StringComparison.OrdinalIgnoreCase)
                ? EmptyToNull(request.CodexCliPath)
                : EmptyToNull(request.CodexCliPath)
        };

        if (string.Equals(request.Target, "cli", StringComparison.OrdinalIgnoreCase))
        {
            settings = settings with { CodexDesktopPath = null };
        }

        var result = await new CodexLaunchService().LaunchAsync(settings, launchEnvironment, cancellationToken);
        return new FrontendCommandResult(result.Launched, result.Message);
    }

    public async Task<(string FileName, string Content)> ExportAccountsCsvAsync(bool includeSecrets, CancellationToken cancellationToken = default)
    {
        var config = await _appConfigStore.LoadAsync(cancellationToken);
        var tempPath = Path.Combine(Path.GetTempPath(), $"codexbar-accounts-{Guid.NewGuid():N}.csv");
        await new AccountCsvService(_secretStore, _secretStore)
            .ExportAsync(config, tempPath, new AccountCsvExportOptions(includeSecrets), cancellationToken);
        var content = await File.ReadAllTextAsync(tempPath, Encoding.UTF8, cancellationToken);
        File.Delete(tempPath);
        return (includeSecrets ? "codexbar-accounts-with-secrets.csv" : "codexbar-accounts.csv", content);
    }

    public async Task<FrontendCommandResult> ImportAccountsCsvAsync(string content, CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"codexbar-import-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken);
        try
        {
            var config = await _appConfigStore.LoadAsync(cancellationToken);
            var (updatedConfig, result) = await new AccountCsvService(_secretStore, _secretStore)
                .ImportAsync(config, tempPath, cancellationToken);
            await _appConfigStore.SaveAsync(updatedConfig, cancellationToken);

            var warnings = result.Warnings.Count == 0 ? "" : $" 警告：{string.Join(" | ", result.Warnings)}";
            return new FrontendCommandResult(true, $"已导入 Provider {result.ProvidersImported} 个，账号 {result.AccountsImported} 个，密钥 {result.SecretsImported} 个。{warnings}".Trim());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task<FrontendCommandResult> ActivateAccountAsync(
        string providerId,
        string accountId,
        bool forceLaunch,
        CancellationToken cancellationToken = default)
    {
        var config = await LoadHydratedConfigAsync(TimeSpan.FromMinutes(1), cancellationToken);
        var requestedSelection = new CodexSelection
        {
            ProviderId = providerId,
            AccountId = accountId
        };
        var aggregateDecision = await new OpenAiAggregateGatewayService(_appPaths, _secretStore)
            .ResolveSelectionAsync(config, requestedSelection, cancellationToken: cancellationToken);
        var result = await NewActivationService()
            .ActivateAsync(config, aggregateDecision.ResolvedSelection, cancellationToken: cancellationToken);
        var journalMessage = aggregateDecision.WasRerouted
            ? $"{aggregateDecision.Message} {result.Message}"
            : result.Message;
        await new SwitchJournalStore(_appPaths.SwitchJournalPath)
            .AppendAsync(result.Selection, result.ValidationPassed ? "ok" : "failed", journalMessage, cancellationToken);

        if (!result.ValidationPassed)
        {
            return new FrontendCommandResult(false, result.Message);
        }

        config = config with
        {
            ActiveSelection = result.Selection,
            Accounts = config.Accounts
                .Select(account => account.ProviderId == result.Selection.ProviderId && account.AccountId == result.Selection.AccountId
                    ? account with { LastUsedAt = DateTimeOffset.UtcNow }
                    : account)
                .ToList()
        };
        await _appConfigStore.SaveAsync(config, cancellationToken);

        if (forceLaunch)
        {
            var launchEnvironment = await CodexLaunchEnvironmentBuilder.BuildAsync(config, _secretStore, cancellationToken);
            var launchResult = await new CodexLaunchService().LaunchAsync(config.Settings, launchEnvironment, cancellationToken);
            return new FrontendCommandResult(launchResult.Launched, launchResult.Message);
        }

        return new FrontendCommandResult(true, aggregateDecision.WasRerouted ? aggregateDecision.Message : "账号已切换。");
    }

    public async Task<FrontendCommandResult> ProbeCompatibleAccountsAsync(
        string? providerId,
        string? accountId,
        CancellationToken cancellationToken = default)
    {
        var config = await _appConfigStore.LoadAsync(cancellationToken);
        var compatibleProviderIds = config.Providers
            .Where(provider => provider.Kind == ProviderKind.OpenAiCompatible)
            .Select(provider => provider.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var accounts = config.Accounts
            .Where(account => compatibleProviderIds.Contains(account.ProviderId))
            .Where(account => string.IsNullOrWhiteSpace(providerId) || string.Equals(account.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .Where(account => string.IsNullOrWhiteSpace(accountId) || string.Equals(account.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (accounts.Count == 0)
        {
            return new FrontendCommandResult(false, "没有可探测的兼容 Provider 账号。");
        }

        _probeStatusStore.MarkChecking(accounts);
        var results = await new CompatibleProviderProbeService(_secretStore).ProbeAsync(config, accounts, cancellationToken);
        _probeStatusStore.Apply(results);

        var successCount = results.Count(result => result.Success);
        return new FrontendCommandResult(
            successCount > 0,
            $"探测完成：{successCount}/{results.Count} 可用。");
    }

    public async Task<FrontendCommandResult> AddCompatibleProviderAsync(
        FrontendCompatibleProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId) ||
            string.IsNullOrWhiteSpace(request.BaseUrl) ||
            string.IsNullOrWhiteSpace(request.AccountId) ||
            string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return new FrontendCommandResult(false, "请填写所有必填项。");
        }

        var credentialRef = $"api-key:{request.ProviderId}:{request.AccountId}";
        await _secretStore.WriteSecretAsync(credentialRef, request.ApiKey, cancellationToken);

        var config = await _appConfigStore.LoadAsync(cancellationToken);
        var existingAccount = config.Accounts.FirstOrDefault(account =>
            string.Equals(account.ProviderId, request.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(account.AccountId, request.AccountId, StringComparison.OrdinalIgnoreCase));

        config = config with
        {
            Providers = Upsert(config.Providers, provider =>
                string.Equals(provider.ProviderId, request.ProviderId, StringComparison.OrdinalIgnoreCase), new ProviderDefinition
            {
                ProviderId = request.ProviderId.Trim(),
                CodexProviderId = EmptyToNull(request.CodexProviderId),
                DisplayName = string.IsNullOrWhiteSpace(request.ProviderName) ? request.ProviderId.Trim() : request.ProviderName.Trim(),
                Kind = ProviderKind.OpenAiCompatible,
                AuthMode = AuthMode.ApiKey,
                BaseUrl = request.BaseUrl.Trim(),
                WireApi = WireApi.Responses,
                SupportsMultiAccount = true
            }),
            Accounts = Upsert(config.Accounts, account =>
                string.Equals(account.ProviderId, request.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(account.AccountId, request.AccountId, StringComparison.OrdinalIgnoreCase), new AccountRecord
            {
                ProviderId = request.ProviderId.Trim(),
                AccountId = request.AccountId.Trim(),
                Label = string.IsNullOrWhiteSpace(request.AccountLabel) ? request.AccountId.Trim() : request.AccountLabel.Trim(),
                CredentialRef = credentialRef,
                Status = AccountStatus.Active,
                CreatedAt = existingAccount?.CreatedAt ?? DateTimeOffset.UtcNow,
                ManualOrder = existingAccount?.ManualOrder ?? NextManualOrder(config)
            })
        };

        await _appConfigStore.SaveAsync(config, cancellationToken);
        return new FrontendCommandResult(true, "兼容 Provider 已保存。");
    }

    public async Task<FrontendCommandResult> ProbeDraftCompatibleProviderAsync(
        FrontendCompatibleProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        var store = new InMemorySecretStore();
        var credentialRef = $"api-key:{request.ProviderId}:{request.AccountId}";
        await store.WriteSecretAsync(credentialRef, request.ApiKey, cancellationToken);

        var provider = new ProviderDefinition
        {
            ProviderId = request.ProviderId,
            CodexProviderId = request.CodexProviderId,
            DisplayName = string.IsNullOrWhiteSpace(request.ProviderName) ? request.ProviderId : request.ProviderName,
            Kind = ProviderKind.OpenAiCompatible,
            AuthMode = AuthMode.ApiKey,
            BaseUrl = request.BaseUrl,
            WireApi = WireApi.Responses,
            SupportsMultiAccount = true
        };
        var account = new AccountRecord
        {
            ProviderId = request.ProviderId,
            AccountId = request.AccountId,
            Label = string.IsNullOrWhiteSpace(request.AccountLabel) ? request.AccountId : request.AccountLabel,
            CredentialRef = credentialRef
        };

        var result = await new CompatibleProviderProbeService(store).ProbeAccountAsync(provider, account, cancellationToken);
        return new FrontendCommandResult(result.Success, result.Message);
    }

    public async Task<FrontendCommandResult> DeleteAccountAsync(string providerId, string accountId, CancellationToken cancellationToken = default)
    {
        var config = await _appConfigStore.LoadAsync(cancellationToken);
        var account = config.Accounts.FirstOrDefault(item =>
            string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            return new FrontendCommandResult(false, "所选账号已不存在。");
        }

        await _secretStore.DeleteSecretAsync(account.CredentialRef, cancellationToken);
        await _secretStore.DeleteTokensAsync(account.CredentialRef, cancellationToken);

        var active = config.ActiveSelection?.ProviderId == providerId && config.ActiveSelection?.AccountId == accountId;
        config = config with
        {
            ActiveSelection = active ? null : config.ActiveSelection,
            Accounts = config.Accounts
                .Where(item => !(string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(item.AccountId, accountId, StringComparison.OrdinalIgnoreCase)))
                .ToList()
        };

        await _appConfigStore.SaveAsync(config, cancellationToken);
        return new FrontendCommandResult(true, "账号已删除。");
    }

    public async Task<FrontendCommandResult> EditAccountAsync(
        FrontendEditAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderId) || string.IsNullOrWhiteSpace(request.AccountId))
        {
            return new FrontendCommandResult(false, "ProviderId / AccountId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AccountLabel))
        {
            return new FrontendCommandResult(false, "Account label is required.");
        }

        var config = await _appConfigStore.LoadAsync(cancellationToken);
        var accountIndex = config.Accounts.FindIndex(account =>
            string.Equals(account.ProviderId, request.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(account.AccountId, request.AccountId, StringComparison.OrdinalIgnoreCase));
        if (accountIndex < 0)
        {
            return new FrontendCommandResult(false, "Account not found.");
        }

        var providerIndex = config.Providers.FindIndex(provider =>
            string.Equals(provider.ProviderId, request.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (providerIndex < 0)
        {
            return new FrontendCommandResult(false, "Provider not found.");
        }

        var account = config.Accounts[accountIndex];
        var provider = config.Providers[providerIndex];

        if (provider.Kind == ProviderKind.OpenAiOAuth &&
            (!string.IsNullOrWhiteSpace(request.BaseUrl) ||
             !string.IsNullOrWhiteSpace(request.ApiKey) ||
             !string.IsNullOrWhiteSpace(request.ProviderName) ||
             !string.IsNullOrWhiteSpace(request.CodexProviderId)))
        {
            return new FrontendCommandResult(false, "OpenAI OAuth account supports label update only.");
        }

        var updatedAccount = account with
        {
            Label = request.AccountLabel.Trim()
        };

        var updatedProvider = provider;
        if (provider.Kind == ProviderKind.OpenAiCompatible)
        {
            var baseUrl = EmptyToNull(request.BaseUrl) ?? provider.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return new FrontendCommandResult(false, "Base URL is required for compatible providers.");
            }

            updatedProvider = provider with
            {
                DisplayName = string.IsNullOrWhiteSpace(request.ProviderName)
                    ? provider.DisplayName
                    : request.ProviderName.Trim(),
                BaseUrl = baseUrl,
                CodexProviderId = string.IsNullOrWhiteSpace(request.CodexProviderId)
                    ? provider.CodexProviderId
                    : request.CodexProviderId.Trim()
            };

            var apiKey = EmptyToNull(request.ApiKey);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                await _secretStore.WriteSecretAsync(account.CredentialRef, apiKey, cancellationToken);
            }
        }

        var updatedAccounts = config.Accounts.ToList();
        updatedAccounts[accountIndex] = updatedAccount;

        var updatedProviders = config.Providers.ToList();
        updatedProviders[providerIndex] = updatedProvider;

        config = config with
        {
            Accounts = updatedAccounts,
            Providers = updatedProviders
        };

        await _appConfigStore.SaveAsync(config, cancellationToken);
        return new FrontendCommandResult(true, "Account updated.");
    }

    public async Task<FrontendCommandResult> ReorderAccountsAsync(
        IReadOnlyList<string> orderedKeys,
        CancellationToken cancellationToken = default)
    {
        var config = await _appConfigStore.LoadAsync(cancellationToken);
        var map = config.Accounts.ToDictionary(
            account => $"{account.ProviderId}/{account.AccountId}",
            account => account,
            StringComparer.OrdinalIgnoreCase);

        if (!HasCompleteDistinctAccountSet(orderedKeys, map))
        {
            return new FrontendCommandResult(false, "排序数据必须覆盖全部账号且不能重复，请刷新后重试。");
        }

        var updatedAccounts = new List<AccountRecord>(config.Accounts.Count);
        var manualOrder = 1;
        foreach (var key in orderedKeys)
        {
            updatedAccounts.Add(map[key] with { ManualOrder = manualOrder++ });
        }

        config = config with { Accounts = updatedAccounts };
        await _appConfigStore.SaveAsync(config, cancellationToken);
        return new FrontendCommandResult(true, "账号顺序已更新。");
    }

    public async Task<FrontendCommandResult> SaveOpenAiOAuthAsync(
        OAuthTokens tokens,
        string label,
        CancellationToken cancellationToken = default)
    {
        var identity = OAuthIdentityExtractor.Extract(tokens);
        var accountId = tokens.AccountId ?? identity.SubjectId ?? identity.Email ?? Guid.NewGuid().ToString("N");
        var displayLabel = string.IsNullOrWhiteSpace(label) || string.Equals(label, "OpenAI", StringComparison.OrdinalIgnoreCase)
            ? identity.BestDisplayName(label)
            : label.Trim();
        var credentialRef = $"oauth:openai:{accountId}";
        await _secretStore.WriteTokensAsync(credentialRef, tokens, cancellationToken);

        var config = await _appConfigStore.LoadAsync(cancellationToken);
        var existingAccount = config.Accounts.FirstOrDefault(account =>
            string.Equals(account.ProviderId, "openai", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(account.AccountId, accountId, StringComparison.OrdinalIgnoreCase));

        config = config with
        {
            Providers = Upsert(config.Providers, provider =>
                string.Equals(provider.ProviderId, "openai", StringComparison.OrdinalIgnoreCase), new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth,
                WireApi = WireApi.Responses,
                SupportsMultiAccount = true
            }),
            Accounts = Upsert(config.Accounts, account =>
                string.Equals(account.ProviderId, "openai", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(account.AccountId, accountId, StringComparison.OrdinalIgnoreCase), new AccountRecord
            {
                ProviderId = "openai",
                AccountId = accountId,
                Label = displayLabel,
                Email = identity.Email,
                SubjectId = identity.SubjectId,
                CredentialRef = credentialRef,
                Status = AccountStatus.Active,
                CreatedAt = existingAccount?.CreatedAt ?? DateTimeOffset.UtcNow,
                ManualOrder = existingAccount?.ManualOrder ?? NextManualOrder(config)
            })
        };

        await _appConfigStore.SaveAsync(config, cancellationToken);
        return new FrontendCommandResult(true, "OpenAI 账号已保存。");
    }

    private async Task<bool> RewriteActiveSelectionAsync(AppConfig config, CancellationToken cancellationToken)
    {
        if (config.ActiveSelection is null)
        {
            return false;
        }

        var decision = await new OpenAiAggregateGatewayService(_appPaths, _secretStore)
            .ResolveSelectionAsync(config, config.ActiveSelection, cancellationToken: cancellationToken);
        var result = await NewActivationService().ActivateAsync(config, decision.ResolvedSelection, cancellationToken: cancellationToken);
        var journalMessage = decision.WasRerouted
            ? $"{decision.Message} {result.Message}"
            : result.Message;
        await new SwitchJournalStore(_appPaths.SwitchJournalPath)
            .AppendAsync(result.Selection, result.ValidationPassed ? "ok" : "failed", journalMessage, cancellationToken);

        if (!result.ValidationPassed)
        {
            return false;
        }

        config = config with
        {
            ActiveSelection = result.Selection,
            Accounts = config.Accounts
                .Select(account => account.ProviderId == result.Selection.ProviderId && account.AccountId == result.Selection.AccountId
                    ? account with { LastUsedAt = DateTimeOffset.UtcNow }
                    : account)
                .ToList()
        };
        await _appConfigStore.SaveAsync(config, cancellationToken);
        return true;
    }

    private async Task<AppConfig> LoadHydratedConfigAsync(TimeSpan officialUsageMinRefreshInterval, CancellationToken cancellationToken)
    {
        var config = await _appConfigStore.LoadAsync(cancellationToken);
        config = await BackfillOAuthIdentitiesAsync(config, cancellationToken);
        config = await NormalizeManualOrderAsync(config, cancellationToken);

        var officialUsageRefresh = await new OpenAiOfficialUsageService(_secretStore)
            .RefreshAsync(config, officialUsageMinRefreshInterval, cancellationToken);
        if (officialUsageRefresh.Changed)
        {
            await _appConfigStore.SaveAsync(officialUsageRefresh.Config, cancellationToken);
        }

        return officialUsageRefresh.Config;
    }

    private async Task<AppConfig> BackfillOAuthIdentitiesAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var changed = false;
        var accounts = new List<AccountRecord>(config.Accounts.Count);
        foreach (var account in config.Accounts)
        {
            if (!string.Equals(account.ProviderId, "openai", StringComparison.OrdinalIgnoreCase) ||
                !account.CredentialRef.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase))
            {
                accounts.Add(account);
                continue;
            }

            var tokens = await _secretStore.ReadTokensAsync(account.CredentialRef, cancellationToken);
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
        await _appConfigStore.SaveAsync(updatedConfig, cancellationToken);
        return updatedConfig;
    }

    private async Task<AppConfig> NormalizeManualOrderAsync(AppConfig config, CancellationToken cancellationToken)
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

        var updatedConfig = config with { Accounts = accounts };
        await _appConfigStore.SaveAsync(updatedConfig, cancellationToken);
        return updatedConfig;
    }

    private async Task<FrontendGatewayPreviewDto?> BuildGatewayPreviewAsync(AppConfig config, CancellationToken cancellationToken)
    {
        if (config.Settings.OpenAiAccountMode != OpenAiAccountMode.AggregateGateway)
        {
            return null;
        }

        var fallbackAccount = config.Accounts.FirstOrDefault(OpenAiQuotaPolicy.IsOpenAiOAuthAccount);
        var requested = config.ActiveSelection ?? (fallbackAccount is null
            ? null
            : new CodexSelection { ProviderId = fallbackAccount.ProviderId, AccountId = fallbackAccount.AccountId });
        if (requested is null)
        {
            return null;
        }

        var decision = await new OpenAiAggregateGatewayService(_appPaths, _secretStore)
            .ResolveSelectionAsync(config, requested, cancellationToken: cancellationToken);
        var requestedAccount = config.Accounts.FirstOrDefault(account =>
            string.Equals(account.ProviderId, requested.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(account.AccountId, requested.AccountId, StringComparison.OrdinalIgnoreCase));
        var resolvedAccount = config.Accounts.FirstOrDefault(account =>
            string.Equals(account.ProviderId, decision.ResolvedSelection.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(account.AccountId, decision.ResolvedSelection.AccountId, StringComparison.OrdinalIgnoreCase));

        return new FrontendGatewayPreviewDto(
            requestedAccount?.Label ?? requested.AccountId,
            resolvedAccount?.Label ?? decision.ResolvedSelection.AccountId,
            decision.Message);
    }

    private FrontendAccountDto ToDashboardAccount(
        AppConfig config,
        AccountRecord account,
        IReadOnlyDictionary<(string ProviderId, string AccountId), AccountUsageSummary> usageByAccount)
    {
        var provider = config.Providers.FirstOrDefault(item => item.ProviderId == account.ProviderId);
        var isOpenAi = provider?.Kind == ProviderKind.OpenAiOAuth || OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account);
        usageByAccount.TryGetValue((account.ProviderId, account.AccountId), out var usage);

        return new FrontendAccountDto(
            account.ProviderId,
            account.AccountId,
            account.Label,
            isOpenAi ? "openai" : "compatible",
            account.Email,
            provider?.BaseUrl,
            config.ActiveSelection?.ProviderId == account.ProviderId && config.ActiveSelection?.AccountId == account.AccountId,
            _probeStatusStore.Resolve(account),
            isOpenAi ? NormalizePercent(account.FiveHourQuota) : null,
            isOpenAi ? NormalizePercent(account.WeeklyQuota) : null,
            isOpenAi ? null : usage?.Today.TotalTokens,
            isOpenAi ? null : usage?.Last7Days.TotalTokens,
            isOpenAi ? null : usage?.Last30Days.TotalTokens);
    }

    private static int? NormalizePercent(QuotaUsageSnapshot snapshot)
    {
        if (!snapshot.HasValue)
        {
            return null;
        }

        var used = OpenAiQuotaPolicy.UsedPercentOrMax(snapshot);
        return used == int.MaxValue ? null : Math.Clamp(used, 0, 100);
    }

    private CodexActivationService NewActivationService()
        => new(
            _homeLocator,
            new CodexConfigStore(),
            new CodexAuthStore(),
            new CodexStateTransaction(_appPaths),
            new CodexIntegrityChecker(),
            _secretStore,
            _secretStore);

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
                : $"OpenAI 官方额度已同步：{withQuota}/{openAiAccounts.Count} 个账号。";
        }

        var reauthCount = failed.Count(OpenAiQuotaPolicy.NeedsReauth);
        var sample = string.Join(", ", failed.Take(2).Select(account => account.Label));
        var suffix = failed.Count > 2 ? "，..." : "";
        var reauthHint = reauthCount > 0 ? $" 其中 {reauthCount} 个需要重新登录。" : "";
        return $"OpenAI 官方额度刷新失败：{failed.Count}/{openAiAccounts.Count} 个账号受影响，示例：{sample}{suffix}。{reauthHint}";
    }

    private static List<T> Upsert<T>(IEnumerable<T> source, Func<T, bool> predicate, T item)
    {
        var list = source.Where(entry => !predicate(entry)).ToList();
        list.Add(item);
        return list;
    }

    private static bool HasCompleteDistinctAccountSet(
        IReadOnlyList<string> orderedKeys,
        IReadOnlyDictionary<string, AccountRecord> accountMap)
    {
        if (orderedKeys.Count == 0 || orderedKeys.Count != accountMap.Count)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in orderedKeys)
        {
            if (!accountMap.ContainsKey(key) || !seen.Add(key))
            {
                return false;
            }
        }

        return true;
    }

    private static int NextManualOrder(AppConfig config)
        => config.Accounts.Count == 0 ? 1 : config.Accounts.Max(account => account.ManualOrder) + 1;

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
