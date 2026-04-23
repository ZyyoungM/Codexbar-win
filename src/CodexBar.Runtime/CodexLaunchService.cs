using System.Diagnostics;
using CodexBar.Core;

namespace CodexBar.Runtime;

public sealed record CodexLaunchTarget(string Kind, string Path, string? Version = null);

public sealed record CodexLaunchResult
{
    public bool Attempted { get; init; }
    public bool Launched { get; init; }
    public string Message { get; init; } = "";
    public CodexLaunchTarget? Target { get; init; }
}

public interface IExternalProcessLauncher
{
    void Start(ProcessStartInfo startInfo);
}

public sealed class ExternalProcessLauncher : IExternalProcessLauncher
{
    public void Start(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to launch {startInfo.FileName}.");
        }
    }
}

public sealed class CodexLaunchService
{
    private static readonly string[] DesktopEnvironmentVariablesToRemove =
    [
        "ELECTRON_RUN_AS_NODE",
        "NODE_OPTIONS",
        "CODEX_INTERNAL_ORIGINATOR_OVERRIDE",
        "CODEX_SHELL",
        "CODEX_THREAD_ID",
        "DOTNET_ROOT",
        "DOTNET_ROOT_X64",
        "DOTNET_ROOT_X86",
        "DOTNET_ROOT_ARM64",
        "DOTNET_ROOT(x86)",
        "DOTNET_HOST_PATH",
        "DOTNET_CLI_HOME",
        "DOTNET_MULTILEVEL_LOOKUP",
        "DOTNET_BUNDLE_EXTRACT_BASE_DIR",
        "DOTNET_ADDITIONAL_DEPS",
        "DOTNET_SHARED_STORE",
        "DOTNET_STARTUP_HOOKS",
        "NUGET_PACKAGES"
    ];

    private readonly CodexDesktopLocator _desktopLocator;
    private readonly CodexCliLocator _cliLocator;
    private readonly IExternalProcessLauncher _processLauncher;

    public CodexLaunchService(
        CodexDesktopLocator? desktopLocator = null,
        CodexCliLocator? cliLocator = null,
        IExternalProcessLauncher? processLauncher = null)
    {
        _desktopLocator = desktopLocator ?? new CodexDesktopLocator();
        _cliLocator = cliLocator ?? new CodexCliLocator();
        _processLauncher = processLauncher ?? new ExternalProcessLauncher();
    }

    public Task<CodexLaunchResult> LaunchIfConfiguredAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => LaunchIfConfiguredAsync(settings, null, cancellationToken);

    public async Task<CodexLaunchResult> LaunchIfConfiguredAsync(
        AppSettings settings,
        IReadOnlyDictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken = default)
    {
        if (settings.ActivationBehavior != ActivationBehavior.LaunchNewCodex)
        {
            return new CodexLaunchResult
            {
                Attempted = false,
                Launched = false,
                Message = "\u5F53\u524D\u8BBE\u7F6E\u4E3A\u201C\u4EC5\u5199\u914D\u7F6E\u201D\uFF0C\u4E0D\u4F1A\u81EA\u52A8\u542F\u52A8 Codex\u3002"
            };
        }

        return await LaunchAsync(settings, environmentVariables, cancellationToken);
    }

    public Task<CodexLaunchResult> LaunchAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => LaunchAsync(settings, null, cancellationToken);

    public async Task<CodexLaunchResult> LaunchAsync(
        AppSettings settings,
        IReadOnlyDictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken = default)
    {
        var desktopPath = _desktopLocator.Locate(settings.CodexDesktopPath);
        if (!string.IsNullOrWhiteSpace(desktopPath))
        {
            return StartDesktop(new CodexLaunchTarget("desktop", desktopPath), environmentVariables);
        }

        var cli = await _cliLocator.LocateAsync(settings.CodexCliPath, cancellationToken);
        if (cli is not null)
        {
            return Start(new CodexLaunchTarget("cli", cli.Path, cli.Version), environmentVariables);
        }

        return new CodexLaunchResult
        {
            Attempted = true,
            Launched = false,
            Message = "\u672A\u627E\u5230 Codex Desktop \u6216 CLI\u3002\u8BF7\u5728\u8BBE\u7F6E\u4E2D\u68C0\u67E5\u8DEF\u5F84\uFF0C\u6216\u5148\u5B89\u88C5 Codex\u3002"
        };
    }

    private CodexLaunchResult Start(
        CodexLaunchTarget target,
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        try
        {
            var hasEnvironmentVariables = environmentVariables is { Count: > 0 };
            var startInfo = new ProcessStartInfo
            {
                FileName = target.Path,
                WorkingDirectory = Path.GetDirectoryName(target.Path),
                UseShellExecute = !hasEnvironmentVariables
            };
            ApplyEnvironmentVariables(startInfo, environmentVariables);
            _processLauncher.Start(startInfo);
            return new CodexLaunchResult
            {
                Attempted = true,
                Launched = true,
                Target = target,
                Message = target.Kind == "desktop"
                    ? $"\u5DF2\u542F\u52A8 Codex Desktop\uFF1A{target.Path}"
                    : $"\u5DF2\u542F\u52A8 Codex CLI\uFF1A{target.Path}"
            };
        }
        catch (Exception ex)
        {
            return new CodexLaunchResult
            {
                Attempted = true,
                Launched = false,
                Target = target,
                Message = DiagnosticLogger.Redact(ex.Message)
            };
        }
    }

    private CodexLaunchResult StartDesktop(
        CodexLaunchTarget target,
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target.Path,
                WorkingDirectory = Path.GetDirectoryName(target.Path),
                UseShellExecute = false
            };
            SanitizeDesktopEnvironment(startInfo);
            ApplyEnvironmentVariables(startInfo, environmentVariables);
            _processLauncher.Start(startInfo);
            return new CodexLaunchResult
            {
                Attempted = true,
                Launched = true,
                Target = target,
                Message = $"\u5DF2\u542F\u52A8 Codex Desktop\uFF1A{target.Path}"
            };
        }
        catch (Exception ex)
        {
            return new CodexLaunchResult
            {
                Attempted = true,
                Launched = false,
                Target = target,
                Message = DiagnosticLogger.Redact(ex.Message)
            };
        }
    }

    private static void SanitizeDesktopEnvironment(ProcessStartInfo startInfo)
    {
        foreach (var name in DesktopEnvironmentVariablesToRemove)
        {
            RemoveEnvironmentVariable(startInfo, name);
        }
    }

    private static void ApplyEnvironmentVariables(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        if (environmentVariables is not { Count: > 0 })
        {
            return;
        }

        foreach (var (name, value) in environmentVariables)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            startInfo.EnvironmentVariables[name] = value;
        }
    }

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
}
