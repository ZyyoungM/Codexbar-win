namespace CodexBar.Core;

public sealed record AppPaths
{
    public required string AppRoot { get; init; }
    public required string ConfigPath { get; init; }
    public required string LogsDirectory { get; init; }
    public required string SwitchJournalPath { get; init; }
    public required string LocksDirectory { get; init; }
    public required string CacheDirectory { get; init; }

    public static AppPaths Resolve(IDictionary<string, string?>? environment = null)
    {
        environment ??= SnapshotEnvironment();

        var userProfile = GetEnv(environment, "USERPROFILE")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appRoot = Path.Combine(userProfile, ".codexbar");

        return new AppPaths
        {
            AppRoot = appRoot,
            ConfigPath = Path.Combine(appRoot, "config.json"),
            LogsDirectory = Path.Combine(appRoot, "logs"),
            SwitchJournalPath = Path.Combine(appRoot, "switch-journal.jsonl"),
            LocksDirectory = Path.Combine(appRoot, "locks"),
            CacheDirectory = Path.Combine(appRoot, "cache")
        };
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(AppRoot);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(LocksDirectory);
        Directory.CreateDirectory(CacheDirectory);
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
