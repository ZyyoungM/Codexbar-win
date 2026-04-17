using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBar.Core;

public sealed class AppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public AppConfigStore(string path)
    {
        _path = path;
    }

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return DefaultConfig();
        }

        await using var stream = File.OpenRead(_path);
        var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, JsonOptions, cancellationToken);
        return config ?? DefaultConfig();
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = $"{_path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(_path))
        {
            File.Replace(temp, _path, $"{_path}.bak-codexbar-last", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, _path);
        }
    }

    public static AppConfig DefaultConfig()
    {
        var openAiProvider = new ProviderDefinition
        {
            ProviderId = "openai",
            DisplayName = "OpenAI",
            Kind = ProviderKind.OpenAiOAuth,
            AuthMode = AuthMode.OAuth,
            SupportsMultiAccount = true,
            WireApi = WireApi.Responses
        };

        return new AppConfig
        {
            Providers = [openAiProvider]
        };
    }
}

