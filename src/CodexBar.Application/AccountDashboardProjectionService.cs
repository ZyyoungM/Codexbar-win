using System.Globalization;
using CodexBar.Core;

namespace CodexBar.Application;

public sealed record AccountDashboardProjection
{
    public required string StatusText { get; init; }
    public required string QuotaStatusText { get; init; }
    public required string RoutingModeText { get; init; }
    public required string FootnoteText { get; init; }
    public required string UsageText { get; init; }
    public IReadOnlyList<AccountProjectionItem> Accounts { get; init; } = [];
    public ActiveAccountProjection ActiveAccount { get; init; } = ActiveAccountProjection.Empty;
}

public sealed record AccountProjectionItem
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

public sealed record ActiveAccountProjection
{
    public static ActiveAccountProjection Empty { get; } = new();

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
    public string FiveHourQuotaLabel { get; init; } = "5h \u989D\u5EA6";
    public string WeeklyQuotaLabel { get; init; } = "\u5468\u989D\u5EA6";
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

public sealed class AccountDashboardProjectionService
{
    public AccountDashboardProjection Build(
        AppConfig config,
        CodexHomeState home,
        UsageDashboard usageDashboard)
    {
        var active = config.ActiveSelection is null
            ? "\u5F53\u524D\u672A\u6FC0\u6D3B\u8D26\u53F7"
            : $"\u5F53\u524D\u6FC0\u6D3B\uFF1A{config.ActiveSelection.ProviderId}/{config.ActiveSelection.AccountId}";

        var accounts = OrderedAccounts(config, usageDashboard)
            .Select(account => BuildAccountProjection(config, usageDashboard, account))
            .ToList();

        return new AccountDashboardProjection
        {
            StatusText = $"{active}\n{home.RootPath}",
            QuotaStatusText = BuildQuotaStatusText(config),
            RoutingModeText = BuildRoutingModeText(config.Settings.OpenAiAccountMode),
            FootnoteText = "\u5207\u6362\u4EC5\u5F71\u54CD\u65B0\u4F1A\u8BDD\u00B7\u73B0\u6709\u4F1A\u8BDD\u4FDD\u6301\u4E0D\u53D8",
            UsageText = BuildUsageText(usageDashboard),
            Accounts = accounts,
            ActiveAccount = BuildActiveAccountProjection(config, usageDashboard)
        };
    }

    public static string BuildRoutingModeBadgeText(OpenAiAccountMode mode)
        => mode == OpenAiAccountMode.AggregateGateway ? "\u81EA\u52A8" : "\u624B\u52A8";

    public static string BuildRoutingDescriptionText(OpenAiAccountMode mode)
        => mode == OpenAiAccountMode.AggregateGateway
            ? "\u81EA\u52A8\u6839\u636E\u72B6\u6001\u3001\u989D\u5EA6\u4FE1\u606F\u4E0E\u672C\u5730\u4F7F\u7528\u91CF\u9009\u62E9\u66F4\u5408\u9002\u7684 Provider / \u8D26\u53F7"
            : "\u59CB\u7EC8\u4F7F\u7528\u5F53\u524D\u624B\u52A8\u9009\u4E2D\u7684 Provider / \u8D26\u53F7";

    private static AccountProjectionItem BuildAccountProjection(
        AppConfig config,
        UsageDashboard usageDashboard,
        AccountRecord account)
    {
        var provider = config.Providers.FirstOrDefault(p => p.ProviderId == account.ProviderId);
        var usage = usageDashboard.Accounts.FirstOrDefault(summary =>
            summary.ProviderId == account.ProviderId &&
            summary.AccountId == account.AccountId);
        var isActive =
            config.ActiveSelection?.ProviderId == account.ProviderId &&
            config.ActiveSelection?.AccountId == account.AccountId;
        var useCompactTokenUnit = provider?.Kind == ProviderKind.OpenAiCompatible;
        var fiveHourUsedPercent = ClampUsagePercent(account.FiveHourQuota);
        var weeklyUsedPercent = ClampUsagePercent(account.WeeklyQuota);
        var isOpenAi = OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account);
        var needsReauthorization = isOpenAi && OpenAiQuotaPolicy.NeedsReauth(account);

        return new AccountProjectionItem
        {
            ProviderId = account.ProviderId,
            AccountId = account.AccountId,
            Name = BuildAccountTitle(account),
            ProviderBadge = BuildAccountProviderBadge(provider, account),
            TierBadgeText = BuildAccountTierBadgeText(account),
            CompactMetaText = BuildCompactAccountMetaText(provider, account),
            Subtitle = BuildAccountSubtitle(provider, account, config.Accounts),
            IsActive = isActive,
            IsOpenAi = isOpenAi,
            NeedsReauthorization = needsReauthorization,
            CanProbe = provider?.Kind == ProviderKind.OpenAiCompatible,
            CanRefreshOfficialQuota = isOpenAi,
            StatusText = BuildAccountStatusText(provider, account),
            StatusBrush = BuildAccountStatusBrush(provider, account),
            DailyTokens = FormatTokenCount(usage?.Today.TotalTokens ?? 0, useCompactTokenUnit),
            WeeklyTokens = FormatTokenCount(usage?.Last7Days.TotalTokens ?? 0, useCompactTokenUnit),
            MonthlyTokens = FormatTokenCount(usage?.Last30Days.TotalTokens ?? 0, useCompactTokenUnit),
            FiveHourUsedPercent = fiveHourUsedPercent,
            WeeklyUsedPercent = weeklyUsedPercent,
            FiveHourQuotaLabel = OpenAiQuotaDisplayFormatter.FormatQuotaLabel(account.FiveHourQuota, "5h \u989D\u5EA6"),
            WeeklyQuotaLabel = OpenAiQuotaDisplayFormatter.FormatQuotaLabel(account.WeeklyQuota, "\u5468\u989D\u5EA6"),
            FiveHourQuotaInlineLabel = OpenAiQuotaDisplayFormatter.FormatInlineQuotaLabel(account.FiveHourQuota, "5h"),
            WeeklyQuotaInlineLabel = OpenAiQuotaDisplayFormatter.FormatInlineQuotaLabel(account.WeeklyQuota, "week"),
            FiveHourAvailableText = BuildAvailableQuotaText(account.FiveHourQuota),
            WeeklyAvailableText = BuildAvailableQuotaText(account.WeeklyQuota),
            FiveHourProgressBrush = BuildUsageBrush(fiveHourUsedPercent),
            WeeklyProgressBrush = BuildUsageBrush(weeklyUsedPercent)
        };
    }

    private static string BuildUsageText(UsageDashboard usageDashboard)
    {
        var text =
            $"\u4ECA\u65E5\uFF1A{usageDashboard.Today.TotalTokens:n0} tokens\n" +
            $"\u8FD1 7 \u5929\uFF1A{usageDashboard.Last7Days.TotalTokens:n0} tokens\n" +
            $"\u8FD1 30 \u5929\uFF1A{usageDashboard.Last30Days.TotalTokens:n0} tokens\n" +
            $"\u7D2F\u8BA1\uFF1A{usageDashboard.Lifetime.TotalTokens:n0} tokens";

        if (usageDashboard.UnattributedSessions > 0)
        {
            text += $"\n\u672A\u5F52\u56E0\u4F1A\u8BDD\uFF1A{usageDashboard.UnattributedSessions:n0}";
        }

        return text;
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

    private static string BuildAccountProviderBadge(ProviderDefinition? provider, AccountRecord account)
    {
        if (OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account))
        {
            return "OpenAI";
        }

        return string.IsNullOrWhiteSpace(provider?.DisplayName) ? "\u517C\u5BB9" : provider.DisplayName;
    }

    private static string BuildAccountTitle(AccountRecord account)
    {
        var workspaceName = OpenAiAccountDisplayFormatter.EffectiveWorkspaceName(account);
        if (!OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account) ||
            string.IsNullOrWhiteSpace(account.Email) ||
            string.IsNullOrWhiteSpace(workspaceName) ||
            string.Equals(workspaceName, "Current workspace", StringComparison.OrdinalIgnoreCase))
        {
            return account.Label;
        }

        return IsDuplicateAccountText(account.Email, workspaceName)
            ? account.Email!
            : $"{account.Email} \u00B7 {workspaceName}";
    }

    private static string BuildCompactAccountMetaText(ProviderDefinition? provider, AccountRecord account)
    {
        if (OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account))
        {
            return BuildAccountTierBadgeText(account);
        }

        return string.IsNullOrWhiteSpace(provider?.DisplayName)
            ? account.ProviderId
            : provider.DisplayName;
    }

    private static string BuildAccountSubtitle(
        ProviderDefinition? provider,
        AccountRecord account,
        IReadOnlyList<AccountRecord>? allAccounts = null)
    {
        var subtitle = OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account)
            ? BuildOpenAiWorkspaceSubtitle(account, allAccounts)
            : provider is null ? account.AccountId : BuildCompatibleSubtitle(provider, account);

        return IsDuplicateAccountText(account.Label, subtitle) ? "" : subtitle;
    }

    private static string BuildOpenAiWorkspaceSubtitle(
        AccountRecord account,
        IReadOnlyList<AccountRecord>? allAccounts)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(account.Email))
        {
            parts.Add(account.Email!);
        }

        var tier = OpenAiAccountDisplayFormatter.FormatTier(account);
        if (!string.IsNullOrWhiteSpace(tier))
        {
            parts.Add(tier);
        }

        if (!string.IsNullOrWhiteSpace(account.WorkspaceType) &&
            !string.Equals(account.WorkspaceType, tier, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(account.WorkspaceType!);
        }

        if (!string.IsNullOrWhiteSpace(account.SeatType))
        {
            parts.Add(account.SeatType!);
        }

        if (allAccounts is not null && OpenAiQuotaPolicy.HasSharedQuotaScope(account, allAccounts))
        {
            parts.Add("shared quota");
        }

        return parts.Count == 0 ? "OpenAI OAuth \u8D26\u53F7" : string.Join(" \u00B7 ", parts);
    }

    private static bool IsDuplicateAccountText(string primary, string secondary)
    {
        if (string.IsNullOrWhiteSpace(primary) || string.IsNullOrWhiteSpace(secondary))
        {
            return false;
        }

        return string.Equals(primary.Trim(), secondary.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildAccountTierBadgeText(AccountRecord account)
        => OpenAiQuotaPolicy.IsOpenAiOAuthAccount(account)
            ? OpenAiAccountDisplayFormatter.FormatTier(account) ?? ""
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

    public static string BuildRoutingModeText(OpenAiAccountMode mode)
        => mode == OpenAiAccountMode.AggregateGateway
            ? "OpenAI \u8DEF\u7531\uFF1A\u805A\u5408\u7F51\u5173"
            : "OpenAI \u8DEF\u7531\uFF1A\u624B\u52A8\u5207\u6362";

    private static ActiveAccountProjection BuildActiveAccountProjection(AppConfig config, UsageDashboard usageDashboard)
    {
        if (config.ActiveSelection is null)
        {
            return ActiveAccountProjection.Empty with
            {
                Title = "\u5F53\u524D\u672A\u6FC0\u6D3B\u8D26\u53F7",
                Subtitle = "\u8BF7\u5148\u5728\u4E3B\u6D6E\u7A97\u9009\u62E9\u4E00\u4E2A\u8D26\u53F7\u5E76\u70B9\u51FB\u201C\u5207\u6362\u201D\u3002",
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
            return ActiveAccountProjection.Empty with
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
        var subtitle = BuildAccountSubtitle(provider, account, config.Accounts);

        if (provider.Kind == ProviderKind.OpenAiOAuth)
        {
            var fiveHourUsedPercent = ClampUsagePercent(account.FiveHourQuota);
            var weeklyUsedPercent = ClampUsagePercent(account.WeeklyQuota);
            return new ActiveAccountProjection
            {
                Title = BuildAccountTitle(account),
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
                FiveHourQuotaLabel = OpenAiQuotaDisplayFormatter.FormatQuotaLabel(account.FiveHourQuota, "5h \u989D\u5EA6"),
                WeeklyQuotaLabel = OpenAiQuotaDisplayFormatter.FormatQuotaLabel(account.WeeklyQuota, "\u5468\u989D\u5EA6"),
                FiveHourQuotaInlineLabel = OpenAiQuotaDisplayFormatter.FormatInlineQuotaLabel(account.FiveHourQuota, "5h"),
                WeeklyQuotaInlineLabel = OpenAiQuotaDisplayFormatter.FormatInlineQuotaLabel(account.WeeklyQuota, "week"),
                FiveHourAvailableText = BuildAvailableQuotaText(account.FiveHourQuota),
                WeeklyAvailableText = BuildAvailableQuotaText(account.WeeklyQuota),
                FiveHourProgressBrush = BuildUsageBrush(fiveHourUsedPercent),
                WeeklyProgressBrush = BuildUsageBrush(weeklyUsedPercent),
                IsOpenAi = true,
                HasSelection = true
            };
        }

        return new ActiveAccountProjection
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
            return $"{provider.DisplayName} \u00B7 {provider.BaseUrl}";
        }

        return $"{provider.DisplayName} \u00B7 {account.AccountId}";
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
            var formatted = value.ToString($"F{decimals}", CultureInfo.InvariantCulture)
                .TrimEnd('0')
                .TrimEnd('.');
            return $"{formatted}{unit.Suffix}";
        }

        return tokens.ToString("n0");
    }
}
