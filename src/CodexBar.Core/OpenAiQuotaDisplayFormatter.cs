namespace CodexBar.Core;

public static class OpenAiQuotaDisplayFormatter
{
    public static string FormatQuotaLabel(
        QuotaUsageSnapshot snapshot,
        string displayLabel,
        DateTimeOffset? now = null)
    {
        var refreshAt = FormatQuotaRefreshAt(snapshot, displayLabel, now);
        return string.IsNullOrWhiteSpace(refreshAt)
            ? displayLabel
            : $"{displayLabel} 刷新于 {refreshAt}";
    }

    public static string? FormatCompactRemaining(
        QuotaUsageSnapshot snapshot,
        string displayLabel,
        DateTimeOffset? now = null)
    {
        if (!snapshot.HasValue)
        {
            return null;
        }

        var remaining = FormatRemainingValue(snapshot);
        var nextReset = FormatNextReset(snapshot, displayLabel, now);
        return string.IsNullOrWhiteSpace(nextReset)
            ? $"{displayLabel} \u5269\u4F59 {remaining}"
            : $"{displayLabel} \u5269\u4F59 {remaining} \u00B7 \u4E0B\u6B21 {nextReset}";
    }

    public static string FormatDetailedRemaining(
        QuotaUsageSnapshot snapshot,
        string displayLabel,
        DateTimeOffset? now = null)
    {
        if (!snapshot.HasValue)
        {
            return "\u5C1A\u672A\u83B7\u53D6";
        }

        var remaining = FormatRemainingValue(snapshot);
        var nextReset = FormatNextReset(snapshot, displayLabel, now);
        return string.IsNullOrWhiteSpace(nextReset)
            ? $"{displayLabel} \u5269\u4F59\u989D\u5EA6\uFF1A{remaining}"
            : $"{displayLabel} \u5269\u4F59\u989D\u5EA6\uFF1A{remaining} | \u4E0B\u6B21\u5237\u65B0\uFF1A{nextReset}";
    }

    public static string FormatRemainingValue(QuotaUsageSnapshot snapshot)
    {
        if (snapshot.Limit.HasValue && snapshot.Limit.Value > 0)
        {
            var remaining = Math.Max(0, snapshot.Limit.Value - (snapshot.Used ?? 0));
            return snapshot.Limit.Value == 100
                ? $"{remaining}%"
                : $"{remaining}/{snapshot.Limit.Value}";
        }

        if (snapshot.Used.HasValue)
        {
            return "\u672A\u77E5";
        }

        return snapshot.Limit.HasValue ? $"{snapshot.Limit.Value}" : "\u672A\u77E5";
    }

    public static string? FormatQuotaRefreshAt(
        QuotaUsageSnapshot snapshot,
        string displayLabel,
        DateTimeOffset? now = null)
    {
        if (!snapshot.ResetAt.HasValue)
        {
            return null;
        }

        var localReset = snapshot.ResetAt.Value.ToLocalTime();
        var localNow = (now ?? DateTimeOffset.Now).ToLocalTime();
        var normalizedLabel = NormalizeLabel(displayLabel);

        if (normalizedLabel.StartsWith("5h", StringComparison.Ordinal))
        {
            return localReset.ToString("HH:mm");
        }

        if (normalizedLabel.Contains("week", StringComparison.Ordinal)
            || normalizedLabel.Contains("weekly", StringComparison.Ordinal)
            || normalizedLabel.Contains("周", StringComparison.Ordinal))
        {
            var delta = localReset - localNow;
            return delta >= TimeSpan.Zero && delta < TimeSpan.FromHours(24)
                ? localReset.ToString("HH:mm")
                : localReset.ToString("MM-dd");
        }

        return localReset.ToString("yyyy-MM-dd HH:mm");
    }

    public static string? FormatNextReset(
        QuotaUsageSnapshot snapshot,
        string displayLabel,
        DateTimeOffset? now = null)
    {
        if (!snapshot.ResetAt.HasValue)
        {
            return null;
        }

        var localReset = snapshot.ResetAt.Value.ToLocalTime();
        var localNow = (now ?? DateTimeOffset.Now).ToLocalTime();
        var normalizedLabel = displayLabel.Trim().ToLowerInvariant();

        if (normalizedLabel == "5h")
        {
            return localReset.ToString("HH:mm");
        }

        if (normalizedLabel is "week" or "weekly" or "\u5468" or "\u672C\u5468")
        {
            var delta = localReset - localNow;
            return delta >= TimeSpan.Zero && delta < TimeSpan.FromHours(24)
                ? localReset.ToString("HH:mm")
                : localReset.ToString("yyyy-MM-dd");
        }

        return localReset.ToString("yyyy-MM-dd HH:mm");
    }

    private static string NormalizeLabel(string value)
        => string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToLowerInvariant();
}
