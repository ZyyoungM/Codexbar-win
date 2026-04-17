namespace CodexBar.Runtime;

public sealed class CodexDesktopLocator
{
    public string? Locate(string? configuredPath = null)
    {
        foreach (var candidate in EnumerateCandidates(configuredPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        yield return Path.Combine(localAppData, "Programs", "Codex", "Codex.exe");
        yield return Path.Combine(programFiles, "Codex", "Codex.exe");
        yield return Path.Combine(programFilesX86, "Codex", "Codex.exe");
    }
}
