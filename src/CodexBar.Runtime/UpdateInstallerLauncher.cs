using System.Diagnostics;

namespace CodexBar.Runtime;

public sealed class UpdateInstallerLauncher
{
    private static readonly string[] SensitiveEnvironmentVariables =
    [
        "CODEX_HOME",
        "CODEX_THREAD_ID",
        "CODEX_INTERNAL_ORIGINATOR_OVERRIDE",
        "OPENAI_API_KEY",
        "OPENAI_ACCESS_TOKEN",
        "ANTHROPIC_API_KEY"
    ];

    private readonly IUpdateInstallerProcessStarter _processStarter;

    public UpdateInstallerLauncher(IUpdateInstallerProcessStarter? processStarter = null)
    {
        _processStarter = processStarter ?? new DefaultUpdateInstallerProcessStarter();
    }

    public async Task<UpdateInstallerLaunchResult> PrepareAndLaunchAsync(
        string bundledHelperPath,
        UpdateInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateInstallDirectory(request.InstallDirectory);
        if (!validation.IsValid)
        {
            return new UpdateInstallerLaunchResult(false, validation.Message, request.LogPath);
        }

        if (!File.Exists(request.ZipPath))
        {
            return new UpdateInstallerLaunchResult(false, $"更新包不存在：{request.ZipPath}", request.LogPath);
        }

        var writable = EnsureWritableInstallDirectory(request.InstallDirectory);
        if (!writable.IsValid)
        {
            return new UpdateInstallerLaunchResult(false, writable.Message, request.LogPath);
        }

        var preparedHelper = await PrepareHelperCopyAsync(bundledHelperPath, request.TempRoot, cancellationToken);
        if (preparedHelper is null)
        {
            return new UpdateInstallerLaunchResult(false, "未找到 updater helper；请使用新版便携包重新解压后再试。", request.LogPath);
        }

        var startInfo = BuildStartInfo(preparedHelper, request);
        _processStarter.Start(startInfo);
        return new UpdateInstallerLaunchResult(true, "更新器已启动，CodexBar 将退出并交给 updater 替换程序目录。", request.LogPath);
    }

    public static UpdateInstallRequest CreateInstallRequest(
        int currentProcessId,
        string installDirectory,
        string zipPath,
        string targetVersion,
        string restartExecutableName,
        string? tempRoot = null,
        string? userProfile = null)
    {
        var fullInstallDirectory = Path.GetFullPath(installDirectory);
        var safeVersion = targetVersion.Trim().TrimStart('v', 'V');
        var root = string.IsNullOrWhiteSpace(tempRoot)
            ? Path.Combine(Path.GetTempPath(), "CodexBarUpdate")
            : Path.GetFullPath(tempRoot);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var parent = Directory.GetParent(fullInstallDirectory)?.FullName
            ?? throw new InvalidOperationException($"无法定位安装目录的父目录：{fullInstallDirectory}");
        var backupDirectory = Path.Combine(parent, $".backup-{safeVersion}-{timestamp}");

        var validation = ValidateInstallDirectory(fullInstallDirectory, userProfile);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Message);
        }

        return new UpdateInstallRequest
        {
            CurrentProcessId = currentProcessId,
            InstallDirectory = fullInstallDirectory,
            ZipPath = Path.GetFullPath(zipPath),
            TargetVersion = safeVersion,
            BackupDirectory = backupDirectory,
            RestartExecutableName = Path.GetFileName(restartExecutableName),
            LogPath = Path.Combine(root, "logs", $"update-{safeVersion}-{timestamp}.log"),
            TempRoot = root
        };
    }

    public static ProcessStartInfo BuildStartInfo(string helperPath, UpdateInstallRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(helperPath),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(helperPath)),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        AddArgument(startInfo, "--pid");
        AddArgument(startInfo, request.CurrentProcessId.ToString());
        AddArgument(startInfo, "--install-dir");
        AddArgument(startInfo, request.InstallDirectory);
        AddArgument(startInfo, "--zip");
        AddArgument(startInfo, request.ZipPath);
        AddArgument(startInfo, "--version");
        AddArgument(startInfo, request.TargetVersion);
        AddArgument(startInfo, "--backup-dir");
        AddArgument(startInfo, request.BackupDirectory);
        AddArgument(startInfo, "--restart");
        AddArgument(startInfo, request.RestartExecutableName);
        AddArgument(startInfo, "--log");
        AddArgument(startInfo, request.LogPath);
        AddArgument(startInfo, "--temp-root");
        AddArgument(startInfo, request.TempRoot);

        foreach (var variable in SensitiveEnvironmentVariables)
        {
            RemoveEnvironmentVariable(startInfo, variable);
        }

        return startInfo;
    }

    public static UpdateInstallerValidationResult ValidateInstallDirectory(string? installDirectory, string? userProfile = null)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return new UpdateInstallerValidationResult(false, "安装目录为空，已拒绝自动更新。");
        }

        string fullPath;
        try
        {
            fullPath = NormalizePath(installDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new UpdateInstallerValidationResult(false, $"安装目录路径异常：{DiagnosticLogger.Redact(ex.Message)}");
        }

        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrWhiteSpace(root) && string.Equals(NormalizePath(root), fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateInstallerValidationResult(false, "安装目录不能是磁盘根目录，已拒绝自动更新。");
        }

        var home = string.IsNullOrWhiteSpace(userProfile)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfile;
        if (!string.IsNullOrWhiteSpace(home))
        {
            var normalizedHome = NormalizePath(home);
            if (string.Equals(fullPath, normalizedHome, StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateInstallerValidationResult(false, "安装目录不能是用户 Home，已拒绝自动更新。");
            }

            foreach (var unsafeDirectory in new[]
            {
                Path.Combine(normalizedHome, ".codex"),
                Path.Combine(normalizedHome, ".codexbar")
            })
            {
                if (IsSameOrChild(fullPath, unsafeDirectory))
                {
                    return new UpdateInstallerValidationResult(false, $"安装目录不能位于 {unsafeDirectory}，已拒绝自动更新。");
                }
            }
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows) && IsSameOrChild(fullPath, windows))
        {
            return new UpdateInstallerValidationResult(false, "安装目录不能位于 Windows 系统目录，已拒绝自动更新。");
        }

        return new UpdateInstallerValidationResult(true, "安装目录安全。");
    }

    private static UpdateInstallerValidationResult EnsureWritableInstallDirectory(string installDirectory)
    {
        try
        {
            Directory.CreateDirectory(installDirectory);
            var probe = Path.Combine(installDirectory, $".codexbar-update-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return new UpdateInstallerValidationResult(true, "安装目录可写。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UpdateInstallerValidationResult(
                false,
                $"当前程序目录不可写，无法自动更新；请手动解压新版便携包覆盖当前目录。详情：{DiagnosticLogger.Redact(ex.Message)}");
        }
    }

    private static async Task<string?> PrepareHelperCopyAsync(string bundledHelperPath, string tempRoot, CancellationToken cancellationToken)
    {
        if (!File.Exists(bundledHelperPath))
        {
            return null;
        }

        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(bundledHelperPath));
        if (sourceDirectory is null)
        {
            return null;
        }

        var destinationDirectory = Path.Combine(tempRoot, "helper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "CodexBar.Updater.*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(target, cancellationToken);
        }

        var helperCopy = Path.Combine(destinationDirectory, Path.GetFileName(bundledHelperPath));
        return File.Exists(helperCopy) ? helperCopy : null;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string value)
        => startInfo.ArgumentList.Add(value);

    private static void RemoveEnvironmentVariable(ProcessStartInfo startInfo, string name)
    {
        foreach (var key in startInfo.EnvironmentVariables.Keys.Cast<string>().ToList())
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                startInfo.EnvironmentVariables.Remove(key);
            }
        }
    }

    private static bool IsSameOrChild(string path, string candidateParent)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedParent = NormalizePath(candidateParent);
        return string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed class DefaultUpdateInstallerProcessStarter : IUpdateInstallerProcessStarter
    {
        public void Start(ProcessStartInfo startInfo)
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("无法启动 updater helper。");
            }
        }
    }
}

