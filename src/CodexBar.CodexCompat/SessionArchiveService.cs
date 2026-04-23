using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexBar.Core;

namespace CodexBar.CodexCompat;

public sealed record SessionArchiveExportOptions(bool IncludeArchived = true);

public sealed record SessionArchiveExportResult(
    string ArchivePath,
    int SessionsExported,
    int ArchivedSessionsExported,
    bool SessionIndexExported,
    int FilesSkipped);

public sealed record SessionArchiveFileStats(int Copied, int Skipped, int Renamed)
{
    public static SessionArchiveFileStats Empty => new(0, 0, 0);
}

public sealed record SessionArchiveIndexStats(int Merged, int Skipped)
{
    public static SessionArchiveIndexStats Empty => new(0, 0);
}

public sealed record SessionArchiveImportResult(
    SessionArchiveFileStats Sessions,
    SessionArchiveFileStats ArchivedSessions,
    SessionArchiveIndexStats SessionIndex,
    string? SessionIndexBackupPath)
{
    public int FilesCopied => Sessions.Copied + ArchivedSessions.Copied;
    public int FilesSkipped => Sessions.Skipped + ArchivedSessions.Skipped;
    public int FilesRenamed => Sessions.Renamed + ArchivedSessions.Renamed;
}

public sealed class SessionArchiveService
{
    private const string ManifestEntryName = "manifest.json";
    private const string SessionIndexEntryName = "session_index.jsonl";
    private const string HistoryLockFileName = "codex-history.lock";
    private readonly AppPaths _appPaths;

    public SessionArchiveService(AppPaths appPaths)
    {
        _appPaths = appPaths;
    }

    public async Task<SessionArchiveExportResult> ExportAsync(
        CodexHomeState home,
        string archivePath,
        SessionArchiveExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SessionArchiveExportOptions();
        var sessions = EnumerateSessionFiles(home.SessionsPath).ToList();
        var archivedSessions = options.IncludeArchived
            ? EnumerateSessionFiles(home.ArchivedSessionsPath).ToList()
            : [];
        var sessionIndexPath = GetSessionIndexPath(home);
        var sessionIndexExists = File.Exists(sessionIndexPath);

        EnsureParentDirectory(archivePath);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        var filesSkipped = 0;
        var sessionIndexExported = false;
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);

        foreach (var sessionPath in sessions)
        {
            var entryName = ToArchivePath("sessions", Path.GetRelativePath(home.SessionsPath, sessionPath));
            if (!await TryWriteFileEntryAsync(archive, entryName, sessionPath, cancellationToken))
            {
                filesSkipped++;
            }
        }

        foreach (var sessionPath in archivedSessions)
        {
            var entryName = ToArchivePath("archived_sessions", Path.GetRelativePath(home.ArchivedSessionsPath, sessionPath));
            if (!await TryWriteFileEntryAsync(archive, entryName, sessionPath, cancellationToken))
            {
                filesSkipped++;
            }
        }

        if (sessionIndexExists)
        {
            if (await TryWriteFileEntryAsync(archive, SessionIndexEntryName, sessionIndexPath, cancellationToken))
            {
                sessionIndexExported = true;
            }
            else
            {
                filesSkipped++;
            }
        }

        await WriteManifestAsync(archive, sessions.Count, archivedSessions.Count, sessionIndexExported, options, cancellationToken);

        return new SessionArchiveExportResult(
            archivePath,
            sessions.Count,
            archivedSessions.Count,
            sessionIndexExported,
            filesSkipped);
    }

    public async Task<SessionArchiveImportResult> ImportAsync(
        CodexHomeState home,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("History archive not found.", archivePath);
        }

        Directory.CreateDirectory(home.RootPath);
        _appPaths.EnsureDirectories();
        Directory.CreateDirectory(_appPaths.LocksDirectory);

        var lockPath = Path.Combine(_appPaths.LocksDirectory, HistoryLockFileName);
        await using var lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        using var archive = ZipFile.OpenRead(archivePath);
        ValidateArchive(archive);

        var sessionIndexBackupPath = BackupSessionIndex(home);
        var sessionStats = SessionArchiveFileStats.Empty;
        var archivedStats = SessionArchiveFileStats.Empty;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = NormalizeEntryName(entry.FullName);
            if (name.StartsWith("sessions/", StringComparison.Ordinal))
            {
                sessionStats = Add(sessionStats, await ImportFileEntryAsync(entry, home.SessionsPath, name["sessions/".Length..], cancellationToken));
            }
            else if (name.StartsWith("archived_sessions/", StringComparison.Ordinal))
            {
                archivedStats = Add(archivedStats, await ImportFileEntryAsync(entry, home.ArchivedSessionsPath, name["archived_sessions/".Length..], cancellationToken));
            }
        }

        var indexStats = await MergeSessionIndexAsync(archive, GetSessionIndexPath(home), cancellationToken);
        return new SessionArchiveImportResult(sessionStats, archivedStats, indexStats, sessionIndexBackupPath);
    }

    public static string FormatImportSummary(SessionArchiveImportResult result)
        => $"sessions copied={result.Sessions.Copied}, skipped={result.Sessions.Skipped}, renamed={result.Sessions.Renamed}; " +
           $"archived copied={result.ArchivedSessions.Copied}, skipped={result.ArchivedSessions.Skipped}, renamed={result.ArchivedSessions.Renamed}; " +
           $"session_index merged={result.SessionIndex.Merged}, skipped={result.SessionIndex.Skipped}";

    private static IEnumerable<string> EnumerateSessionFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
        {
            yield return path;
        }
    }

    private static async Task WriteManifestAsync(
        ZipArchive archive,
        int sessions,
        int archivedSessions,
        bool sessionIndex,
        SessionArchiveExportOptions options,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, new Dictionary<string, object?>
        {
            ["format"] = "codexbar-history",
            ["schema_version"] = 1,
            ["created_at"] = DateTimeOffset.UtcNow,
            ["include_archived"] = options.IncludeArchived,
            ["sessions"] = sessions,
            ["archived_sessions"] = archivedSessions,
            ["session_index"] = sessionIndex
        }, cancellationToken: cancellationToken);
    }

    private static async Task<bool> TryWriteFileEntryAsync(
        ZipArchive archive,
        string entryName,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        FileStream source;
        try
        {
            source = OpenSharedRead(sourcePath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        using (source)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            TrySetEntryTimestamp(entry, sourcePath);
            await using var destination = entry.Open();
            await source.CopyToAsync(destination, cancellationToken);
        }

        return true;
    }

    private static async Task<SessionArchiveFileStats> ImportFileEntryAsync(
        ZipArchiveEntry entry,
        string destinationRoot,
        string relativeArchivePath,
        CancellationToken cancellationToken)
    {
        var destinationPath = GetSafeDestinationPath(destinationRoot, relativeArchivePath);
        EnsureParentDirectory(destinationPath);
        var tempPath = Path.Combine(Path.GetTempPath(), $"codexbar-history-{Guid.NewGuid():N}.jsonl");

        try
        {
            await using (var input = entry.Open())
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            if (!File.Exists(destinationPath))
            {
                File.Move(tempPath, destinationPath);
                TrySetLastWriteTime(destinationPath, entry.LastWriteTime);
                return new SessionArchiveFileStats(1, 0, 0);
            }

            if (FilesMatch(tempPath, destinationPath))
            {
                File.Delete(tempPath);
                return new SessionArchiveFileStats(0, 1, 0);
            }

            var importedPath = NextImportedPath(destinationPath);
            while (File.Exists(importedPath))
            {
                if (FilesMatch(tempPath, importedPath))
                {
                    File.Delete(tempPath);
                    return new SessionArchiveFileStats(0, 1, 0);
                }

                importedPath = NextImportedPath(destinationPath, importedPath);
            }

            File.Move(tempPath, importedPath);
            TrySetLastWriteTime(importedPath, entry.LastWriteTime);
            return new SessionArchiveFileStats(0, 0, 1);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static async Task<SessionArchiveIndexStats> MergeSessionIndexAsync(
        ZipArchive archive,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(SessionIndexEntryName);
        if (entry is null)
        {
            return SessionArchiveIndexStats.Empty;
        }

        EnsureParentDirectory(destinationPath);
        if (!File.Exists(destinationPath))
        {
            await File.WriteAllTextAsync(destinationPath, "", Encoding.UTF8, cancellationToken);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in await File.ReadAllLinesAsync(destinationPath, Encoding.UTF8, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                seen.Add(SessionIndexKey(line));
            }
        }

        var merged = 0;
        var skipped = 0;
        var needsLeadingNewLine = NeedsTrailingNewLine(destinationPath);
        await using var input = entry.Open();
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        await using var output = new FileStream(destinationPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (needsLeadingNewLine)
        {
            await writer.WriteLineAsync();
        }

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var key = SessionIndexKey(line);
            if (!seen.Add(key))
            {
                skipped++;
                continue;
            }

            await writer.WriteLineAsync(line);
            merged++;
        }

        return new SessionArchiveIndexStats(merged, skipped);
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = NormalizeEntryName(entry.FullName);
            if (!seen.Add(name))
            {
                throw new InvalidDataException($"Duplicate archive entry is not allowed: {entry.FullName}");
            }

            if (!IsAllowedEntryName(name))
            {
                throw new InvalidDataException($"Unsupported history archive entry: {entry.FullName}");
            }
        }
    }

    private static bool IsAllowedEntryName(string name)
        => string.Equals(name, ManifestEntryName, StringComparison.Ordinal)
           || string.Equals(name, SessionIndexEntryName, StringComparison.Ordinal)
           || IsSessionEntry(name, "sessions")
           || IsSessionEntry(name, "archived_sessions");

    private static bool IsSessionEntry(string name, string root)
        => name.StartsWith(root + "/", StringComparison.Ordinal)
           && name.Length > root.Length + 1
           && name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Contains('\\', StringComparison.Ordinal)
            || name.StartsWith("/", StringComparison.Ordinal)
            || name.Contains(':', StringComparison.Ordinal)
            || Path.IsPathRooted(name))
        {
            throw new InvalidDataException($"Unsafe history archive entry path: {name}");
        }

        var segments = name.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                                    || string.Equals(segment, ".", StringComparison.Ordinal)
                                    || string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"Unsafe history archive entry path: {name}");
        }

        return name;
    }

    private static string GetSafeDestinationPath(string destinationRoot, string relativeArchivePath)
    {
        var root = Path.GetFullPath(destinationRoot);
        var destination = Path.GetFullPath(Path.Combine(root, relativeArchivePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsafe history archive entry destination: {relativeArchivePath}");
        }

        return destination;
    }

    private string? BackupSessionIndex(CodexHomeState home)
    {
        var source = GetSessionIndexPath(home);
        if (!File.Exists(source))
        {
            return null;
        }

        var backupDirectory = Path.Combine(_appPaths.AppRoot, "backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"session_index-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        File.Copy(source, backupPath, overwrite: false);
        return backupPath;
    }

    private static string SessionIndexKey(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var id = FirstString(document.RootElement, "id", "session_id", "thread_id");
                var timestamp = FirstString(document.RootElement, "updated_at", "created_at") ?? "";
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return $"{id}::{timestamp}";
                }
            }
        }
        catch (JsonException)
        {
        }

        return $"raw::{raw}";
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                return value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : value.ToString();
            }
        }

        return null;
    }

    private static string ToArchivePath(string root, string relativePath)
        => root + "/" + relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string GetSessionIndexPath(CodexHomeState home)
        => Path.Combine(home.RootPath, SessionIndexEntryName);

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static FileStream OpenSharedRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);

    private static bool FilesMatch(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (!leftInfo.Exists || !rightInfo.Exists || leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using var leftStream = OpenSharedRead(left);
        using var rightStream = OpenSharedRead(right);
        return SHA256.HashData(leftStream).SequenceEqual(SHA256.HashData(rightStream));
    }

    private static bool NeedsTrailingNewLine(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0)
        {
            return false;
        }

        try
        {
            using var stream = OpenSharedRead(path);
            stream.Seek(-1, SeekOrigin.End);
            var last = stream.ReadByte();
            return last is not '\n' and not '\r';
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string NextImportedPath(string destinationPath)
        => Path.Combine(
            Path.GetDirectoryName(destinationPath)!,
            $"{Path.GetFileNameWithoutExtension(destinationPath)}.imported-1{Path.GetExtension(destinationPath)}");

    private static string NextImportedPath(string destinationPath, string currentImportedPath)
    {
        var stem = Path.GetFileNameWithoutExtension(destinationPath);
        var suffix = Path.GetExtension(destinationPath);
        var currentName = Path.GetFileNameWithoutExtension(currentImportedPath);
        var marker = $"{stem}.imported-";
        var current = currentName.StartsWith(marker, StringComparison.Ordinal)
                      && int.TryParse(currentName[marker.Length..], out var parsed)
            ? parsed
            : 1;
        return Path.Combine(Path.GetDirectoryName(destinationPath)!, $"{stem}.imported-{current + 1}{suffix}");
    }

    private static SessionArchiveFileStats Add(SessionArchiveFileStats left, SessionArchiveFileStats right)
        => new(left.Copied + right.Copied, left.Skipped + right.Skipped, left.Renamed + right.Renamed);

    private static void TrySetEntryTimestamp(ZipArchiveEntry entry, string path)
    {
        try
        {
            entry.LastWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TrySetLastWriteTime(string path, DateTimeOffset lastWriteTime)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, lastWriteTime.UtcDateTime);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
