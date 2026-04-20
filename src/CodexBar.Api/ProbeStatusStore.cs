using System.Collections.Concurrent;
using CodexBar.Auth;
using CodexBar.Core;

namespace CodexBar.Api;

public sealed class ProbeStatusStore
{
    private readonly ConcurrentDictionary<string, FrontendConnectionStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);

    public void MarkChecking(IEnumerable<AccountRecord> accounts)
    {
        foreach (var account in accounts)
        {
            _statuses[Key(account.ProviderId, account.AccountId)] = FrontendConnectionStatus.Checking;
        }
    }

    public void Apply(IEnumerable<CompatibleProviderProbeResult> results)
    {
        foreach (var result in results)
        {
            _statuses[Key(result.ProviderId, result.AccountId)] = result.Success
                ? FrontendConnectionStatus.Online
                : FrontendConnectionStatus.Offline;
        }
    }

    public FrontendConnectionStatus Resolve(AccountRecord account)
    {
        if (_statuses.TryGetValue(Key(account.ProviderId, account.AccountId), out var status))
        {
            return status;
        }

        if (string.Equals(account.ProviderId, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return OpenAiQuotaPolicy.NeedsReauth(account) || account.Status == AccountStatus.Revoked
                ? FrontendConnectionStatus.Offline
                : FrontendConnectionStatus.Online;
        }

        return account.Status is AccountStatus.Revoked or AccountStatus.NeedsReauth
            ? FrontendConnectionStatus.Offline
            : FrontendConnectionStatus.Online;
    }

    private static string Key(string providerId, string accountId)
        => $"{providerId}/{accountId}";
}
