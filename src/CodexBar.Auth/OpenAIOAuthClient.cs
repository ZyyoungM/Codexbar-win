using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBar.Core;

namespace CodexBar.Auth;

public sealed class OpenAIOAuthClient
{
    private readonly HttpClient _httpClient;

    public OpenAIOAuthClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public OAuthPendingFlow BeginLogin(OAuthOptions? options = null)
    {
        options ??= new OAuthOptions();
        var state = Pkce.CreateState();
        var verifier = Pkce.CreateVerifier();
        var challenge = Pkce.CreateChallenge(verifier);

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri.ToString(),
            ["response_type"] = "code",
            ["scope"] = options.Scope,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["id_token_add_organizations"] = "true",
            ["codex_cli_simplified_flow"] = "true",
            ["allowed_workspace_id"] = string.IsNullOrWhiteSpace(options.AllowedWorkspaceId) ? null : options.AllowedWorkspaceId.Trim(),
            ["originator"] = "Codex Desktop"
        };

        return new OAuthPendingFlow
        {
            AuthorizationUrl = AppendQuery(options.AuthorizationEndpoint, query),
            State = state,
            CodeVerifier = verifier,
            RedirectUri = options.RedirectUri,
            Options = options
        };
    }

    public void OpenSystemBrowser(Uri authorizationUrl)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = authorizationUrl.ToString(),
            UseShellExecute = true
        });
    }

    public async Task<OAuthTokens> ExchangeCodeAsync(
        OAuthPendingFlow flow,
        string code,
        CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = flow.Options.ClientId,
            ["code"] = code,
            ["redirect_uri"] = flow.RedirectUri.ToString(),
            ["code_verifier"] = flow.CodeVerifier
        };

        using var response = await _httpClient.PostAsync(flow.Options.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Token response was empty.");

        return tokenResponse.ToOAuthTokens(flow.Options.ClientId);
    }

    public async Task<OAuthTokens> CompleteManualInputAsync(
        OAuthPendingFlow flow,
        string callbackUrlOrCode,
        CancellationToken cancellationToken = default)
    {
        var parsed = ManualCallbackParser.Parse(callbackUrlOrCode);
        if (parsed.WasFullCallbackUrl)
        {
            if (string.IsNullOrWhiteSpace(parsed.State))
            {
                throw new InvalidOperationException("Callback URL does not contain a state parameter.");
            }

            if (!string.Equals(parsed.State, flow.State, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Callback state does not match the current OAuth login attempt.");
            }
        }

        return await ExchangeCodeAsync(flow, parsed.Code, cancellationToken);
    }

    private static Uri AppendQuery(Uri endpoint, IReadOnlyDictionary<string, string?> values)
    {
        var query = string.Join("&", values
            .Where(pair => pair.Value is not null)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        var separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
        return new Uri(endpoint + separator + query);
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public required string AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; init; }

        [JsonPropertyName("account_id")]
        public string? AccountId { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; init; }

        public OAuthTokens ToOAuthTokens(string clientId)
            => new()
            {
                AccessToken = AccessToken,
                RefreshToken = RefreshToken,
                IdToken = IdToken,
                AccountId = AccountId ?? ReadChatGptAccountId(IdToken),
                ClientId = clientId,
                LastRefresh = DateTimeOffset.UtcNow,
                ExtensionData = ExtensionData
            };
    }

    private static string? ReadChatGptAccountId(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = DecodeBase64Url(parts[1]);
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("https://api.openai.com/auth", out var authClaim) ||
                authClaim.ValueKind != JsonValueKind.Object ||
                !authClaim.TryGetProperty("chatgpt_account_id", out var accountId) ||
                accountId.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(accountId.GetString()) ? null : accountId.GetString()!.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
