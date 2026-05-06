using CodexBar.Auth;
using CodexBar.Core;

namespace CodexBar.Api;

public sealed class OAuthSessionManager
{
    private readonly IOpenAiOAuthClient _client;
    private readonly ILoopbackCallbackListener _loopback;
    private readonly IOpenAiWorkspaceDiscoveryService _workspaceDiscovery;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private OAuthPendingFlow? _flow;
    private OAuthTokens? _capturedTokens;
    private IReadOnlyList<OpenAiWorkspaceDescriptor> _workspaceChoices = [];
    private string? _selectedWorkspaceId;
    private OpenAiWorkspaceDescriptor? _selectedWorkspaceHint;
    private bool _isListening;
    private string _statusMessage = "就绪。";
    private string? _errorMessage;
    private string? _successMessage;
    private bool _isCompleted;

    public OAuthSessionManager()
        : this(new OpenAiOAuthClientAdapter(), new LoopbackCallbackListenerAdapter(), new OpenAiWorkspaceDiscoveryService())
    {
    }

    public OAuthSessionManager(IOpenAiOAuthClient client, ILoopbackCallbackListener loopback)
        : this(client, loopback, new OpenAiWorkspaceDiscoveryService())
    {
    }

    public OAuthSessionManager(
        IOpenAiOAuthClient client,
        ILoopbackCallbackListener loopback,
        IOpenAiWorkspaceDiscoveryService workspaceDiscovery)
    {
        _client = client;
        _loopback = loopback;
        _workspaceDiscovery = workspaceDiscovery;
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

    public async Task<FrontendOAuthStateDto> OpenBrowserAsync(string? allowedWorkspaceId = null)
    {
        await _gate.WaitAsync();
        try
        {
            EnsureFlowForNewInteractiveAttemptUnsafe(allowedWorkspaceId);
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

    public async Task<FrontendOAuthStateDto> ListenAsync(string? allowedWorkspaceId = null)
    {
        await _gate.WaitAsync();
        try
        {
            EnsureFlowForNewInteractiveAttemptUnsafe(allowedWorkspaceId);
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

    public async Task<FrontendOAuthStateDto> SelectWorkspaceAsync(string? workspaceId)
    {
        await _gate.WaitAsync();
        try
        {
            var normalizedWorkspaceId = NormalizeWorkspaceId(workspaceId);
            if (normalizedWorkspaceId is null)
            {
                _errorMessage = "Choose a ChatGPT/Codex workspace first.";
                _successMessage = null;
                return ToDto();
            }

            if (_capturedTokens is null || _workspaceChoices.Count == 0)
            {
                _errorMessage = "Workspace choices are not ready yet. Complete OpenAI OAuth first.";
                _successMessage = null;
                return ToDto();
            }

            var selected = _workspaceChoices.FirstOrDefault(item =>
                string.Equals(item.WorkspaceId, normalizedWorkspaceId, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                _errorMessage = "Selected workspace was not found. Restart OAuth and try again.";
                _successMessage = null;
                return ToDto();
            }

            _selectedWorkspaceId = selected.WorkspaceId;
            _selectedWorkspaceHint = selected;
            if (selected.IsCurrent ||
                string.Equals(_capturedTokens.AccountId, selected.WorkspaceId, StringComparison.OrdinalIgnoreCase))
            {
                _capturedTokens = selected.ApplyTo(_capturedTokens);
                _statusMessage = $"Selected workspace {selected.WorkspaceName}.";
                _errorMessage = null;
                _successMessage = "Workspace selected.";
                return ToDto();
            }

            StartNewFlowUnsafe(
                $"Selected {selected.WorkspaceName}. Reopening OpenAI OAuth for that workspace...",
                allowedWorkspaceId: selected.WorkspaceId);
            _selectedWorkspaceId = selected.WorkspaceId;
            _selectedWorkspaceHint = selected;
            StartListeningUnsafe();
            _client.OpenSystemBrowser(_flow!.AuthorizationUrl);
            _statusMessage = $"Opened browser for {selected.WorkspaceName}; waiting for localhost callback.";
            _errorMessage = null;
            _successMessage = null;
            return ToDto();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FrontendCommandResult> CompleteAsync(
        FrontendOAuthCompleteRequest request,
        Func<OAuthTokens, string, OpenAiWorkspaceDescriptor?, CancellationToken, Task<FrontendCommandResult>> saveAccountAsync,
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

        var workspaceMismatch = ValidateAllowedWorkspace(flow, tokens);
        if (workspaceMismatch is not null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                _capturedTokens = tokens;
                _isCompleted = true;
                _isListening = false;
                _statusMessage = "OAuth workspace mismatch.";
                _errorMessage = workspaceMismatch;
                _successMessage = null;
            }
            finally
            {
                _gate.Release();
            }

            return new FrontendCommandResult(false, workspaceMismatch);
        }

        var saveResult = await saveAccountAsync(tokens, request.Label, _selectedWorkspaceHint, cancellationToken);

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

    private void EnsureFlowForNewInteractiveAttemptUnsafe(string? allowedWorkspaceId = null)
    {
        allowedWorkspaceId = NormalizeWorkspaceId(allowedWorkspaceId);
        if (_flow is null)
        {
            StartNewFlowUnsafe(allowedWorkspaceId: allowedWorkspaceId);
            return;
        }

        if (!string.Equals(_flow.Options.AllowedWorkspaceId, allowedWorkspaceId, StringComparison.Ordinal))
        {
            StartNewFlowUnsafe(allowedWorkspaceId: allowedWorkspaceId);
            return;
        }

        if (_isListening)
        {
            return;
        }

        if (_isCompleted)
        {
            StartNewFlowUnsafe(allowedWorkspaceId: allowedWorkspaceId);
        }
    }

    private void StartNewFlowUnsafe(
        string? statusMessage = null,
        string? successMessage = null,
        string? allowedWorkspaceId = null)
    {
        CancelPendingListenerUnsafe();
        var normalizedWorkspaceId = NormalizeWorkspaceId(allowedWorkspaceId);
        _flow = _client.BeginLogin(normalizedWorkspaceId);
        _capturedTokens = null;
        _workspaceChoices = [];
        _selectedWorkspaceId = normalizedWorkspaceId;
        if (normalizedWorkspaceId is null ||
            !string.Equals(_selectedWorkspaceHint?.WorkspaceId, normalizedWorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            _selectedWorkspaceHint = null;
        }
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
            var workspaces = await DiscoverWorkspacesBestEffortAsync(tokens);

            await _gate.WaitAsync();
            try
            {
                if (!ReferenceEquals(flow, _flow))
                {
                    return;
                }

                _capturedTokens = tokens;
                _workspaceChoices = workspaces;
                _selectedWorkspaceId = ResolveSelectedWorkspaceId(tokens, workspaces, flow.Options.AllowedWorkspaceId);
                _selectedWorkspaceHint ??= workspaces.FirstOrDefault(item =>
                    string.Equals(item.WorkspaceId, _selectedWorkspaceId, StringComparison.OrdinalIgnoreCase));
                _isListening = false;
                _isCompleted = true;
                _statusMessage = workspaces.Count > 1
                    ? "OAuth callback received. Choose a ChatGPT/Codex workspace to save."
                    : "已收到 localhost 回调，请点击“完成登录”保存账号。";
                _errorMessage = null;
                _successMessage = workspaces.Count > 1
                    ? "Workspace choices loaded."
                    : "回调已捕获。";
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
            _successMessage,
            _workspaceChoices.Select(ToWorkspaceDto).ToList(),
            _selectedWorkspaceId);

    private async Task<IReadOnlyList<OpenAiWorkspaceDescriptor>> DiscoverWorkspacesBestEffortAsync(OAuthTokens tokens)
    {
        try
        {
            return await _workspaceDiscovery.DiscoverAsync(tokens);
        }
        catch
        {
            return [];
        }
    }

    private static FrontendOAuthWorkspaceDto ToWorkspaceDto(OpenAiWorkspaceDescriptor workspace)
        => new(
            workspace.WorkspaceId,
            workspace.WorkspaceName,
            workspace.WorkspaceType,
            workspace.SeatType,
            workspace.IsCurrent,
            workspace.DisplayLabel);

    private static string? ResolveSelectedWorkspaceId(
        OAuthTokens tokens,
        IReadOnlyList<OpenAiWorkspaceDescriptor> workspaces,
        string? allowedWorkspaceId)
    {
        var normalizedAllowed = NormalizeWorkspaceId(allowedWorkspaceId);
        if (normalizedAllowed is not null &&
            workspaces.Any(item => string.Equals(item.WorkspaceId, normalizedAllowed, StringComparison.OrdinalIgnoreCase)))
        {
            return normalizedAllowed;
        }

        return workspaces.FirstOrDefault(item => item.IsCurrent)?.WorkspaceId ??
               workspaces.FirstOrDefault(item => string.Equals(item.WorkspaceId, tokens.AccountId, StringComparison.OrdinalIgnoreCase))?.WorkspaceId ??
               workspaces.FirstOrDefault()?.WorkspaceId ??
               NormalizeWorkspaceId(tokens.AccountId);
    }

    private static string Sanitize(string message)
        => string.IsNullOrWhiteSpace(message)
            ? "unknown error"
            : message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();

    private static string? NormalizeWorkspaceId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ValidateAllowedWorkspace(OAuthPendingFlow flow, OAuthTokens tokens)
    {
        var expected = NormalizeWorkspaceId(flow.Options.AllowedWorkspaceId);
        if (expected is null ||
            string.Equals(tokens.AccountId, expected, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var actual = string.IsNullOrWhiteSpace(tokens.AccountId) ? "(none)" : tokens.AccountId.Trim();
        return $"OAuth returned workspace {actual}, expected {expected}. Please confirm the workspace id or sign in to the matching ChatGPT workspace.";
    }
}
