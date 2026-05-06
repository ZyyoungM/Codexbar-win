namespace CodexBar.Auth;

public static class OpenAiWorkspaceLabelFormatter
{
    private static readonly string[] Separators = [" · ", " 路 ", " - ", " / "];

    public static string Build(
        OAuthIdentity identity,
        OpenAiWorkspaceDescriptor workspace,
        string? fallback)
    {
        var owner = identity.BestDisplayName(string.IsNullOrWhiteSpace(fallback) ? "OpenAI" : fallback.Trim());
        return string.IsNullOrWhiteSpace(workspace.WorkspaceName) ||
               string.Equals(workspace.WorkspaceName, "Current workspace", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(owner.Trim(), workspace.WorkspaceName.Trim(), StringComparison.OrdinalIgnoreCase)
            ? owner
            : $"{owner} · {workspace.WorkspaceName}";
    }

    public static bool ShouldGenerate(string? label, OAuthIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(label) ||
            string.Equals(label.Trim(), "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = label.Trim();
        foreach (var owner in CandidateOwners(identity))
        {
            if (string.Equals(trimmed, owner, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Separators.Any(separator =>
                    trimmed.StartsWith(owner + separator, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> CandidateOwners(OAuthIdentity identity)
        => new[] { identity.Email, identity.Name, identity.SubjectId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
