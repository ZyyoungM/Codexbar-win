using System.Text.Json;
using CodexBar.Core;

namespace CodexBar.CodexCompat;

public sealed class UsageScanner
{
    public async Task<UsageSummary> ScanAsync(CodexHomeState home, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default)
    {
        var sessions = await ScanSessionsAsync(home, cancellationToken);
        return Summarize(sessions, rangeStart, rangeEnd);
    }

    public async Task<IReadOnlyList<SessionUsageRecord>> ScanSessionsAsync(CodexHomeState home, CancellationToken cancellationToken = default)
    {
        var sessions = new List<SessionUsageRecord>();

        foreach (var directory in new[] { home.SessionsPath, home.ArchivedSessionsPath })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                long input = 0;
                long output = 0;
                long cached = 0;
                var events = 0;
                DateTimeOffset? startedAt = null;

                await foreach (var line in ReadLinesSharedAsync(path, cancellationToken))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        startedAt ??= ExtractSessionStart(doc.RootElement);
                        var usageEvent = ExtractUsage(doc.RootElement, File.GetLastWriteTimeUtc(path));
                        if (usageEvent is null)
                        {
                            continue;
                        }

                        input += usageEvent.InputTokens;
                        output += usageEvent.OutputTokens;
                        cached += usageEvent.CachedInputTokens;
                        events++;
                    }
                    catch (JsonException)
                    {
                        continue;
                    }
                }

                sessions.Add(new SessionUsageRecord
                {
                    SessionPath = path,
                    StartedAt = startedAt ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero),
                    InputTokens = input,
                    OutputTokens = output,
                    CachedInputTokens = cached,
                    EventsScanned = events
                });
            }
        }

        return sessions;
    }

    private static async IAsyncEnumerable<string> ReadLinesSharedAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        using (stream)
        using (var reader = new StreamReader(stream))
        {
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is not null)
                {
                    yield return line;
                }
            }
        }
    }

    private static UsageEvent? ExtractUsage(JsonElement root, DateTime fallbackUtc)
    {
        var timestamp = FindTimestamp(root) ?? new DateTimeOffset(fallbackUtc, TimeSpan.Zero);
        var input = FindLong(root, "input_tokens") ?? FindLong(root, "prompt_tokens") ?? 0;
        var output = FindLong(root, "output_tokens") ?? FindLong(root, "completion_tokens") ?? 0;
        var cached = FindLong(root, "cached_input_tokens") ?? FindLong(root, "cached_tokens") ?? 0;

        if (input == 0 && output == 0 && cached == 0)
        {
            return null;
        }

        return new UsageEvent(timestamp, input, output, cached);
    }

    private static DateTimeOffset? ExtractSessionStart(JsonElement root)
    {
        if (!TryFindType(root, out var type) || !string.Equals(type, "session_meta", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (TryFindProperty(root, "payload", out var payload))
        {
            return FindTimestamp(payload) ?? FindTimestamp(root);
        }

        return FindTimestamp(root);
    }

    private static DateTimeOffset? FindTimestamp(JsonElement element)
    {
        foreach (var key in new[] { "timestamp", "created_at", "createdAt", "time" })
        {
            if (TryFindProperty(element, key, out var value))
            {
                if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed))
                {
                    return parsed;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unix))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unix);
                }
            }
        }

        return null;
    }

    private static long? FindLong(JsonElement element, string propertyName)
    {
        if (!TryFindProperty(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryFindProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }

                if (TryFindProperty(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindProperty(item, propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryFindType(JsonElement element, out string? type)
    {
        if (TryFindProperty(element, "type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
        {
            type = typeElement.GetString();
            return !string.IsNullOrWhiteSpace(type);
        }

        type = null;
        return false;
    }

    public static UsageSummary Summarize(IEnumerable<SessionUsageRecord> sessions, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        long input = 0;
        long output = 0;
        long cached = 0;
        var files = 0;
        var events = 0;

        foreach (var session in sessions)
        {
            if (session.StartedAt < rangeStart || session.StartedAt > rangeEnd)
            {
                continue;
            }

            files++;
            input += session.InputTokens;
            output += session.OutputTokens;
            cached += session.CachedInputTokens;
            events += session.EventsScanned;
        }

        return new UsageSummary
        {
            InputTokens = input,
            OutputTokens = output,
            CachedInputTokens = cached,
            EstimatedCostUsd = 0m,
            SessionFilesScanned = files,
            EventsScanned = events,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd
        };
    }

    private sealed record UsageEvent(DateTimeOffset Timestamp, long InputTokens, long OutputTokens, long CachedInputTokens);
}
