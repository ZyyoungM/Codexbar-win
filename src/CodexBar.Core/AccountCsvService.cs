using System.Globalization;

namespace CodexBar.Core;

public sealed record AccountCsvExportOptions(bool IncludeSecrets = false);

public sealed record AccountCsvImportResult(int ProvidersImported, int AccountsImported, int SecretsImported, IReadOnlyList<string> Warnings);

public sealed class AccountCsvService
{
    private static readonly string[] Header =
    [
        "provider_id",
        "provider_name",
        "provider_kind",
        "base_url",
        "account_id",
        "account_label",
        "email",
        "subject_id",
        "manual_order",
        "status",
        "api_key",
        "access_token",
        "refresh_token",
        "id_token",
        "oauth_account_id",
        "last_refresh"
    ];

    private readonly ISecretStore _secretStore;
    private readonly IOAuthTokenStore _tokenStore;

    public AccountCsvService(ISecretStore secretStore, IOAuthTokenStore tokenStore)
    {
        _secretStore = secretStore;
        _tokenStore = tokenStore;
    }

    public async Task ExportAsync(AppConfig config, string path, AccountCsvExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new AccountCsvExportOptions();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream);
        await writer.WriteLineAsync(Csv.Join(Header));

        foreach (var account in config.Accounts
            .OrderBy(account => account.ManualOrder <= 0 ? int.MaxValue : account.ManualOrder)
            .ThenBy(account => account.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.Label, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = config.Providers.FirstOrDefault(p => p.ProviderId == account.ProviderId);
            if (provider is null)
            {
                continue;
            }

            string? apiKey = null;
            OAuthTokens? tokens = null;
            if (options.IncludeSecrets)
            {
                if (provider.Kind == ProviderKind.OpenAiCompatible)
                {
                    apiKey = await _secretStore.ReadSecretAsync(account.CredentialRef, cancellationToken);
                }
                else
                {
                    tokens = await _tokenStore.ReadTokensAsync(account.CredentialRef, cancellationToken);
                }
            }

            await writer.WriteLineAsync(Csv.Join(
                provider.ProviderId,
                provider.DisplayName,
                provider.Kind.ToString(),
                provider.BaseUrl ?? "",
                account.AccountId,
                account.Label,
                account.Email ?? "",
                account.SubjectId ?? "",
                account.ManualOrder.ToString(CultureInfo.InvariantCulture),
                account.Status.ToString(),
                apiKey ?? "",
                tokens?.AccessToken ?? "",
                tokens?.RefreshToken ?? "",
                tokens?.IdToken ?? "",
                tokens?.AccountId ?? "",
                tokens?.LastRefresh.ToString("O", CultureInfo.InvariantCulture) ?? ""));
        }
    }

    public async Task<(AppConfig Config, AccountCsvImportResult Result)> ImportAsync(AppConfig config, string path, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var providersImported = 0;
        var accountsImported = 0;
        var secretsImported = 0;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine is null)
        {
            throw new InvalidDataException("CSV file is empty.");
        }

        var header = Csv.ParseLine(headerLine);
        var index = header
            .Select((name, i) => (name: name.Trim(), i))
            .ToDictionary(entry => entry.name, entry => entry.i, StringComparer.OrdinalIgnoreCase);

        RequireColumn(index, "provider_id");
        RequireColumn(index, "account_id");

        var providers = config.Providers.ToList();
        var accounts = config.Accounts.ToList();
        var lineNumber = 1;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = Csv.ParseLine(line);
            var providerId = Get(index, values, "provider_id");
            var accountId = Get(index, values, "account_id");
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(accountId))
            {
                warnings.Add($"Line {lineNumber}: provider_id/account_id is missing; skipped.");
                continue;
            }

            var existingProvider = providers.FirstOrDefault(p => p.ProviderId == providerId);
            var providerKind = ParseEnum(Get(index, values, "provider_kind"), existingProvider?.Kind ?? GuessProviderKind(providerId, Get(index, values, "api_key")));
            var providerName = FirstNonEmpty(Get(index, values, "provider_name"), existingProvider?.DisplayName, providerId)!;
            var baseUrl = FirstNonEmpty(Get(index, values, "base_url"), existingProvider?.BaseUrl);

            var provider = new ProviderDefinition
            {
                ProviderId = providerId,
                DisplayName = providerName,
                Kind = providerKind,
                AuthMode = providerKind == ProviderKind.OpenAiOAuth ? AuthMode.OAuth : AuthMode.ApiKey,
                BaseUrl = providerKind == ProviderKind.OpenAiCompatible ? baseUrl : null,
                WireApi = existingProvider?.WireApi ?? WireApi.Responses,
                SupportsMultiAccount = true
            };
            providers = Upsert(providers, p => p.ProviderId == provider.ProviderId, provider);
            providersImported++;

            var existingAccount = accounts.FirstOrDefault(a => a.ProviderId == providerId && a.AccountId == accountId);
            var credentialRef = existingAccount?.CredentialRef ?? DefaultCredentialRef(providerKind, providerId, accountId);
            var manualOrder = ParseInt(Get(index, values, "manual_order")) ?? existingAccount?.ManualOrder ?? NextManualOrder(accounts);
            var status = ParseEnum(Get(index, values, "status"), existingAccount?.Status ?? AccountStatus.Active);
            var secretImported = await ImportSecretAsync(providerKind, credentialRef, index, values, cancellationToken);
            secretsImported += secretImported ? 1 : 0;

            if (existingAccount is null && !secretImported)
            {
                status = AccountStatus.NeedsReauth;
                warnings.Add($"Line {lineNumber}: imported metadata for {providerId}/{accountId}, but no secret/token was provided.");
            }

            var account = new AccountRecord
            {
                ProviderId = providerId,
                AccountId = accountId,
                Label = FirstNonEmpty(Get(index, values, "account_label"), existingAccount?.Label, accountId)!,
                Email = FirstNonEmpty(Get(index, values, "email"), existingAccount?.Email),
                SubjectId = FirstNonEmpty(Get(index, values, "subject_id"), existingAccount?.SubjectId),
                CredentialRef = credentialRef,
                Status = status,
                CreatedAt = existingAccount?.CreatedAt ?? DateTimeOffset.UtcNow,
                LastUsedAt = existingAccount?.LastUsedAt,
                ManualOrder = manualOrder
            };

            accounts = Upsert(accounts, a => a.ProviderId == providerId && a.AccountId == accountId, account);
            accountsImported++;
        }

        return (config with { Providers = providers, Accounts = accounts },
            new AccountCsvImportResult(providersImported, accountsImported, secretsImported, warnings));
    }

    private async Task<bool> ImportSecretAsync(ProviderKind providerKind, string credentialRef, Dictionary<string, int> index, IReadOnlyList<string> values, CancellationToken cancellationToken)
    {
        if (providerKind == ProviderKind.OpenAiCompatible)
        {
            var apiKey = Get(index, values, "api_key");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return false;
            }

            await _secretStore.WriteSecretAsync(credentialRef, apiKey, cancellationToken);
            return true;
        }

        var accessToken = Get(index, values, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        DateTimeOffset.TryParse(Get(index, values, "last_refresh"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastRefresh);
        await _tokenStore.WriteTokensAsync(credentialRef, new OAuthTokens
        {
            AccessToken = accessToken,
            RefreshToken = EmptyToNull(Get(index, values, "refresh_token")),
            IdToken = EmptyToNull(Get(index, values, "id_token")),
            AccountId = EmptyToNull(Get(index, values, "oauth_account_id")),
            LastRefresh = lastRefresh == default ? DateTimeOffset.UtcNow : lastRefresh
        }, cancellationToken);
        return true;
    }

    private static ProviderKind GuessProviderKind(string providerId, string? apiKey)
        => string.Equals(providerId, "openai", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(apiKey)
            ? ProviderKind.OpenAiOAuth
            : ProviderKind.OpenAiCompatible;

    private static string DefaultCredentialRef(ProviderKind providerKind, string providerId, string accountId)
        => providerKind == ProviderKind.OpenAiOAuth ? $"oauth:{providerId}:{accountId}" : $"api-key:{providerId}:{accountId}";

    private static string Get(Dictionary<string, int> index, IReadOnlyList<string> values, string name)
        => index.TryGetValue(name, out var i) && i >= 0 && i < values.Count ? values[i] : "";

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? ParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    private static int NextManualOrder(IEnumerable<AccountRecord> accounts)
        => accounts.Any() ? accounts.Max(account => account.ManualOrder) + 1 : 1;

    private static void RequireColumn(Dictionary<string, int> index, string name)
    {
        if (!index.ContainsKey(name))
        {
            throw new InvalidDataException($"CSV is missing required column: {name}");
        }
    }

    private static List<T> Upsert<T>(IEnumerable<T> source, Func<T, bool> predicate, T item)
    {
        var list = source.Where(entry => !predicate(entry)).ToList();
        list.Add(item);
        return list;
    }

    private static class Csv
    {
        public static string Join(params string[] values)
            => string.Join(",", values.Select(Escape));

        public static List<string> ParseLine(string line)
        {
            var values = new List<string>();
            var current = new StringWriter(CultureInfo.InvariantCulture);
            var quoted = false;

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Write('"');
                        i++;
                    }
                    else if (ch == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        current.Write(ch);
                    }

                    continue;
                }

                if (ch == ',')
                {
                    values.Add(current.ToString());
                    current.GetStringBuilder().Clear();
                }
                else if (ch == '"')
                {
                    quoted = true;
                }
                else
                {
                    current.Write(ch);
                }
            }

            values.Add(current.ToString());
            return values;
        }

        private static string Escape(string value)
            => value.IndexOfAny([',', '"', '\r', '\n']) < 0
                ? value
                : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
