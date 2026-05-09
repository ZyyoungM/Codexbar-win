using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace CodexBar.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        try
        {
            var request = UpdateRequest.Parse(args);
            var runner = new UpdateRunner(request);
            runner.Run();
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "CodexBar 自动更新失败。\n\n" + ex.Message,
                "CodexBar Updater",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}

internal sealed record UpdateRequest(
    int CurrentProcessId,
    string InstallDirectory,
    string ZipPath,
    string TargetVersion,
    string BackupDirectory,
    string RestartExecutableName,
    string LogPath,
    string TempRoot)
{
    public static UpdateRequest Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Count; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count)
            {
                throw new ArgumentException("Updater 参数格式异常。");
            }

            values[args[index]] = args[index + 1];
        }

        static string Required(IReadOnlyDictionary<string, string> values, string name)
            => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"缺少 updater 参数：{name}");

        if (!int.TryParse(Required(values, "--pid"), out var pid) || pid <= 0)
        {
            throw new ArgumentException("Updater PID 参数异常。");
        }

        var request = new UpdateRequest(
            pid,
            Path.GetFullPath(Required(values, "--install-dir")),
            Path.GetFullPath(Required(values, "--zip")),
            Required(values, "--version").Trim().TrimStart('v', 'V'),
            Path.GetFullPath(Required(values, "--backup-dir")),
            Path.GetFileName(Required(values, "--restart")),
            Path.GetFullPath(Required(values, "--log")),
            Path.GetFullPath(Required(values, "--temp-root")));
        UpdateSafety.ValidateInstallDirectory(request.InstallDirectory);
        if (string.IsNullOrWhiteSpace(request.RestartExecutableName))
        {
            throw new ArgumentException("重启入口为空。");
        }

        return request;
    }
}

internal sealed class UpdateRunner
{
    private readonly UpdateRequest _request;

    public UpdateRunner(UpdateRequest request)
    {
        _request = request;
    }

    public void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_request.LogPath)!);
        Log("Updater started.");
        WaitForMainProcessExit();
        UpdateSafety.ValidateInstallDirectory(_request.InstallDirectory);
        if (!File.Exists(_request.ZipPath))
        {
            throw new FileNotFoundException("更新包不存在。", _request.ZipPath);
        }

        var stagingRoot = Path.Combine(_request.TempRoot, "staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stagingRoot);
            ZipFile.ExtractToDirectory(_request.ZipPath, stagingRoot, overwriteFiles: true);
            var payloadRoot = ResolvePayloadRoot(stagingRoot);
            var restartSource = Path.Combine(payloadRoot, _request.RestartExecutableName);
            if (!File.Exists(restartSource))
            {
                throw new FileNotFoundException("新版包内没有找到重启入口。", restartSource);
            }

            CopyDirectory(_request.InstallDirectory, _request.BackupDirectory);
            Log("Backup completed: " + _request.BackupDirectory);
            ReplaceInstallDirectory(payloadRoot);
            Log("Install directory replaced.");
            CleanupAfterSuccess(stagingRoot);
            StartUpdatedApp();
            Log("Updated app started.");
        }
        catch (Exception ex)
        {
            Log("Update failed: " + ex);
            TryRollback();
            MessageBox.Show(
                "CodexBar 更新失败，已尽量恢复旧版本。\n\n日志路径：\n" + _request.LogPath + "\n\n" + ex.Message,
                "CodexBar Updater",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            throw;
        }
    }

    private void WaitForMainProcessExit()
    {
        try
        {
            using var process = Process.GetProcessById(_request.CurrentProcessId);
            if (!process.HasExited)
            {
                Log("Waiting for main process: " + _request.CurrentProcessId);
                if (!process.WaitForExit(60_000))
                {
                    throw new TimeoutException("等待 CodexBar 主程序退出超时，请手动关闭后重试。");
                }
            }
        }
        catch (ArgumentException)
        {
            Log("Main process already exited.");
        }
    }

    private static string ResolvePayloadRoot(string stagingRoot)
    {
        if (File.Exists(Path.Combine(stagingRoot, "CodexBar.Win.exe")))
        {
            return stagingRoot;
        }

        var children = Directory.GetDirectories(stagingRoot);
        if (children.Length == 1 && File.Exists(Path.Combine(children[0], "CodexBar.Win.exe")))
        {
            return children[0];
        }

        foreach (var directory in children)
        {
            if (File.Exists(Path.Combine(directory, "CodexBar.Win.exe")))
            {
                return directory;
            }
        }

        throw new InvalidDataException("更新包结构异常，未找到 CodexBar.Win.exe。");
    }

    private void ReplaceInstallDirectory(string payloadRoot)
    {
        foreach (var file in Directory.EnumerateFiles(_request.InstallDirectory))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (var directory in Directory.EnumerateDirectories(_request.InstallDirectory))
        {
            Directory.Delete(directory, recursive: true);
        }

        CopyDirectory(payloadRoot, _request.InstallDirectory);
    }

    private void TryRollback()
    {
        if (!Directory.Exists(_request.BackupDirectory))
        {
            return;
        }

        try
        {
            Log("Attempting rollback.");
            Directory.CreateDirectory(_request.InstallDirectory);
            foreach (var file in Directory.EnumerateFiles(_request.InstallDirectory))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (var directory in Directory.EnumerateDirectories(_request.InstallDirectory))
            {
                Directory.Delete(directory, recursive: true);
            }

            CopyDirectory(_request.BackupDirectory, _request.InstallDirectory);
            Log("Rollback completed.");
        }
        catch (Exception ex)
        {
            Log("Rollback failed: " + ex);
        }
    }

    private void CleanupAfterSuccess(string stagingRoot)
    {
        TryDeleteDirectory(stagingRoot);
        var downloadDirectory = Path.GetDirectoryName(_request.ZipPath);
        if (!string.IsNullOrWhiteSpace(downloadDirectory))
        {
            TryDeleteDirectory(downloadDirectory);
        }

        foreach (var helperDirectory in Directory.EnumerateDirectories(_request.TempRoot, "helper-*"))
        {
            TryDeleteDirectory(helperDirectory);
        }
    }

    private void StartUpdatedApp()
    {
        var restartPath = Path.Combine(_request.InstallDirectory, _request.RestartExecutableName);
        var startInfo = new ProcessStartInfo
        {
            FileName = restartPath,
            WorkingDirectory = _request.InstallDirectory,
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }

    private void Log(string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}";
        File.AppendAllText(_request.LogPath, line + Environment.NewLine, Encoding.UTF8);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

internal static class UpdateSafety
{
    public static void ValidateInstallDirectory(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new InvalidOperationException("安装目录为空，拒绝更新。");
        }

        var fullPath = Normalize(installDirectory);
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrWhiteSpace(root) && string.Equals(Normalize(root), fullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("安装目录不能是磁盘根目录。");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            var normalizedHome = Normalize(home);
            if (string.Equals(fullPath, normalizedHome, StringComparison.OrdinalIgnoreCase) ||
                IsSameOrChild(fullPath, Path.Combine(normalizedHome, ".codex")) ||
                IsSameOrChild(fullPath, Path.Combine(normalizedHome, ".codexbar")))
            {
                throw new InvalidOperationException("安装目录不能是用户 Home、~/.codex 或 ~/.codexbar。");
            }
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows) && IsSameOrChild(fullPath, windows))
        {
            throw new InvalidOperationException("安装目录不能位于 Windows 系统目录。");
        }
    }

    private static bool IsSameOrChild(string path, string parent)
    {
        var normalizedPath = Normalize(path);
        var normalizedParent = Normalize(parent);
        return string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
