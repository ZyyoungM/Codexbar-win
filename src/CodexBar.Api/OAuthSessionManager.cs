using CodexBar.Auth;
using CodexBar.Core;

namespace CodexBar.Api;

public sealed class OAuthSessionManager
{
    private readonly IOpenAiOAuthClient _client;
    private readonly ILoopbackCallbackListener _loopback;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private OAuthPendingFlow? _flow;
    private OAuthTokens? _capturedTokens;
    private bool _isListening;
    private string _statusMessage = "就绪。";
    private string? _errorMessage;
    private string? _successMessage;
    private bool _isCompleted;

    public OAuthSessionManager()
        : this(new OpenAiOAuthClientAdapter(), new LoopbackCallbackListenerAdapter())
    {
    }

    public OAuthSessionManager(IOpenAiOAuthClient client, ILoopbackCallbackListener loopback)
    {
        _client = client;
        _loopback = loopback;
    }

    public async Task<FrontendOAuthStateDto> GetStateAsync()
    {
        await _gate.WaitAsync();
        try
        {
            EnsureFlow();
            return ToDto();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FrontendOAuthStateDto> OpenBrowserAsync()
    {
        await _gate.WaitAsync();
        try
        {
            EnsureFlowForNewInteractiveAttemptUnsafe();
            StartListeningUnsafe();
            _client.OpenSystemBrowser(_flow!.AuthorizationUrl);
            _statusMessage = "已打开浏览器，并开始监听 localhost:1455。";
            _errorMessage = null;
            _successMessage = null;
            return ToDto();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FrontendOAuthStateDto> ListenAsync()
    {
        await _gate.WaitAsync();
        try
        {
            EnsureFlowForNewInteractiveAttemptUnsafe();
            StartListeningUnsafe();
            _statusMessage = "正在监听 localhost:1455 ...";
            _errorMessage = null;
            return ToDto();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FrontendCommandResult> CompleteAsync(
        FrontendOAuthCompleteRequest request,
        Func<OAuthTokens, string, CancellationToken, Task<FrontendCommandResult>> saveAccountAsync,
        CancellationToken cancellationToken)
    {
        OAuthPendingFlow flow;
        OAuthTokens tokens;
        var hasManualInput = !string.IsNullOrWhiteSpace(request.CallbackInput);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureFlow();
            flow = _flow!;

            if (!hasManualInput)
            {
                if (_capturedTokens is null)
                {
                    throw new InvalidOperationException("尚未收到 localhost 回调，请粘贴完整 callback URL 或 code。");
                }

                tokens = _capturedTokens;
            }
            else
            {
                tokens = default!;
            }
        }
        catch (Exception ex)
        {
            _errorMessage = Sanitize(ex.Message);
            _successMessage = null;
            _statusMessage = "OAuth 完成失败。";
            return new FrontendCommandResult(false, _errorMessage);
        }
        finally
        {
            _gate.Release();
        }

        if (hasManualInput)
        {
            try
            {
                tokens = await _client.CompleteManualInputAsync(flow, request.CallbackInput, cancellationToken);
            }
            catch (Exception ex)
            {
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    _errorMessage = Sanitize(ex.Message);
                    _successMessage = null;
                    _statusMessage = "OAuth 完成失败。";
                }
                finally
                {
                    _gate.Release();
                }

                return new FrontendCommandResult(false, Sanitize(ex.Message));
            }
        }

        var saveResult = await saveAccountAsync(tokens, request.Label, cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (saveResult.Ok)
            {
                StartNewFlowUnsafe(
                    "已保存当前 OpenAI 登录结果；如需继续添加账号，请开始新的登录流程。",
                    successMessage: saveResult.Message);
            }
            else
            {
                _capturedTokens = tokens;
                _isCompleted = true;
                _isListening = false;
                _statusMessage = "OpenAI 账号保存失败。";
                _errorMessage = saveResult.Message;
                _successMessage = null;
            }
        }
        finally
        {
            _gate.Release();
        }

        return saveResult;
    }

    private void EnsureFlow()
    {
        if (_flow is not null)
        {
            return;
        }

        StartNewFlowUnsafe();
    }

    private void EnsureFlowForNewInteractiveAttemptUnsafe()
    {
        if (_flow is null)
        {
            StartNewFlowUnsafe();
            return;
        }

        if (_isListening)
        {
            return;
        }

        if (_isCompleted)
        {
            StartNewFlowUnsafe();
        }
    }

    private void StartNewFlowUnsafe(string? statusMessage = null, string? successMessage = null)
    {
        CancelPendingListenerUnsafe();
        _flow = _client.BeginLogin();
        _capturedTokens = null;
        _isCompleted = false;
        _isListening = false;
        _errorMessage = null;
        _successMessage = successMessage;
        _statusMessage = statusMessage ?? "已生成新的 OpenAI OAuth 授权链接。";
    }

    private void CancelPendingListenerUnsafe()
    {
        _loopback.CancelPendingWait();
        _isListening = false;
    }

    private void StartListeningUnsafe()
    {
        if (_isListening || _flow is null)
        {
            return;
        }

        _isListening = true;
        _ = ListenInBackgroundAsync(_flow);
    }

    private async Task ListenInBackgroundAsync(OAuthPendingFlow flow)
    {
        try
        {
            var callback = await _loopback.WaitForCallbackAsync(flow.State, TimeSpan.FromMinutes(5));
            var tokens = await _client.ExchangeCodeAsync(flow, callback.Code);

            await _gate.WaitAsync();
            try
            {
                if (!ReferenceEquals(flow, _flow))
                {
                    return;
                }

                _capturedTokens = tokens;
                _isListening = false;
                _isCompleted = true;
                _statusMessage = "已收到 localhost 回调，请点击“完成登录”保存账号。";
                _errorMessage = null;
                _successMessage = "回调已捕获。";
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            await _gate.WaitAsync();
            try
            {
                if (!ReferenceEquals(flow, _flow))
                {
                    return;
                }

                _isListening = false;
                _errorMessage = Sanitize(ex.Message);
                _successMessage = null;
                _statusMessage = "监听回调失败，可改用手工粘贴 fallback。";
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private FrontendOAuthStateDto ToDto()
        => new(
            _flow?.AuthorizationUrl.ToString() ?? "",
            _isListening,
            _capturedTokens is not null,
            _isCompleted,
            _statusMessage,
            _errorMessage,
            _successMessage);

    private static string Sanitize(string message)
        => string.IsNullOrWhiteSpace(message)
            ? "unknown error"
            : message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
}
