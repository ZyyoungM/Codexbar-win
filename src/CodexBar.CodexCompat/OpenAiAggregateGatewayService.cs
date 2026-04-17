using CodexBar.Core;

namespace CodexBar.CodexCompat;

public sealed record OpenAiAggregateGatewayDecision
{
    public required CodexSelection RequestedSelection { get; init; }
    public required CodexSelection ResolvedSelection { get; init; }
    public required string Message { get; init; }
    public bool WasRerouted =>
        !string.Equals(RequestedSelection.ProviderId, ResolvedSelection.ProviderId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(RequestedSelection.AccountId, ResolvedSelection.AccountId, StringComparison.OrdinalIgnoreCase);
}

public sealed class OpenAiAggregateGatewayService
{
    private readonly AppPaths _appPaths;
    private readonly CodexHomeLocator _homeLocator;
    private readonly IOAuthTokenStore _tokenStore;

    public OpenAiAggregateGatewayService(
        AppPaths appPaths,
        IOAuthTokenStore tokenStore,
        CodexHomeLocator? homeLocator = null)
    {
        _appPaths = appPaths;
        _tokenStore = tokenStore;
        _homeLocator = homeLocator ?? new CodexHomeLocator();
    }

    public async Task<OpenAiAggregateGatewayDecision> ResolveSelectionAsync(
        AppConfig config,
        CodexSelection requestedSelection,
        IDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var provider = config.Providers.SingleOrDefault(item => item.ProviderId == requestedSelection.ProviderId);
        if (provider is null || provider.Kind != ProviderKind.OpenAiOAuth || config.Settings.OpenAiAccountMode != OpenAiAccountMode.AggregateGateway)
        {
            return new OpenAiAggregateGatewayDecision
            {
                RequestedSelection = requestedSelection,
                ResolvedSelection = requestedSelection,
                Message = "\u5F53\u524D\u9009\u62E9\u672A\u542F\u7528 OpenAI \u805A\u5408\u7F51\u5173\u3002"
            };
        }

        var oauthProviderIds = config.Providers
            .Where(item => item.Kind == ProviderKind.OpenAiOAuth)
            .Select(item => item.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidateAccounts = new List<AccountRecord>();
        foreach (var account in config.Accounts.Where(item =>
                     oauthProviderIds.Contains(item.ProviderId) &&
                     item.Status != AccountStatus.Revoked &&
                     !string.IsNullOrWhiteSpace(item.CredentialRef)))
        {
            if (await _tokenStore.ReadTokensAsync(account.CredentialRef, cancellationToken) is not null)
            {
                candidateAccounts.Add(account);
            }
        }

        if (candidateAccounts.Count == 0)
        {
            throw new InvalidOperationException("\u805A\u5408\u7F51\u5173\u672A\u627E\u5230\u53EF\u7528\u7684 OpenAI OAuth \u8D26\u53F7\u3002");
        }

        var home = _homeLocator.Resolve(environment);
        var usageDashboard = await new UsageAttributionService(
                new UsageScanner(),
                new SwitchJournalStore(_appPaths.SwitchJournalPath))
            .BuildDashboardAsync(config, home, DateTimeOffset.Now, cancellationToken);

        var usageByAccount = usageDashboard.Accounts
            .ToDictionary(
                item => (item.ProviderId, item.AccountId),
                item => item,
                EqualityComparer<(string ProviderId, string AccountId)>.Default);

        var preferredAccount = candidateAccounts.FirstOrDefault(item =>
            string.Equals(item.ProviderId, requestedSelection.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.AccountId, requestedSelection.AccountId, StringComparison.OrdinalIgnoreCase));

        var resolvedAccount = candidateAccounts
            .OrderBy(item => OpenAiQuotaPolicy.RoutingStatusRank(item))
            .ThenBy(item => OpenAiQuotaPolicy.RoutingQuotaRank(item))
            .ThenBy(item => OpenAiQuotaPolicy.UsedPercentOrMax(item.FiveHourQuota))
            .ThenBy(item => OpenAiQuotaPolicy.UsedPercentOrMax(item.WeeklyQuota))
            .ThenBy(item => usageByAccount.TryGetValue((item.ProviderId, item.AccountId), out var usage) ? usage.Today.TotalTokens : 0)
            .ThenBy(item => usageByAccount.TryGetValue((item.ProviderId, item.AccountId), out var usage) ? usage.Last30Days.TotalTokens : 0)
            .ThenBy(item => item.LastUsedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => preferredAccount is not null &&
                                      string.Equals(item.ProviderId, preferredAccount.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                                      string.Equals(item.AccountId, preferredAccount.AccountId, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.ManualOrder <= 0 ? int.MaxValue : item.ManualOrder)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .First();

        var resolvedSelection = new CodexSelection
        {
            ProviderId = resolvedAccount.ProviderId,
            AccountId = resolvedAccount.AccountId,
            SelectedAt = DateTimeOffset.UtcNow
        };

        var quotaMode = OpenAiQuotaPolicy.HasAnyOfficialQuota(resolvedAccount)
            ? $"{OpenAiQuotaDisplayFormatter.FormatCompactRemaining(resolvedAccount.FiveHourQuota, "5h") ?? "5h \u6682\u4E0D\u53EF\u7528"}, {OpenAiQuotaDisplayFormatter.FormatCompactRemaining(resolvedAccount.WeeklyQuota, "\u5468") ?? "\u5468\u989D\u5EA6\u6682\u4E0D\u53EF\u7528"}"
            : "\u5B98\u65B9\u989D\u5EA6\u6682\u4E0D\u53EF\u7528\uFF0C\u5DF2\u56DE\u9000\u5230\u672C\u5730\u4F7F\u7528\u91CF";
        var message = usageByAccount.TryGetValue((resolvedAccount.ProviderId, resolvedAccount.AccountId), out var resolvedUsage)
            ? $"\u805A\u5408\u7F51\u5173\u5DF2\u5207\u6362\u5230 {resolvedAccount.Label}\uFF08{quotaMode}\uFF1B\u4ECA\u65E5 {resolvedUsage.Today.TotalTokens:n0}\uFF0C\u8FD1 30 \u5929 {resolvedUsage.Last30Days.TotalTokens:n0}\uFF09\u3002"
            : $"\u805A\u5408\u7F51\u5173\u5DF2\u5207\u6362\u5230 {resolvedAccount.Label}\uFF08{quotaMode}\uFF09\u3002";

        if (preferredAccount is not null &&
            string.Equals(preferredAccount.ProviderId, resolvedAccount.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(preferredAccount.AccountId, resolvedAccount.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            message = OpenAiQuotaPolicy.HasAnyOfficialQuota(resolvedAccount)
                ? $"\u805A\u5408\u7F51\u5173\u4FDD\u6301 {resolvedAccount.Label} \u4E3A\u5F53\u524D\u8D26\u53F7\uFF08{OpenAiQuotaDisplayFormatter.FormatCompactRemaining(resolvedAccount.FiveHourQuota, "5h") ?? "5h \u6682\u4E0D\u53EF\u7528"}, {OpenAiQuotaDisplayFormatter.FormatCompactRemaining(resolvedAccount.WeeklyQuota, "\u5468") ?? "\u5468\u989D\u5EA6\u6682\u4E0D\u53EF\u7528"}\uFF09\u3002"
                : $"\u805A\u5408\u7F51\u5173\u4FDD\u6301 {resolvedAccount.Label} \u4E3A\u5F53\u524D\u8D26\u53F7\uFF0C\u5E76\u4F7F\u7528\u672C\u5730\u4F7F\u7528\u91CF\u56DE\u9000\u3002";
        }

        return new OpenAiAggregateGatewayDecision
        {
            RequestedSelection = requestedSelection,
            ResolvedSelection = resolvedSelection,
            Message = message
        };
    }
}
