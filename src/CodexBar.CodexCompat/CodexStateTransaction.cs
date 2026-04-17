using System.Text;
using CodexBar.Core;

namespace CodexBar.CodexCompat;

public sealed class CodexStateTransaction
{
    private readonly AppPaths _appPaths;

    public CodexStateTransaction(AppPaths appPaths)
    {
        _appPaths = appPaths;
    }

    public async Task<CodexSwitchResult> WriteActivationAsync(
        CodexHomeState home,
        CodexSelection selection,
        string configContent,
        string authContent,
        Func<ValidationReport> validate,
        CancellationToken cancellationToken = default)
    {
        _appPaths.EnsureDirectories();
        Directory.CreateDirectory(home.RootPath);

        var lockPath = Path.Combine(_appPaths.LocksDirectory, "codex-state.lock");
        await using var lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var snapshots = new[]
        {
            SnapshotTarget.Capture(home.AuthPath),
            SnapshotTarget.Capture(home.ConfigPath)
        };
        var written = new List<string>();

        try
        {
            EnsureUnchanged(snapshots[0]);
            await AtomicReplaceAsync(home.AuthPath, authContent, cancellationToken);
            written.Add(home.AuthPath);

            EnsureUnchanged(snapshots[1]);
            await AtomicReplaceAsync(home.ConfigPath, configContent, cancellationToken);
            written.Add(home.ConfigPath);

            var report = validate();
            if (!report.IsValid)
            {
                throw new InvalidOperationException("Post-write validation failed: " + string.Join("; ", report.Errors));
            }

            return new CodexSwitchResult
            {
                Selection = selection,
                WrittenFiles = written,
                RollbackApplied = false,
                ValidationPassed = true,
                Message = "Activation written."
            };
        }
        catch (Exception ex)
        {
            Rollback(snapshots.Reverse());
            return new CodexSwitchResult
            {
                Selection = selection,
                WrittenFiles = written,
                RollbackApplied = true,
                ValidationPassed = false,
                Message = ex.Message
            };
        }
    }

    private static void EnsureUnchanged(SnapshotTarget snapshot)
    {
        var current = FileHash.Sha256OrNull(snapshot.Path);
        if (!string.Equals(current, snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Refusing to write {snapshot.Path}; it changed during activation.");
        }
    }

    private static async Task AtomicReplaceAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        var backup = $"{path}.bak-codexbar-last";
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);

        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(path))
        {
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }

            File.Replace(temp, path, backup, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }

    private static void Rollback(IEnumerable<SnapshotTarget> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Bytes is null)
            {
                if (File.Exists(snapshot.Path))
                {
                    File.Delete(snapshot.Path);
                }
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(snapshot.Path)!);
            File.WriteAllBytes(snapshot.Path, snapshot.Bytes);
        }
    }

    private sealed record SnapshotTarget(string Path, byte[]? Bytes, string? Sha256)
    {
        public static SnapshotTarget Capture(string path)
        {
            if (!File.Exists(path))
            {
                return new SnapshotTarget(path, null, null);
            }

            return new SnapshotTarget(path, File.ReadAllBytes(path), FileHash.Sha256OrNull(path));
        }
    }
}

