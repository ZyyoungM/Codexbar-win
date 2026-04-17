using CodexBar.Core;

namespace CodexBar.CodexCompat;

public sealed class CodexHomeLocator
{
    public CodexHomeState Resolve(IDictionary<string, string?>? environment = null)
    {
        environment ??= SnapshotEnvironment();

        var explicitHome = GetEnv(environment, "CODEX_HOME");
        var root = explicitHome;
        var overridden = !string.IsNullOrWhiteSpace(root);

        if (string.IsNullOrWhiteSpace(root))
        {
            var userProfile = GetEnv(environment, "USERPROFILE")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = Path.Combine(userProfile, ".codex");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root!));
        return new CodexHomeState
        {
            RootPath = root,
            ConfigPath = Path.Combine(root, "config.toml"),
            AuthPath = Path.Combine(root, "auth.json"),
            SessionsPath = Path.Combine(root, "sessions"),
            ArchivedSessionsPath = Path.Combine(root, "archived_sessions"),
            IsExplicitlyOverridden = overridden
        };
    }

    private static string? GetEnv(IDictionary<string, string?> environment, string name)
        => environment.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static Dictionary<string, string?> SnapshotEnvironment()
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            snapshot[(string)entry.Key] = entry.Value?.ToString();
        }

        return snapshot;
    }
}
