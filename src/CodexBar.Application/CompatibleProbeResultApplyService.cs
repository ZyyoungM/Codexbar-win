using CodexBar.Auth;
using CodexBar.Core;

namespace CodexBar.Application;

public sealed class CompatibleProbeResultApplyService
{
    private readonly AppConfigStore _configStore;

    public CompatibleProbeResultApplyService(AppConfigStore configStore)
    {
        _configStore = configStore;
    }

    public async Task<AppConfig> ApplyAsync(
        IEnumerable<CompatibleProviderProbeResult> results,
        CancellationToken cancellationToken = default)
    {
        var resultMap = results.ToDictionary(
            result => (result.ProviderId, result.AccountId),
            result => result,
            EqualityComparer<(string ProviderId, string AccountId)>.Default);

        var latest = await _configStore.LoadAsync(cancellationToken);
        if (resultMap.Count == 0)
        {
            return latest;
        }

        var updated = latest with
        {
            Accounts = latest.Accounts
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

        if (updated != latest)
        {
            await _configStore.SaveAsync(updated, cancellationToken);
        }

        return updated;
    }
}
