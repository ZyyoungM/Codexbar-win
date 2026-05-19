using CodexBar.Auth;
using CodexBar.Core;

namespace CodexBar.Application;

public sealed class AppConfigHydrationService
{
    private readonly AppConfigStore _configStore;
    private readonly IOAuthTokenStore _tokenStore;

    public AppConfigHydrationService(AppConfigStore configStore, IOAuthTokenStore tokenStore)
    {
        _configStore = configStore;
        _tokenStore = tokenStore;
    }

    public async Task<AppConfig> HydrateAsync(
        TimeSpan officialUsageMinRefreshInterval,
        bool refreshOfficialUsage = true,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken);
        config = await BackfillOAuthIdentitiesAsync(config, cancellationToken);
        config = await NormalizeManualOrderAsync(config, cancellationToken);

        if (!refreshOfficialUsage)
        {
            return config;
        }

        var officialUsageRefresh = await new OpenAiOfficialUsageService(_tokenStore)
            .RefreshAsync(config, officialUsageMinRefreshInterval, cancellationToken);
        if (!officialUsageRefresh.Changed)
        {
            return config;
        }

        return await MergeOfficialUsageAccountsAsync(officialUsageRefresh.Config.Accounts, cancellationToken);
    }

    public async Task<AppConfig> BackfillOAuthIdentitiesAsync(
        AppConfig config,
        CancellationToken cancellationToken = default)
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

            var tokens = await _tokenStore.ReadTokensAsync(account.CredentialRef, cancellationToken);
            if (tokens is null)
            {
                accounts.Add(account);
                continue;
            }

            var identity = OAuthIdentityExtractor.Extract(tokens);
            var workspace = OpenAiWorkspaceDiscovery.CurrentOrFallback(tokens, identity);
            var label = account.Label;
            if (string.IsNullOrWhiteSpace(label) || string.Equals(label, "OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                label = OpenAiWorkspaceLabelFormatter.Build(identity, workspace, account.Label);
            }

            var updated = account with
            {
                Label = label,
                Email = account.Email ?? identity.Email,
                SubjectId = account.SubjectId ?? identity.SubjectId,
                OpenAiAccountId = account.OpenAiAccountId ?? OpenAiOAuthAccountKey.NormalizeOpenAiAccountId(tokens),
                WorkspaceId = account.WorkspaceId ?? workspace.WorkspaceId,
                WorkspaceName = account.WorkspaceName ?? workspace.WorkspaceName,
                WorkspaceType = account.WorkspaceType ?? workspace.WorkspaceType,
                SeatType = account.SeatType ?? workspace.SeatType,
                QuotaScopeKey = account.QuotaScopeKey ?? workspace.QuotaScopeKey
            };
            changed |= updated != account;
            accounts.Add(updated);
        }

        if (!changed)
        {
            return config;
        }

        var updatedConfig = config with { Accounts = accounts };
        await _configStore.SaveAsync(updatedConfig, cancellationToken);
        return updatedConfig;
    }

    public async Task<AppConfig> NormalizeManualOrderAsync(
        AppConfig config,
        CancellationToken cancellationToken = default)
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
        await _configStore.SaveAsync(updatedConfig, cancellationToken);
        return updatedConfig;
    }

    public async Task<AppConfig> MergeOfficialUsageAccountsAsync(
        IEnumerable<AccountRecord> refreshedAccounts,
        CancellationToken cancellationToken = default)
    {
        var refreshedMap = refreshedAccounts.ToDictionary(
            account => (account.ProviderId, account.AccountId),
            account => account,
            EqualityComparer<(string ProviderId, string AccountId)>.Default);

        var latest = await _configStore.LoadAsync(cancellationToken);
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
            await _configStore.SaveAsync(merged, cancellationToken);
        }

        return merged;
    }

    private static AccountRecord MergeOfficialUsageFields(AccountRecord current, AccountRecord refreshed)
        => current with
        {
            Email = current.Email ?? refreshed.Email,
            SubjectId = current.SubjectId ?? refreshed.SubjectId,
            OpenAiAccountId = current.OpenAiAccountId ?? refreshed.OpenAiAccountId,
            WorkspaceId = current.WorkspaceId ?? refreshed.WorkspaceId,
            WorkspaceName = current.WorkspaceName ?? refreshed.WorkspaceName,
            WorkspaceType = current.WorkspaceType ?? refreshed.WorkspaceType,
            SeatType = current.SeatType ?? refreshed.SeatType,
            QuotaScopeKey = current.QuotaScopeKey ?? refreshed.QuotaScopeKey,
            Tier = refreshed.Tier,
            OfficialPlanTypeRaw = refreshed.OfficialPlanTypeRaw,
            FiveHourQuota = refreshed.FiveHourQuota,
            WeeklyQuota = refreshed.WeeklyQuota,
            OfficialUsageFetchedAt = refreshed.OfficialUsageFetchedAt,
            OfficialUsageError = refreshed.OfficialUsageError,
            Status = refreshed.Status
        };
}
