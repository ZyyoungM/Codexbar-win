namespace CodexBar.Auth;

public sealed record OAuthOptions
{
    public string ClientId { get; init; } = "app_EMoamEEZ73f0CkXaXp7hrann";
    public Uri AuthorizationEndpoint { get; init; } = new("https://auth.openai.com/oauth/authorize");
    public Uri TokenEndpoint { get; init; } = new("https://auth.openai.com/oauth/token");
    public Uri RedirectUri { get; init; } = new("http://localhost:1455/auth/callback");
    public string Scope { get; init; } = "openid profile email offline_access api.connectors.read api.connectors.invoke";
}

public sealed record OAuthPendingFlow
{
    public required Uri AuthorizationUrl { get; init; }
    public required string State { get; init; }
    public required string CodeVerifier { get; init; }
    public required Uri RedirectUri { get; init; }
    public required OAuthOptions Options { get; init; }
}

public sealed record ManualCallbackParseResult
{
    public required string Code { get; init; }
    public string? State { get; init; }
    public bool WasFullCallbackUrl { get; init; }
}
