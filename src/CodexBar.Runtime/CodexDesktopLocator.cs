using System.Text.RegularExpressions;

namespace CodexBar.Runtime;

public sealed class CodexDesktopLocator
{
    private static readonly Regex PackagedCodexPattern = new(
        @"^OpenAI\.Codex_(?<version>\d+(?:\.\d+){3})_(?<architecture>[^_]+)__(?<publisher>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IReadOnlyList<string>? _windowsAppsRootsOverride;
    private readonly string _localAppData;
    private readonly string _programFiles;
    private readonly string _programFilesX86;
    private readonly string? _pathEnvironment;

    public CodexDesktopLocator(
        IEnumerable<string>? windowsAppsRoots = null,
        string? localAppData = null,
        string? programFiles = null,
        string? programFilesX86 = null,
        string? pathEnvironment = null)
    {
        _windowsAppsRootsOverride = windowsAppsRoots?.ToList();
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _programFiles = programFiles ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        _programFilesX86 = programFilesX86 ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        _pathEnvironment = pathEnvironment;
    }

    public string? Locate(string? configuredPath = null)
    {
        foreach (var candidate in EnumerateCandidates(configuredPath))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateCandidates(string? configuredPath)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedConfiguredPath = string.IsNullOrWhiteSpace(configuredPath) ? null : configuredPath.Trim();
        var packagedHint = TryParsePackagedHint(normalizedConfiguredPath);
        var preferCliHints = packagedHint is not null || string.IsNullOrWhiteSpace(normalizedConfiguredPath);

        if (!preferCliHints && normalizedConfiguredPath is not null && yielded.Add(normalizedConfiguredPath))
        {
            yield return normalizedConfiguredPath;
        }

        if (preferCliHints)
        {
            foreach (var candidate in EnumerateDesktopCandidatesFromCliHints())
            {
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        foreach (var candidate in EnumeratePackagedCandidates(packagedHint))
        {
            if (yielded.Add(candidate))
            {
                yield return candidate;
            }
        }

        if (preferCliHints)
        {
            foreach (var candidate in EnumerateDesktopCandidatesFromCliHints())
            {
                if (yielded.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        if (normalizedConfiguredPath is not null && yielded.Add(normalizedConfiguredPath))
        {
            yield return normalizedConfiguredPath;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(_localAppData, "Programs", "Codex", "Codex.exe"),
                     Path.Combine(_programFiles, "Codex", "Codex.exe"),
                     Path.Combine(_programFilesX86, "Codex", "Codex.exe")
                 })
        {
            if (yielded.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private IEnumerable<string> EnumerateDesktopCandidatesFromCliHints()
    {
        foreach (var cliCandidate in EnumerateCliCandidatePaths())
        {
            if (!File.Exists(cliCandidate))
            {
                continue;
            }

            foreach (var desktopCandidate in DeriveDesktopPathsFromCli(cliCandidate))
            {
                yield return desktopCandidate;
            }
        }
    }

    private IEnumerable<string> EnumeratePackagedCandidates(PackagedHint? packagedHint)
    {
        var relativeExecutable = packagedHint?.RelativeExecutablePath ?? Path.Combine("app", "Codex.exe");
        var roots = EnumerateWindowsAppsRoots(packagedHint?.WindowsAppsRoot);
        var packagedExecutables = roots
            .SelectMany(root => EnumeratePackageDirectories(root))
            .Select(packageDirectory => TryBuildPackagedCandidate(packageDirectory, relativeExecutable, packagedHint?.Publisher))
            .Where(candidate => candidate is not null)
            .Cast<PackagedExecutable>()
            .OrderByDescending(candidate => candidate.Version)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in packagedExecutables)
        {
            yield return candidate.Path;
        }
    }

    private IEnumerable<string> EnumerateCliCandidatePaths()
    {
        var pathEnvironment = _pathEnvironment ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(directory.Trim(), "codex.exe");
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(_localAppData, "Programs", "Codex", "codex.exe"),
                     Path.Combine(_localAppData, "OpenAI", "Codex", "codex.exe"),
                     Path.Combine(_programFiles, "Codex", "codex.exe"),
                     Path.Combine(_programFilesX86, "Codex", "codex.exe")
                 })
        {
            yield return candidate;
        }
    }

    private IEnumerable<string> EnumerateWindowsAppsRoots(string? preferredRoot)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(preferredRoot) && yielded.Add(preferredRoot))
        {
            yield return preferredRoot;
        }

        if (_windowsAppsRootsOverride is not null)
        {
            foreach (var root in _windowsAppsRootsOverride.Where(root => !string.IsNullOrWhiteSpace(root)))
            {
                if (yielded.Add(root))
                {
                    yield return root;
                }
            }

            yield break;
        }

        foreach (var root in new[]
                 {
                     Path.Combine(_programFiles, "WindowsApps")
                 })
        {
            if (!string.IsNullOrWhiteSpace(root) && yielded.Add(root))
            {
                yield return root;
            }
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            {
                continue;
            }

            var root = Path.Combine(drive.RootDirectory.FullName, "WindowsApps");
            if (yielded.Add(root))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<string> EnumeratePackageDirectories(string windowsAppsRoot)
    {
        if (string.IsNullOrWhiteSpace(windowsAppsRoot) || !Directory.Exists(windowsAppsRoot))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(windowsAppsRoot, "OpenAI.Codex_*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    private static PackagedExecutable? TryBuildPackagedCandidate(
        string packageDirectory,
        string relativeExecutablePath,
        string? requiredPublisher)
    {
        var packageName = Path.GetFileName(packageDirectory);
        if (!TryParsePackageDirectoryName(packageName, out var version, out var publisher))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(requiredPublisher) &&
            !string.Equals(requiredPublisher, publisher, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new PackagedExecutable(version, Path.Combine(packageDirectory, relativeExecutablePath));
    }

    private static PackagedHint? TryParsePackagedHint(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var normalizedPath = configuredPath.Trim();
        var packageDirectory = FindPackagedDirectory(normalizedPath);
        if (packageDirectory is null)
        {
            return null;
        }

        var packageName = Path.GetFileName(packageDirectory);
        if (!TryParsePackageDirectoryName(packageName, out _, out var publisher))
        {
            return null;
        }

        var windowsAppsRoot = Path.GetDirectoryName(packageDirectory);
        if (string.IsNullOrWhiteSpace(windowsAppsRoot))
        {
            return null;
        }

        var relativeExecutablePath = Path.GetRelativePath(packageDirectory, normalizedPath);
        if (relativeExecutablePath.StartsWith("..", StringComparison.Ordinal))
        {
            relativeExecutablePath = Path.Combine("app", "Codex.exe");
        }

        return new PackagedHint(windowsAppsRoot, relativeExecutablePath, publisher);
    }

    private static string? FindPackagedDirectory(string path)
    {
        var directory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (TryParsePackageDirectoryName(Path.GetFileName(directory), out _, out _))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static bool TryParsePackageDirectoryName(string packageName, out Version version, out string publisher)
    {
        version = new Version();
        publisher = "";
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return false;
        }

        var match = PackagedCodexPattern.Match(packageName);
        if (!match.Success)
        {
            return false;
        }

        if (!Version.TryParse(match.Groups["version"].Value, out var parsedVersion))
        {
            return false;
        }

        version = parsedVersion;
        publisher = match.Groups["publisher"].Value;
        return !string.IsNullOrWhiteSpace(publisher);
    }

    private static IEnumerable<string> DeriveDesktopPathsFromCli(string cliPath)
    {
        var cliDirectory = Path.GetDirectoryName(cliPath);
        if (string.IsNullOrWhiteSpace(cliDirectory))
        {
            yield break;
        }

        var parentDirectory = Directory.GetParent(cliDirectory);
        if (parentDirectory is null)
        {
            yield break;
        }

        yield return Path.Combine(parentDirectory.FullName, "Codex.exe");

        var grandParentDirectory = parentDirectory.Parent;
        if (grandParentDirectory is null)
        {
            yield break;
        }

        yield return Path.Combine(grandParentDirectory.FullName, "Codex.exe");
    }

    private sealed record PackagedHint(string WindowsAppsRoot, string RelativeExecutablePath, string Publisher);

    private sealed record PackagedExecutable(Version Version, string Path);
}
