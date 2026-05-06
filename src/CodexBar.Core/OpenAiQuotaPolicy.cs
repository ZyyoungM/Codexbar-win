namespace CodexBar.Core;

public static class OpenAiQuotaPolicy
{
    public static bool IsOpenAiOAuthAccount(AccountRecord account)
        => string.Equals(account.ProviderId, "openai", StringComparison.OrdinalIgnoreCase) &&
           account.CredentialRef.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase);

    public static bool HasPrimaryQuota(AccountRecord account)
        => account.FiveHourQuota.HasValue;

    public static bool HasWeeklyQuota(AccountRecord account)
        => account.WeeklyQuota.HasValue;

    public static bool HasAnyOfficialQuota(AccountRecord account)
        => HasPrimaryQuota(account) || HasWeeklyQuota(account);

    public static bool HasCompleteOfficialQuota(AccountRecord account)
        => HasPrimaryQuota(account) && HasWeeklyQuota(account);

    public static bool NeedsReauth(AccountRecord account)
        => account.Status == AccountStatus.NeedsReauth ||
           (!string.IsNullOrWhiteSpace(account.OfficialUsageError) &&
            (account.OfficialUsageError.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
             account.OfficialUsageError.Contains("reauth", StringComparison.OrdinalIgnoreCase)));

    public static int DisplaySortBucket(AccountRecord account)
    {
        if (!IsOpenAiOAuthAccount(account))
        {
            return 1;
        }

        if (NeedsReauth(account))
        {
            return 3;
        }

        if (HasCompleteOfficialQuota(account))
        {
            return 0;
        }

        return HasAnyOfficialQuota(account) ? 2 : 3;
    }

    public static int RoutingStatusRank(AccountRecord account)
        => account.Status switch
        {
            AccountStatus.Active => NeedsReauth(account) ? 2 : 0,
            AccountStatus.Stale => NeedsReauth(account) ? 2 : 1,
            AccountStatus.NeedsReauth => 2,
            _ => 3
        };

    public static int RoutingQuotaRank(AccountRecord account)
    {
        if (!IsOpenAiOAuthAccount(account))
        {
            return 3;
        }

        if (HasCompleteOfficialQuota(account))
        {
            return 0;
        }

        if (HasAnyOfficialQuota(account))
        {
            return 1;
        }

        return string.IsNullOrWhiteSpace(account.OfficialUsageError) ? 2 : 3;
    }

    public static int SameQuotaScopeReroutePenalty(AccountRecord account, AccountRecord? requestedAccount)
    {
        if (requestedAccount is null ||
            SameAccount(account, requestedAccount) ||
            !SharesQuotaScope(account, requestedAccount))
        {
            return 0;
        }

        return 1;
    }

    public static bool SharesQuotaScope(AccountRecord left, AccountRecord right)
    {
        var leftScope = ExplicitQuotaScopeKey(left);
        var rightScope = ExplicitQuotaScopeKey(right);
        return !string.IsNullOrWhiteSpace(leftScope) &&
               !string.IsNullOrWhiteSpace(rightScope) &&
               string.Equals(leftScope, rightScope, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasSharedQuotaScope(AccountRecord account, IEnumerable<AccountRecord> accounts)
    {
        var scope = ExplicitQuotaScopeKey(account);
        if (string.IsNullOrWhiteSpace(scope))
        {
            return false;
        }

        return accounts.Any(other => !SameAccount(account, other) && SharesQuotaScope(account, other));
    }

    public static string? EffectiveOpenAiWorkspaceId(AccountRecord account)
        => FirstNonEmpty(account.WorkspaceId, account.OpenAiAccountId);

    public static string? EffectiveQuotaScopeKey(AccountRecord account)
        => FirstNonEmpty(account.QuotaScopeKey, account.WorkspaceId, account.OpenAiAccountId);

    public static int UsedPercentOrMax(QuotaUsageSnapshot snapshot)
    {
        if (snapshot.Used.HasValue && snapshot.Limit.HasValue && snapshot.Limit.Value > 0 && snapshot.Limit.Value != 100)
        {
            return (int)Math.Round(snapshot.Used.Value * 100m / snapshot.Limit.Value, MidpointRounding.AwayFromZero);
        }

        return snapshot.Used ?? int.MaxValue;
    }

    private static string? ExplicitQuotaScopeKey(AccountRecord account)
        => FirstNonEmpty(account.QuotaScopeKey);

    private static bool SameAccount(AccountRecord left, AccountRecord right)
        => string.Equals(left.ProviderId, right.ProviderId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.AccountId, right.AccountId, StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
