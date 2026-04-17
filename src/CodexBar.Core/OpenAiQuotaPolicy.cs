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

    public static int UsedPercentOrMax(QuotaUsageSnapshot snapshot)
    {
        if (snapshot.Used.HasValue && snapshot.Limit.HasValue && snapshot.Limit.Value > 0 && snapshot.Limit.Value != 100)
        {
            return (int)Math.Round(snapshot.Used.Value * 100m / snapshot.Limit.Value, MidpointRounding.AwayFromZero);
        }

        return snapshot.Used ?? int.MaxValue;
    }
}
