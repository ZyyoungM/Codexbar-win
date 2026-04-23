using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexBar.Runtime;

public sealed record CodexDesktopProcessSnapshot(
    int ProcessId,
    int? ParentProcessId,
    string ProcessName,
    string? ExecutablePath,
    bool HasMainWindow);

public sealed record CodexDesktopProcessStatus(IReadOnlyList<CodexDesktopProcessSnapshot> Processes)
{
    public bool IsRunning => Processes.Count > 0;

    public bool HasClosableWindow => Processes.Any(process => process.HasMainWindow);

    public string Summary => IsRunning
        ? $"Codex Desktop 正在运行（{Processes.Count} 个相关进程）。"
        : "未检测到正在运行的 Codex Desktop。";
}

public sealed record CodexDesktopCloseResult
{
    public bool CloseRequested { get; init; }
    public bool AllExited { get; init; }
    public string Message { get; init; } = "";
}

public sealed record CodexDesktopTerminateResult
{
    public bool TerminateRequested { get; init; }
    public bool AllExited { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<int> AttemptedRootProcessIds { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public interface ICodexDesktopProcess : IDisposable
{
    int Id { get; }

    int? ParentProcessId { get; }

    string ProcessName { get; }

    string? ExecutablePath { get; }

    bool HasMainWindow { get; }

    bool HasExited { get; }

    bool RequestClose();

    void Terminate();

    void Refresh();

    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public interface ICodexDesktopProcessProvider
{
    IReadOnlyList<ICodexDesktopProcess> EnumerateCandidates();
}

public sealed record CodexDesktopForceTerminateResult(
    bool Succeeded,
    string? ErrorMessage = null);

public interface ICodexDesktopForceTerminator
{
    CodexDesktopForceTerminateResult TryForceTerminateProcess(int processId);
}

public sealed class CodexDesktopProcessService
{
    private readonly CodexDesktopLocator _desktopLocator;
    private readonly ICodexDesktopProcessProvider _processProvider;
    private readonly ICodexDesktopForceTerminator _forceTerminator;

    public CodexDesktopProcessService(
        CodexDesktopLocator? desktopLocator = null,
        ICodexDesktopProcessProvider? processProvider = null,
        ICodexDesktopForceTerminator? forceTerminator = null)
    {
        _desktopLocator = desktopLocator ?? new CodexDesktopLocator();
        _processProvider = processProvider ?? new SystemCodexDesktopProcessProvider();
        _forceTerminator = forceTerminator ?? new SystemCodexDesktopForceTerminator();
    }

    public CodexDesktopProcessStatus GetStatus(string? configuredDesktopPath = null)
    {
        var locatedDesktopPath = _desktopLocator.Locate(configuredDesktopPath);
        var candidates = _processProvider.EnumerateCandidates();
        try
        {
            var processes = TrackDesktopProcessTree(candidates, locatedDesktopPath)
                .Select(process => new CodexDesktopProcessSnapshot(
                    process.Id,
                    process.ParentProcessId,
                    process.ProcessName,
                    process.ExecutablePath,
                    process.HasMainWindow))
                .ToList();
            return new CodexDesktopProcessStatus(processes);
        }
        finally
        {
            DisposeProcesses(candidates);
        }
    }

    public async Task<CodexDesktopCloseResult> RequestCloseAsync(
        CodexDesktopProcessStatus status,
        string? configuredDesktopPath = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!status.IsRunning)
        {
            return new CodexDesktopCloseResult
            {
                CloseRequested = false,
                AllExited = true,
                Message = "未检测到正在运行的 Codex Desktop，跳过关闭。"
            };
        }

        var trackedProcessIds = status.Processes
            .Select(process => process.ProcessId)
            .ToHashSet();
        var candidates = _processProvider.EnumerateCandidates();
        var processes = candidates
            .Where(process => trackedProcessIds.Contains(process.Id))
            .ToList();

        try
        {
            if (processes.Count == 0)
            {
                return new CodexDesktopCloseResult
                {
                    CloseRequested = false,
                    AllExited = true,
                    Message = "Codex Desktop 已不在运行。"
                };
            }

            var closeRequested = false;
            foreach (var process in processes.Where(process => !process.HasExited && process.HasMainWindow))
            {
                closeRequested = process.RequestClose() || closeRequested;
            }

            if (!closeRequested)
            {
                return new CodexDesktopCloseResult
                {
                    CloseRequested = false,
                    AllExited = false,
                    Message = "Codex Desktop 正在运行，但没有可正常关闭的窗口。为避免静默强杀，请先手动退出 Codex Desktop 后再重试。"
                };
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
            try
            {
                await Task.WhenAll(processes.Select(process => process.WaitForExitAsync(timeoutSource.Token)));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout is handled by the final HasExited check below.
            }

            foreach (var process in processes)
            {
                process.Refresh();
            }

            var allExited = processes.All(process => process.HasExited);
            return new CodexDesktopCloseResult
            {
                CloseRequested = true,
                AllExited = allExited,
                Message = allExited
                    ? "Codex Desktop 已正常关闭。"
                    : "已请求 Codex Desktop 正常关闭，但仍有相关后台进程在运行。"
            };
        }
        finally
        {
            DisposeProcesses(candidates);
        }
    }

    public async Task<CodexDesktopTerminateResult> TerminateAfterUserConfirmationAsync(
        CodexDesktopProcessStatus status,
        string? configuredDesktopPath = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!status.IsRunning)
        {
            return new CodexDesktopTerminateResult
            {
                TerminateRequested = false,
                AllExited = true,
                Message = "未检测到正在运行的 Codex Desktop，跳过结束后台进程。"
            };
        }

        var trackedProcessIds = status.Processes
            .Select(process => process.ProcessId)
            .ToHashSet();
        var candidates = _processProvider.EnumerateCandidates();
        var processes = candidates
            .Where(process => trackedProcessIds.Contains(process.Id))
            .ToList();

        try
        {
            if (processes.Count == 0)
            {
                return new CodexDesktopTerminateResult
                {
                    TerminateRequested = false,
                    AllExited = true,
                    Message = "Codex Desktop 相关后台进程已退出。"
                };
            }

            var targets = GetTerminationTargets(processes);
            var terminateRequested = targets.Count > 0;
            var attemptedRoots = targets
                .Where(process => IsTerminationRoot(processes, process))
                .Select(process => process.Id)
                .ToList();
            var terminationErrors = new List<string>();

            foreach (var process in targets)
            {
                var error = TryRequestTerminateProcess(process);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    terminationErrors.Add(error);
                }
            }

            await WaitForProcessesExitAsync(
                targets,
                LimitTimeout(timeout, TimeSpan.FromMilliseconds(250)),
                cancellationToken);

            var fallbackSucceeded = new HashSet<int>();
            var fallbackTargets = targets
                .Where(process => !process.HasExited)
                .ToList();
            foreach (var process in fallbackTargets)
            {
                var fallbackResult = _forceTerminator.TryForceTerminateProcess(process.Id);
                if (fallbackResult.Succeeded)
                {
                    fallbackSucceeded.Add(process.Id);
                    continue;
                }

                terminationErrors.Add(string.IsNullOrWhiteSpace(fallbackResult.ErrorMessage)
                    ? $"PID {process.Id} taskkill 失败。"
                    : fallbackResult.ErrorMessage!);
            }

            await WaitForProcessesExitAsync(
                targets,
                timeout ?? TimeSpan.FromSeconds(4),
                cancellationToken);

            foreach (var process in fallbackTargets.Where(process => !process.HasExited && fallbackSucceeded.Contains(process.Id)))
            {
                terminationErrors.Add($"PID {process.Id} taskkill 已返回成功，但进程仍在运行。");
            }

            var allExited = processes.All(process => process.HasExited);
            return new CodexDesktopTerminateResult
            {
                TerminateRequested = terminateRequested,
                AllExited = allExited,
                AttemptedRootProcessIds = attemptedRoots,
                Errors = terminationErrors,
                Message = allExited
                    ? "Codex Desktop 相关后台进程已结束。"
                    : BuildTerminateFailureMessage(terminationErrors)
            };
        }
        finally
        {
            DisposeProcesses(candidates);
        }
    }

    private static IReadOnlyList<ICodexDesktopProcess> TrackDesktopProcessTree(
        IReadOnlyList<ICodexDesktopProcess> candidates,
        string? locatedDesktopPath)
    {
        var roots = candidates
            .Where(process => LooksLikeDesktopRoot(process, locatedDesktopPath))
            .ToList();
        if (roots.Count == 0)
        {
            return [];
        }

        var trackedIds = roots
            .Select(process => process.Id)
            .ToHashSet();
        var expanded = true;
        while (expanded)
        {
            expanded = false;
            foreach (var process in candidates)
            {
                if (trackedIds.Contains(process.Id) ||
                    !process.ParentProcessId.HasValue ||
                    !trackedIds.Contains(process.ParentProcessId.Value))
                {
                    continue;
                }

                trackedIds.Add(process.Id);
                expanded = true;
            }
        }

        return candidates
            .Where(process => trackedIds.Contains(process.Id))
            .ToList();
    }

    private static bool IsTerminationRoot(IReadOnlyList<ICodexDesktopProcess> processes, ICodexDesktopProcess process)
    {
        var byId = processes.ToDictionary(process => process.Id);
        return !process.ParentProcessId.HasValue ||
               !byId.TryGetValue(process.ParentProcessId.Value, out var parent) ||
               parent.HasExited;
    }

    private static IReadOnlyList<ICodexDesktopProcess> GetTerminationTargets(IReadOnlyList<ICodexDesktopProcess> processes)
    {
        var byId = processes.ToDictionary(process => process.Id);
        var depths = new Dictionary<int, int>();

        int GetDepth(ICodexDesktopProcess process)
        {
            if (depths.TryGetValue(process.Id, out var cached))
            {
                return cached;
            }

            if (!process.ParentProcessId.HasValue ||
                !byId.TryGetValue(process.ParentProcessId.Value, out var parent) ||
                parent.HasExited)
            {
                depths[process.Id] = 0;
                return 0;
            }

            var depth = GetDepth(parent) + 1;
            depths[process.Id] = depth;
            return depth;
        }

        return processes
            .Where(process => !process.HasExited)
            .OrderByDescending(GetDepth)
            .ThenByDescending(process => process.HasMainWindow)
            .ToList();
    }

    private static string? TryRequestTerminateProcess(ICodexDesktopProcess process)
    {
        try
        {
            process.Terminate();
            return null;
        }
        catch (Exception ex)
        {
            return $"PID {process.Id} Process.Kill 失败：{DiagnosticLogger.Redact(ex.Message)}";
        }
    }

    private static async Task WaitForProcessesExitAsync(
        IReadOnlyList<ICodexDesktopProcess> processes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (processes.Count == 0 || processes.All(process => process.HasExited))
        {
            return;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await Task.WhenAll(processes
                .Where(process => !process.HasExited)
                .Select(process => process.WaitForExitAsync(timeoutSource.Token)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout is handled by the final HasExited check below.
        }

        foreach (var process in processes)
        {
            process.Refresh();
        }
    }

    private static TimeSpan LimitTimeout(TimeSpan? timeout, TimeSpan defaultTimeout)
    {
        if (!timeout.HasValue || timeout.Value <= TimeSpan.Zero)
        {
            return defaultTimeout;
        }

        return timeout.Value < defaultTimeout ? timeout.Value : defaultTimeout;
    }

    private static string BuildTerminateFailureMessage(IReadOnlyList<string> terminationErrors)
    {
        if (terminationErrors.Count == 0)
        {
            return "已请求结束 Codex Desktop 相关后台进程，但它仍在运行。请手动退出后再重试。";
        }

        return "已请求结束 Codex Desktop 相关后台进程，但它仍在运行。"
            + string.Join("；", terminationErrors)
            + "。请手动退出后再重试。";
    }

    private static bool LooksLikeDesktopRoot(ICodexDesktopProcess process, string? locatedDesktopPath)
    {
        var executablePath = process.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(locatedDesktopPath) && PathsEqual(executablePath, locatedDesktopPath))
        {
            return true;
        }

        return string.Equals(Path.GetFileName(executablePath), "Codex.exe", StringComparison.Ordinal);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void DisposeProcesses(IEnumerable<ICodexDesktopProcess> processes)
    {
        foreach (var process in processes)
        {
            process.Dispose();
        }
    }
}

public sealed class SystemCodexDesktopProcessProvider : ICodexDesktopProcessProvider
{
    public IReadOnlyList<ICodexDesktopProcess> EnumerateCandidates()
    {
        var parents = ParentProcessResolver.GetParentProcessMap();
        var processes = new Dictionary<int, Process>();
        foreach (var processName in new[] { "Codex", "codex" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                processes[process.Id] = process;
            }
        }

        return processes.Values
            .Select(process => (ICodexDesktopProcess)new SystemCodexDesktopProcess(
                process,
                parents.TryGetValue(process.Id, out var parentProcessId) ? parentProcessId : null))
            .ToList();
    }
}

public sealed class SystemCodexDesktopForceTerminator : ICodexDesktopForceTerminator
{
    public CodexDesktopForceTerminateResult TryForceTerminateProcess(int processId)
    {
        try
        {
            using var fallback = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/PID {processId} /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
            if (fallback is null)
            {
                return new CodexDesktopForceTerminateResult(
                    false,
                    $"PID {processId} 无法启动 taskkill。");
            }

            var exited = fallback.WaitForExit(2500);
            if (!exited)
            {
                return new CodexDesktopForceTerminateResult(
                    false,
                    $"PID {processId} taskkill 超时。");
            }

            if (fallback.ExitCode == 0)
            {
                return new CodexDesktopForceTerminateResult(true);
            }

            var stdout = fallback.StandardOutput.ReadToEnd().Trim();
            var stderr = fallback.StandardError.ReadToEnd().Trim();
            var detail = string.Join(" ", new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));
            return new CodexDesktopForceTerminateResult(
                false,
                string.IsNullOrWhiteSpace(detail)
                    ? $"PID {processId} taskkill 失败，退出码 {fallback.ExitCode}。"
                    : $"PID {processId} taskkill 失败：{DiagnosticLogger.Redact(detail)}");
        }
        catch (Exception ex)
        {
            return new CodexDesktopForceTerminateResult(
                false,
                $"PID {processId} taskkill 异常：{DiagnosticLogger.Redact(ex.Message)}");
        }
    }
}

public sealed class SystemCodexDesktopProcess : ICodexDesktopProcess
{
    private readonly Process _process;

    public SystemCodexDesktopProcess(Process process, int? parentProcessId)
    {
        _process = process;
        ParentProcessId = parentProcessId;
    }

    public int Id => _process.Id;

    public int? ParentProcessId { get; }

    public string ProcessName => _process.ProcessName;

    public string? ExecutablePath
    {
        get
        {
            try
            {
                return _process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }
    }

    public bool HasMainWindow
    {
        get
        {
            try
            {
                return _process.MainWindowHandle != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch
            {
                return true;
            }
        }
    }

    public bool RequestClose()
    {
        try
        {
            return _process.CloseMainWindow();
        }
        catch
        {
            return false;
        }
    }

    public void Terminate()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }

    public void Refresh()
    {
        try
        {
            _process.Refresh();
        }
        catch
        {
            // Process may have exited while refreshing.
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        try
        {
            return _process.WaitForExitAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return Task.CompletedTask;
        }
    }

    public void Dispose()
        => _process.Dispose();
}

internal static class ParentProcessResolver
{
    private const uint SnapshotProcess = 0x00000002;

    public static IReadOnlyDictionary<int, int> GetParentProcessMap()
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return new Dictionary<int, int>();
        }

        try
        {
            var entry = new ProcessEntry32
            {
                dwSize = (uint)Marshal.SizeOf<ProcessEntry32>()
            };
            var parents = new Dictionary<int, int>();
            if (!Process32First(snapshot, ref entry))
            {
                return parents;
            }

            do
            {
                parents[unchecked((int)entry.th32ProcessID)] = unchecked((int)entry.th32ParentProcessID);
                entry.dwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));

            return parents;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
