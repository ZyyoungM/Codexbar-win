using CodexBar.Auth;
using CodexBar.Core;

namespace CodexBar.Application;

public sealed record AccountHealthRefreshResult
{
    public required AppConfig UpdatedConfig { get; init; }
    public IReadOnlyList<AccountRecord> RefreshedOfficialAccounts { get; init; } = [];
    public IReadOnlyList<AccountRecord> CompatibleAccountsProbed { get; init; } = [];
    public IReadOnlyList<CompatibleProviderProbeResult> CompatibleProbeResults { get; init; } = [];
    public int OfficialAccountCount => RefreshedOfficialAccounts.Count;
    public int OfficialFailedCount => RefreshedOfficialAccounts.Count(account => !string.IsNullOrWhiteSpace(account.OfficialUsageError));
    public int CompatibleProbeCount => CompatibleProbeResults.Count;
    public int CompatibleProbeSuccessCount => CompatibleProbeResults.Count(result => result.Success);
}

public sealed class AccountHealthRefreshWorkflow
{
    private readonly AppConfigStore _configStore;
    private readonly AppConfigHydrationService _hydrationService;
    private readonly CompatibleProbeResultApplyService _probeResultApplyService;
    private readonly Func<AccountRecord, CancellationToken, Task<AccountRecord>> _refreshOfficialAccountAsync;
    private readonly Func<AppConfig, IReadOnlyList<AccountRecord>, CancellationToken, Task<IReadOnlyList<CompatibleProviderProbeResult>>> _probeCompatibleAccountsAsync;

    public AccountHealthRefreshWorkflow(
        AppConfigStore configStore,
        ISecretStore secretStore,
        IOAuthTokenStore tokenStore)
    {
        _configStore = configStore;
        _hydrationService = new AppConfigHydrationService(configStore, tokenStore);
        _probeResultApplyService = new CompatibleProbeResultApplyService(configStore);

        var officialUsageService = new OpenAiOfficialUsageService(tokenStore);
        var compatibleProbeService = new CompatibleProviderProbeService(secretStore);
        _refreshOfficialAccountAsync = officialUsageService.RefreshAccountAsync;
        _probeCompatibleAccountsAsync = (config, accounts, cancellationToken) =>
            compatibleProbeService.ProbeAsync(config, accounts, cancellationToken);
    }

    public AccountHealthRefreshWorkflow(
        AppConfigStore configStore,
        AppConfigHydrationService hydrationService,
        CompatibleProbeResultApplyService probeResultApplyService,
        Func<AccountRecord, CancellationToken, Task<AccountRecord>> refreshOfficialAccountAsync,
        Func<AppConfig, IReadOnlyList<AccountRecord>, CancellationToken, Task<IReadOnlyList<CompatibleProviderProbeResult>>> probeCompatibleAccountsAsync)
    {
        _configStore = configStore;
        _hydrationService = hydrationService;
        _probeResultApplyService = probeResultApplyService;
        _refreshOfficialAccountAsync = refreshOfficialAccountAsync;
        _probeCompatibleAccountsAsync = probeCompatibleAccountsAsync;
    }

    public async Task<AccountHealthRefreshResult> RefreshQuotaAndApisAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken);
        var officialAccounts = config.Accounts
            .Where(OpenAiQuotaPolicy.IsOpenAiOAuthAccount)
            .ToList();

        var refreshedOfficialAccounts = new List<AccountRecord>(officialAccounts.Count);
        var current = config;
        foreach (var account in officialAccounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            refreshedOfficialAccounts.Add(await _refreshOfficialAccountAsync(account, cancellationToken));
        }

        if (refreshedOfficialAccounts.Count > 0)
        {
            current = await _hydrationService.MergeOfficialUsageAccountsAsync(refreshedOfficialAccounts, cancellationToken);
        }

        var compatibleProviderIds = current.Providers
            .Where(provider => provider.Kind == ProviderKind.OpenAiCompatible)
            .Select(provider => provider.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var compatibleAccounts = current.Accounts
            .Where(account => compatibleProviderIds.Contains(account.ProviderId))
            .ToList();

        IReadOnlyList<CompatibleProviderProbeResult> probeResults = [];
        if (compatibleAccounts.Count > 0)
        {
            probeResults = await _probeCompatibleAccountsAsync(current, compatibleAccounts, cancellationToken);
            current = await _probeResultApplyService.ApplyAsync(probeResults, cancellationToken);
        }

        return new AccountHealthRefreshResult
        {
            UpdatedConfig = current,
            RefreshedOfficialAccounts = refreshedOfficialAccounts,
            CompatibleAccountsProbed = compatibleAccounts,
            CompatibleProbeResults = probeResults
        };
    }

    public async Task<AccountHealthRefreshResult> RefreshOfficialQuotaAsync(
        CodexSelection? targetSelection = null,
        CancellationToken cancellationToken = default)
        => await RefreshOfficialQuotaAsync(targetSelection?.ProviderId, targetSelection?.AccountId, cancellationToken);

    public async Task<AccountHealthRefreshResult> RefreshOfficialQuotaAsync(
        string? providerId,
        string? accountId,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken);
        var targetAccounts = config.Accounts
            .Where(OpenAiQuotaPolicy.IsOpenAiOAuthAccount)
            .Where(account => MatchesFilter(account, providerId, accountId))
            .ToList();

        var refreshedAccounts = new List<AccountRecord>(targetAccounts.Count);
        foreach (var account in targetAccounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            refreshedAccounts.Add(await _refreshOfficialAccountAsync(account, cancellationToken));
        }

        var updated = refreshedAccounts.Count == 0
            ? config
            : await _hydrationService.MergeOfficialUsageAccountsAsync(refreshedAccounts, cancellationToken);

        return new AccountHealthRefreshResult
        {
            UpdatedConfig = updated,
            RefreshedOfficialAccounts = refreshedAccounts
        };
    }

    public async Task<AccountHealthRefreshResult> ProbeCompatibleApisAsync(
        CodexSelection? targetSelection = null,
        CancellationToken cancellationToken = default)
        => await ProbeCompatibleApisAsync(targetSelection?.ProviderId, targetSelection?.AccountId, cancellationToken);

    public async Task<AccountHealthRefreshResult> ProbeCompatibleApisAsync(
        string? providerId,
        string? accountId,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken);
        var accounts = SelectCompatibleAccounts(config, providerId, accountId);

        if (accounts.Count == 0)
        {
            return new AccountHealthRefreshResult { UpdatedConfig = config };
        }

        var probeResults = await _probeCompatibleAccountsAsync(config, accounts, cancellationToken);
        var updated = await _probeResultApplyService.ApplyAsync(probeResults, cancellationToken);
        return new AccountHealthRefreshResult
        {
            UpdatedConfig = updated,
            CompatibleAccountsProbed = accounts,
            CompatibleProbeResults = probeResults
        };
    }

    public async Task<IReadOnlyList<AccountRecord>> ListCompatibleProbeTargetsAsync(
        string? providerId,
        string? accountId,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken);
        return SelectCompatibleAccounts(config, providerId, accountId);
    }

    private static bool MatchesFilter(AccountRecord account, string? providerId, string? accountId)
        => (string.IsNullOrWhiteSpace(providerId) || string.Equals(account.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)) &&
           (string.IsNullOrWhiteSpace(accountId) || string.Equals(account.AccountId, accountId, StringComparison.OrdinalIgnoreCase));

    private static List<AccountRecord> SelectCompatibleAccounts(AppConfig config, string? providerId, string? accountId)
    {
        var compatibleProviderIds = config.Providers
            .Where(provider => provider.Kind == ProviderKind.OpenAiCompatible)
            .Select(provider => provider.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return config.Accounts
            .Where(account => compatibleProviderIds.Contains(account.ProviderId))
            .Where(account => MatchesFilter(account, providerId, accountId))
            .ToList();
    }
}
