using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBar.Core;

public sealed class SwitchJournalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public SwitchJournalStore(string path)
    {
        _path = path;
    }

    public async Task AppendAsync(CodexSelection selection, string status, string message, CancellationToken cancellationToken = default)
        => await AppendEntryAsync(new SwitchJournalEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Selection = selection,
            Status = status,
            Message = message
        }, cancellationToken);

    public async Task AppendEntryAsync(SwitchJournalEntry entry, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var line = JsonSerializer.Serialize(entry, JsonOptions);
        await File.AppendAllTextAsync(_path, line + Environment.NewLine, cancellationToken);
    }

    public async Task<IReadOnlyList<SwitchJournalEntry>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var entries = new List<SwitchJournalEntry>();
        foreach (var line in await File.ReadAllLinesAsync(_path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<SwitchJournalEntry>(line, JsonOptions);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Skip malformed journal rows.
            }
        }

        return entries;
    }
}

public sealed record SwitchJournalEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required CodexSelection Selection { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
}
