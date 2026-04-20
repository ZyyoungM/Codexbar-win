using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBar.Core;

namespace CodexBar.Auth;

public sealed record OpenAiOfficialUsageRefreshResult(
    AppConfig Config,
    int AccountsRefreshed,
    int FailedAccounts,
    bool Changed);

public sealed record OpenAiOfficialUsageSnapshot(
    AccountTier Tier,
    string? RawPlanType,
    QuotaUsageSnapshot FiveHourQuota,
    QuotaUsageSnapshot WeeklyQuota);

public sealed class OpenAiOfficialUsageService
{
    private static readonly Uri UsageEndpoint = new("https://chatgpt.com/backend-api/wham/usage");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IOAuthTokenStore _tokenStore;
    private readonly HttpClient _httpClient;

    public OpenAiOfficialUsageService(IOAuthTokenStore tokenStore, HttpClient? httpClient = null)
    {
        _tokenStore = tokenStore;
        _httpClient = httpClient ?? CreateHttpClient();
    }

    public async Task<OpenAiOfficialUsageRefreshResult> RefreshAsync(
        AppConfig config,
        TimeSpan minRefreshInterval,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        var accountsRefreshed = 0;
        var failedAccounts = 0;
        var accounts = new List<AccountRecord>(config.Accounts.Count);

        foreach (var account in config.Accounts)
        {
            if (!IsOfficiallyManagedOpenAiAccount(account))
            {
                accounts.Add(account);
                continue;
            }

            if (account.OfficialUsageFetchedAt is { } fetchedAt &&
                now - fetchedAt < minRefreshInterval)
            {
                accounts.Add(account);
                continue;
            }

            var updated = await RefreshAccountInternalAsync(account, now, cancellationToken);
            accountsRefreshed++;
            if (!string.IsNullOrWhiteSpace(updated.OfficialUsageError))
            {
                failedAccounts++;
            }

            changed |= updated != account;
            accounts.Add(updated);
        }

        return new OpenAiOfficialUsageRefreshResult(
            changed ? config with { Accounts = accounts } : config,
            accountsRefreshed,
            failedAccounts,
            changed);
    }

    public async Task<OpenAiOfficialUsageSnapshot> FetchAsync(
        OAuthTokens tokens,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            throw new InvalidOperationException("Missing OAuth access token.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("CodexBar.Win/0.1");

        if (!string.IsNullOrWhiteSpace(tokens.AccountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", tokens.AccountId);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new OpenAiOfficialUsageUnauthorizedException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI quota endpoint returned HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<UsageResponse>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("OpenAI quota response was empty.");

        var windows = new[]
        {
            payload.RateLimit?.PrimaryWindow,
            payload.RateLimit?.SecondaryWindow
        }.Where(window => window is not null).Cast<UsageWindowResponse>().ToList();

        if (windows.Count == 0)
        {
            throw new InvalidOperationException("OpenAI quota response did not include any usable windows.");
        }

        var now = DateTimeOffset.UtcNow;
        var fiveHour = SelectWindow(windows, expectedWindowSeconds: 5 * 60 * 60, fallbackIndex: 0);
        var weekly = SelectWindow(windows, expectedWindowSeconds: 7 * 24 * 60 * 60, fallbackIndex: 1);

        return new OpenAiOfficialUsageSnapshot(
            MapTier(payload.PlanType),
            payload.PlanType,
            ToQuotaSnapshot(fiveHour, now),
            ToQuotaSnapshot(weekly, now));
    }

    public async Task<AccountRecord> RefreshAccountAsync(
        AccountRecord account,
        CancellationToken cancellationToken = default)
    {
        if (!IsOfficiallyManagedOpenAiAccount(account))
        {
            return account;
        }

        return await RefreshAccountInternalAsync(account, DateTimeOffset.UtcNow, cancellationToken);
    }

    private async Task<AccountRecord> RefreshAccountInternalAsync(
        AccountRecord account,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken)
    {
        OAuthTokens? tokens = null;

        try
        {
            tokens = await _tokenStore.ReadTokensAsync(account.CredentialRef, cancellationToken);
            if (tokens is null)
            {
                return account with
                {
                    OfficialUsageFetchedAt = fetchedAt,
                    OfficialUsageError = "Official quota fetch failed: OAuth token was not found."
                };
            }

            var snapshot = await FetchAsync(tokens, cancellationToken);
            return account with
            {
                Tier = snapshot.Tier,
                OfficialPlanTypeRaw = snapshot.RawPlanType,
                FiveHourQuota = snapshot.FiveHourQuota,
                WeeklyQuota = snapshot.WeeklyQuota,
                OfficialUsageFetchedAt = fetchedAt,
                OfficialUsageError = null,
                Status = account.Status == AccountStatus.NeedsReauth ? AccountStatus.Active : account.Status
            };
        }
        catch (OpenAiOfficialUsageUnauthorizedException)
        {
            return account with
            {
                OfficialUsageFetchedAt = fetchedAt,
                OfficialUsageError = "Official quota fetch was unauthorized. Re-auth may be required.",
                Status = AccountStatus.NeedsReauth
            };
        }
        catch (Exception ex)
        {
            return account with
            {
                OfficialUsageFetchedAt = fetchedAt,
                OfficialUsageError = $"Official quota fetch failed: {SanitizeError(ex.Message)}"
            };
        }
    }

    private static bool IsOfficiallyManagedOpenAiAccount(AccountRecord account)
        => string.Equals(account.ProviderId, "openai", StringComparison.OrdinalIgnoreCase) &&
           account.CredentialRef.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateHttpClient()
        => new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

    private static UsageWindowResponse? SelectWindow(
        IReadOnlyList<UsageWindowResponse> windows,
        int expectedWindowSeconds,
        int fallbackIndex)
    {
        var exact = windows.FirstOrDefault(window => window.LimitWindowSeconds == expectedWindowSeconds);
        if (exact is not null)
        {
            return exact;
        }

        return fallbackIndex >= 0 && fallbackIndex < windows.Count ? windows[fallbackIndex] : null;
    }

    private static QuotaUsageSnapshot ToQuotaSnapshot(UsageWindowResponse? window, DateTimeOffset now)
    {
        if (window is null)
        {
            return new QuotaUsageSnapshot();
        }

        var usedPercent = window.UsedPercent;
        return new QuotaUsageSnapshot
        {
            Used = usedPercent,
            Limit = usedPercent.HasValue ? 100 : null,
            WindowSeconds = window.LimitWindowSeconds,
            ResetAt = window.ResetAfterSeconds.HasValue ? now.AddSeconds(window.ResetAfterSeconds.Value) : null
        };
    }

    private static AccountTier MapTier(string? rawPlanType)
    {
        if (string.IsNullOrWhiteSpace(rawPlanType))
        {
            return AccountTier.Unknown;
        }

        var normalized = NormalizePlanType(rawPlanType);
        return normalized switch
        {
            "free" => AccountTier.Free,
            "go" => AccountTier.Go,
            "plus" => AccountTier.Plus,
            "pro" => AccountTier.Pro,
            _ => AccountTier.Unknown
        };
    }

    private static string NormalizePlanType(string value)
    {
        var normalized = new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        return normalized.StartsWith("chatgpt", StringComparison.Ordinal)
            ? normalized["chatgpt".Length..]
            : normalized;
    }

    private static string SanitizeError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown error";
        }

        return message.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private sealed class OpenAiOfficialUsageUnauthorizedException : Exception;

    private sealed record UsageResponse
    {
        [JsonPropertyName("plan_type")]
        public string? PlanType { get; init; }

        [JsonPropertyName("rate_limit")]
        public RateLimitResponse? RateLimit { get; init; }
    }

    private sealed record RateLimitResponse
    {
        [JsonPropertyName("primary_window")]
        public UsageWindowResponse? PrimaryWindow { get; init; }

        [JsonPropertyName("secondary_window")]
        public UsageWindowResponse? SecondaryWindow { get; init; }
    }

    private sealed record UsageWindowResponse
    {
        [JsonPropertyName("used_percent")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? UsedPercent { get; init; }

        [JsonPropertyName("limit_window_seconds")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? LimitWindowSeconds { get; init; }

        [JsonPropertyName("reset_after_seconds")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? ResetAfterSeconds { get; init; }
    }
}
