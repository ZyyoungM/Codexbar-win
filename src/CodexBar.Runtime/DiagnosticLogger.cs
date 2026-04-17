using System.Text.Json;
using System.Text.RegularExpressions;
using CodexBar.Core;

namespace CodexBar.Runtime;

public sealed class DiagnosticLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Regex SensitiveQueryRegex = new("(code|access_token|refresh_token|id_token|api_key|OPENAI_API_KEY)=([^&\\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BearerRegex = new("Bearer\\s+[A-Za-z0-9._\\-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _logPath;
    private readonly object _sync = new();

    public DiagnosticLogger(AppPaths appPaths)
    {
        appPaths.EnsureDirectories();
        _logPath = Path.Combine(appPaths.LogsDirectory, $"codexbar-{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl");
    }

    public void Info(string eventName, object? data = null)
        => Write("info", eventName, data);

    public void Warning(string eventName, object? data = null)
        => Write("warning", eventName, data);

    public void Error(string eventName, Exception exception, object? data = null)
        => Write("error", eventName, new
        {
            error = Redact(exception.Message),
            exception = exception.GetType().FullName,
            data
        });

    private void Write(string level, string eventName, object? data)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow,
            level,
            eventName,
            data = Redact(JsonSerializer.Serialize(data ?? new { }, JsonOptions))
        };

        var line = JsonSerializer.Serialize(entry, JsonOptions);
        lock (_sync)
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
    }

    public static string Redact(string input)
    {
        input = SensitiveQueryRegex.Replace(input, "$1=<redacted>");
        input = BearerRegex.Replace(input, "Bearer <redacted>");
        return input;
    }
}

