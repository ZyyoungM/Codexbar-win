using CodexBar.Auth;
using CodexBar.Core;

namespace CodexBar.Api;

public interface IOpenAiOAuthClient
{
    OAuthPendingFlow BeginLogin();
    void OpenSystemBrowser(Uri authorizationUrl);
    Task<OAuthTokens> ExchangeCodeAsync(OAuthPendingFlow flow, string code, CancellationToken cancellationToken = default);
    Task<OAuthTokens> CompleteManualInputAsync(OAuthPendingFlow flow, string callbackUrlOrCode, CancellationToken cancellationToken = default);
}

public interface ILoopbackCallbackListener
{
    Task<ManualCallbackParseResult> WaitForCallbackAsync(
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    void CancelPendingWait();
}

internal sealed class OpenAiOAuthClientAdapter : IOpenAiOAuthClient
{
    private readonly OpenAIOAuthClient _inner = new();

    public OAuthPendingFlow BeginLogin() => _inner.BeginLogin();

    public void OpenSystemBrowser(Uri authorizationUrl) => _inner.OpenSystemBrowser(authorizationUrl);

    public Task<OAuthTokens> ExchangeCodeAsync(OAuthPendingFlow flow, string code, CancellationToken cancellationToken = default)
        => _inner.ExchangeCodeAsync(flow, code, cancellationToken);

    public Task<OAuthTokens> CompleteManualInputAsync(OAuthPendingFlow flow, string callbackUrlOrCode, CancellationToken cancellationToken = default)
        => _inner.CompleteManualInputAsync(flow, callbackUrlOrCode, cancellationToken);
}

internal sealed class LoopbackCallbackListenerAdapter : ILoopbackCallbackListener
{
    private readonly LoopbackCallbackServer _inner = new();

    public Task<ManualCallbackParseResult> WaitForCallbackAsync(
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => _inner.WaitForCallbackAsync(expectedState, timeout, cancellationToken);

    public void CancelPendingWait()
        => _inner.CancelPendingWait();
}
