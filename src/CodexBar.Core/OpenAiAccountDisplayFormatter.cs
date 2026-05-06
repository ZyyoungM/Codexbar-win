namespace CodexBar.Core;

public static class OpenAiAccountDisplayFormatter
{
    public static string? FormatTier(AccountRecord account)
    {
        var raw = FormatRawPlan(account.OfficialPlanTypeRaw);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        if (account.Tier != AccountTier.Unknown)
        {
            return account.Tier == AccountTier.Team
                ? "team"
                : account.Tier.ToString().ToLowerInvariant();
        }

        return FormatRawPlan(account.WorkspaceType);
    }

    public static string? EffectiveWorkspaceName(AccountRecord account)
    {
        if (IsTeamLike(account) &&
            (string.IsNullOrWhiteSpace(account.WorkspaceName) ||
             string.Equals(account.WorkspaceName, "Personal", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(account.WorkspaceName, "Current workspace", StringComparison.OrdinalIgnoreCase)))
        {
            return "Team";
        }

        return string.IsNullOrWhiteSpace(account.WorkspaceName) ? null : account.WorkspaceName.Trim();
    }

    public static AccountTier MapPlanToTier(string? rawPlanType)
    {
        var normalized = NormalizePlanType(rawPlanType);
        return normalized switch
        {
            "free" => AccountTier.Free,
            "go" => AccountTier.Go,
            "plus" => AccountTier.Plus,
            "pro" => AccountTier.Pro,
            "team" or "business" or "workspace" or "enterprise" or "edu" => AccountTier.Team,
            _ => AccountTier.Unknown
        };
    }

    public static bool IsTeamLike(AccountRecord account)
        => MapPlanToTier(account.OfficialPlanTypeRaw) == AccountTier.Team ||
           account.Tier == AccountTier.Team ||
           MapPlanToTier(account.WorkspaceType) == AccountTier.Team;

    public static string NormalizePlanType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        return normalized.StartsWith("chatgpt", StringComparison.Ordinal)
            ? normalized["chatgpt".Length..]
            : normalized;
    }

    private static string? FormatRawPlan(string? value)
    {
        var normalized = NormalizePlanType(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Contains("pro", StringComparison.Ordinal) &&
            normalized.Contains("10", StringComparison.Ordinal))
        {
            return "pro 10x";
        }

        if (normalized.Contains("pro", StringComparison.Ordinal) &&
            normalized.Contains("5", StringComparison.Ordinal))
        {
            return "pro 5x";
        }

        if (normalized.Contains("team", StringComparison.Ordinal) ||
            normalized.Contains("business", StringComparison.Ordinal) ||
            normalized.Contains("workspace", StringComparison.Ordinal) ||
            normalized.Contains("enterprise", StringComparison.Ordinal) ||
            normalized.Contains("edu", StringComparison.Ordinal))
        {
            return "team";
        }

        if (normalized.Contains("plus", StringComparison.Ordinal))
        {
            return "plus";
        }

        if (normalized.Contains("go", StringComparison.Ordinal))
        {
            return "go";
        }

        if (normalized.Contains("free", StringComparison.Ordinal))
        {
            return "free";
        }

        return normalized.Contains("pro", StringComparison.Ordinal) ? "pro" : null;
    }
}
