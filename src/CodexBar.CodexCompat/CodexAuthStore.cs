using System.Text.Json;
using CodexBar.Core;

namespace CodexBar.CodexCompat;

public sealed class CodexAuthStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<JsonDocument?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public string SerializeOpenAiOAuth(OAuthTokens tokens)
    {
        var document = new Dictionary<string, object?>
        {
            ["auth_mode"] = "chatgpt",
            ["OPENAI_API_KEY"] = null,
            ["tokens"] = tokens,
            ["last_refresh"] = tokens.LastRefresh
        };
        return JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
    }

    public string SerializeCompatibleApiKey(
        string apiKey,
        JsonElement? preservedTokens = null,
        DateTimeOffset? lastRefresh = null)
    {
        var document = new Dictionary<string, object?>
        {
            ["auth_mode"] = "apikey",
            ["OPENAI_API_KEY"] = apiKey
        };

        if (preservedTokens.HasValue)
        {
            document["tokens"] = preservedTokens.Value;
        }

        if (lastRefresh.HasValue)
        {
            document["last_refresh"] = lastRefresh.Value;
        }

        return JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
    }
}
