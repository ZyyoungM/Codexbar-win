using System.Diagnostics;

namespace CodexBar.Runtime;

public sealed record CodexExecutable(string Path, string? Version);

public sealed class CodexCliLocator
{
    public async Task<CodexExecutable?> LocateAsync(string? configuredPath = null, CancellationToken cancellationToken = default)
    {
        foreach (var candidate in EnumerateCandidates(configuredPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var version = await TryGetVersionAsync(candidate, cancellationToken);
            if (version is not null)
            {
                return new CodexExecutable(candidate, version);
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

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(directory.Trim(), "codex.exe");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        yield return Path.Combine(localAppData, "Programs", "Codex", "codex.exe");
        yield return Path.Combine(localAppData, "OpenAI", "Codex", "codex.exe");
        yield return Path.Combine(programFiles, "Codex", "codex.exe");
        yield return Path.Combine(programFilesX86, "Codex", "codex.exe");
    }

    private static async Task<string?> TryGetVersionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token);
            var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}

