using System.Diagnostics;
using System.Net.Http.Headers;
using CodexBar.Core;

namespace CodexBar.Auth;

public sealed record CompatibleProviderProbeResult
{
    public required string ProviderId { get; init; }
    public required string AccountId { get; init; }
    public required string Label { get; init; }
    public required string BaseUrl { get; init; }
    public bool Success { get; init; }
    public int? StatusCode { get; init; }
    public string Message { get; init; } = "";
    public TimeSpan Elapsed { get; init; }
    public string? SuggestedBaseUrl { get; init; }
}

public sealed class CompatibleProviderProbeService
{
    private readonly ISecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public CompatibleProviderProbeService(ISecretStore secretStore, HttpClient? httpClient = null)
    {
        _secretStore = secretStore;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task<IReadOnlyList<CompatibleProviderProbeResult>> ProbeAsync(
        AppConfig config,
        IEnumerable<AccountRecord> accounts,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CompatibleProviderProbeResult>();
        foreach (var account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = config.Providers.FirstOrDefault(item => item.ProviderId == account.ProviderId);
            if (provider is null || provider.Kind != ProviderKind.OpenAiCompatible)
            {
                continue;
            }

            results.Add(await ProbeAccountAsync(provider, account, cancellationToken));
        }

        return results;
    }

    public async Task<CompatibleProviderProbeResult> ProbeAccountAsync(
        ProviderDefinition provider,
        AccountRecord account,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            return Failure(provider, account, provider.BaseUrl ?? "", null, TimeSpan.Zero, "Base URL 为空。");
        }

        var apiKey = await _secretStore.ReadSecretAsync(account.CredentialRef, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Failure(provider, account, provider.BaseUrl, null, TimeSpan.Zero, "API Key 缺失。");
        }

        var configured = await ProbeModelsEndpointAsync(provider.BaseUrl, apiKey, cancellationToken);
        if (configured.Success || ShouldNotTryV1Fallback(configured.StatusCode, provider.BaseUrl))
        {
            return ToResult(provider, account, provider.BaseUrl, configured);
        }

        var suggestedBaseUrl = AppendV1(provider.BaseUrl);
        var suggested = await ProbeModelsEndpointAsync(suggestedBaseUrl, apiKey, cancellationToken);
        if (!suggested.Success)
        {
            return ToResult(provider, account, provider.BaseUrl, configured);
        }

        return Failure(
            provider,
            account,
            provider.BaseUrl,
            configured.StatusCode,
            configured.Elapsed,
            $"当前 Base URL 探测失败，但 {suggestedBaseUrl} 可连通；建议把 Base URL 改为这个 /v1 地址。",
            suggestedBaseUrl);
    }

    private async Task<ProbeAttempt> ProbeModelsEndpointAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        if (!TryBuildModelsUri(baseUrl, out var uri, out var error))
        {
            return new ProbeAttempt(false, null, TimeSpan.Zero, error);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("CodexBar.Win/0.1");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            sw.Stop();
            var statusCode = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new ProbeAttempt(true, statusCode, sw.Elapsed, $"HTTP {statusCode}，/models 可访问。");
            }

            return new ProbeAttempt(false, statusCode, sw.Elapsed, $"HTTP {statusCode}，/models 不可用。");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new ProbeAttempt(false, null, sw.Elapsed, "请求超时。");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeAttempt(false, null, sw.Elapsed, SanitizeError(ex.Message));
        }
    }

    private static CompatibleProviderProbeResult ToResult(
        ProviderDefinition provider,
        AccountRecord account,
        string baseUrl,
        ProbeAttempt attempt)
        => attempt.Success
            ? new CompatibleProviderProbeResult
            {
                ProviderId = provider.ProviderId,
                AccountId = account.AccountId,
                Label = account.Label,
                BaseUrl = baseUrl,
                Success = true,
                StatusCode = attempt.StatusCode,
                Elapsed = attempt.Elapsed,
                Message = attempt.Message
            }
            : Failure(provider, account, baseUrl, attempt.StatusCode, attempt.Elapsed, attempt.Message);

    private static CompatibleProviderProbeResult Failure(
        ProviderDefinition provider,
        AccountRecord account,
        string baseUrl,
        int? statusCode,
        TimeSpan elapsed,
        string message,
        string? suggestedBaseUrl = null)
        => new()
        {
            ProviderId = provider.ProviderId,
            AccountId = account.AccountId,
            Label = account.Label,
            BaseUrl = baseUrl,
            Success = false,
            StatusCode = statusCode,
            Elapsed = elapsed,
            Message = message,
            SuggestedBaseUrl = suggestedBaseUrl
        };

    private static bool TryBuildModelsUri(string baseUrl, out Uri uri, out string message)
    {
        uri = null!;
        message = "";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https"))
        {
            message = "Base URL 不是有效的 http/https 地址。";
            return false;
        }

        uri = new Uri(EnsureTrailingSlash(parsed), "models");
        return true;
    }

    private static bool ShouldNotTryV1Fallback(int? statusCode, string baseUrl)
        => statusCode is 401 or 403 || EndsWithV1Path(baseUrl);

    private static string AppendV1(string baseUrl)
        => EnsureTrailingSlash(new Uri(baseUrl)).ToString() + "v1";

    private static bool EndsWithV1Path(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var text = uri.ToString();
        return text.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(text + "/");
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

    private sealed record ProbeAttempt(bool Success, int? StatusCode, TimeSpan Elapsed, string Message);
}
