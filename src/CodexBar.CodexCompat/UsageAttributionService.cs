using CodexBar.Core;

namespace CodexBar.CodexCompat;

public sealed class UsageAttributionService
{
    private readonly UsageScanner _usageScanner;
    private readonly SwitchJournalStore _switchJournalStore;

    public UsageAttributionService(UsageScanner usageScanner, SwitchJournalStore switchJournalStore)
    {
        _usageScanner = usageScanner;
        _switchJournalStore = switchJournalStore;
    }

    public async Task<UsageDashboard> BuildDashboardAsync(AppConfig config, CodexHomeState home, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var sessions = await _usageScanner.ScanSessionsAsync(home, cancellationToken);
        var todayStart = new DateTimeOffset(now.LocalDateTime.Date, TimeZoneInfo.Local.GetUtcOffset(now.LocalDateTime.Date));
        var last7Start = now.AddDays(-7);
        var last30Start = now.AddDays(-30);
        var lifetimeStart = sessions.Count == 0 ? now : sessions.Min(session => session.StartedAt);

        var today = UsageScanner.Summarize(sessions, todayStart, now);
        var last7 = UsageScanner.Summarize(sessions, last7Start, now);
        var last30 = UsageScanner.Summarize(sessions, last30Start, now);
        var lifetime = UsageScanner.Summarize(sessions, lifetimeStart, now);

        var journalEntries = await ReadAttributionEntriesAsync(config, cancellationToken);
        var accountSessions = new Dictionary<(string ProviderId, string AccountId), List<SessionUsageRecord>>();
        var unattributed = 0;

        foreach (var session in sessions)
        {
            var selection = FindSelectionForSession(journalEntries, session.StartedAt);
            if (selection is null)
            {
                unattributed++;
                continue;
            }

            var key = (selection.ProviderId, selection.AccountId);
            if (!accountSessions.TryGetValue(key, out var list))
            {
                list = [];
                accountSessions[key] = list;
            }

            list.Add(session);
        }

        var accounts = config.Accounts
            .Select(account =>
            {
                var key = (account.ProviderId, account.AccountId);
                accountSessions.TryGetValue(key, out var sessionsForAccount);
                sessionsForAccount ??= [];
                return new AccountUsageSummary
                {
                    ProviderId = account.ProviderId,
                    AccountId = account.AccountId,
                    Today = UsageScanner.Summarize(sessionsForAccount, todayStart, now),
                    Last7Days = UsageScanner.Summarize(sessionsForAccount, last7Start, now),
                    Last30Days = UsageScanner.Summarize(sessionsForAccount, last30Start, now),
                    Lifetime = UsageScanner.Summarize(sessionsForAccount, lifetimeStart, now)
                };
            })
            .ToList();

        return new UsageDashboard
        {
            Today = today,
            Last7Days = last7,
            Last30Days = last30,
            Lifetime = lifetime,
            Accounts = accounts,
            UnattributedSessions = unattributed
        };
    }

    private async Task<IReadOnlyList<SwitchJournalEntry>> ReadAttributionEntriesAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var entries = (await _switchJournalStore.ReadAllAsync(cancellationToken))
            .Where(entry => string.Equals(entry.Status, "ok", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (config.ActiveSelection is not null && entries.All(entry =>
                entry.Timestamp != config.ActiveSelection.SelectedAt ||
                entry.Selection.ProviderId != config.ActiveSelection.ProviderId ||
                entry.Selection.AccountId != config.ActiveSelection.AccountId))
        {
            entries.Add(new SwitchJournalEntry
            {
                Timestamp = config.ActiveSelection.SelectedAt,
                Selection = config.ActiveSelection,
                Status = "ok",
                Message = "active selection snapshot"
            });
        }

        return entries
            .OrderBy(entry => entry.Timestamp)
            .ToList();
    }

    private static CodexSelection? FindSelectionForSession(IReadOnlyList<SwitchJournalEntry> entries, DateTimeOffset timestamp)
    {
        SwitchJournalEntry? winner = null;
        foreach (var entry in entries)
        {
            if (entry.Timestamp > timestamp)
            {
                break;
            }

            winner = entry;
        }

        return winner?.Selection;
    }
}
