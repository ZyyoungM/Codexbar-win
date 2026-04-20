using CodexBar.Api;
using CodexBar.Auth;
using CodexBar.Core;

namespace CodexBar.Tests;

internal static class ApiRegressionTests
{
    public static Task TrustedFrontendCorsTest()
    {
        foreach (var origin in TrustedFrontendCors.TrustedOrigins)
        {
            ApiRegressionAssertions.AssertTrue(TrustedFrontendCors.IsTrustedOrigin(origin), $"Expected trusted origin: {origin}");
        }

        ApiRegressionAssertions.AssertTrue(!TrustedFrontendCors.IsTrustedOrigin("http://localhost:5174"));
        ApiRegressionAssertions.AssertTrue(!TrustedFrontendCors.IsTrustedOrigin("https://localhost:5173"));
        ApiRegressionAssertions.AssertTrue(!TrustedFrontendCors.IsTrustedOrigin("http://evil.example.com"));
        ApiRegressionAssertions.AssertTrue(!TrustedFrontendCors.IsTrustedOrigin("null"));
        return Task.CompletedTask;
    }

    public static async Task OAuthManualFallbackUsesCurrentInputTest()
    {
        var client = new FakeOAuthClient();
        var loopback = new FakeLoopbackCallbackListener("loopback-code");
        var manager = new OAuthSessionManager(client, loopback);
        var savedTokens = new List<OAuthTokens>();

        await manager.ListenAsync();
        await client.ExchangeCompleted.Task;

        var result = await manager.CompleteAsync(
            new FrontendOAuthCompleteRequest("manual-callback", "Manual"),
            (tokens, _, _) =>
            {
                savedTokens.Add(tokens);
                return Task.FromResult(new FrontendCommandResult(true, "saved"));
            },
            CancellationToken.None);

        ApiRegressionAssertions.AssertTrue(result.Ok, result.Message);
        ApiRegressionAssertions.AssertEqual(1, client.ManualInputCalls);
        ApiRegressionAssertions.AssertEqual(1, savedTokens.Count);
        ApiRegressionAssertions.AssertEqual("manual-access", savedTokens.Single().AccessToken);
        ApiRegressionAssertions.AssertEqual("manual-callback", client.ManualInputs.Single());
    }

    public static async Task OAuthSuccessfulSaveResetsAttemptStateTest()
    {
        var client = new FakeOAuthClient();
        var manager = new OAuthSessionManager(client, new FakeLoopbackCallbackListener("unused-code"));

        var initial = await manager.GetStateAsync();
        var result = await manager.CompleteAsync(
            new FrontendOAuthCompleteRequest("manual-callback", "Manual"),
            (_, _, _) => Task.FromResult(new FrontendCommandResult(true, "saved")),
            CancellationToken.None);

        ApiRegressionAssertions.AssertTrue(result.Ok, result.Message);

        var afterSave = await manager.GetStateAsync();
        ApiRegressionAssertions.AssertTrue(!afterSave.HasCapturedTokens, "captured tokens should be cleared after a successful save");
        ApiRegressionAssertions.AssertTrue(!afterSave.IsCompleted, "successful save should prepare a fresh attempt");
        ApiRegressionAssertions.AssertEqual("saved", afterSave.SuccessMessage);
        ApiRegressionAssertions.AssertTrue(!string.Equals(initial.AuthorizationUrl, afterSave.AuthorizationUrl, StringComparison.Ordinal),
            "a successful save should rotate to a fresh OAuth authorization URL");

        var retry = await manager.CompleteAsync(
            new FrontendOAuthCompleteRequest("", "Retry"),
            (_, _, _) => Task.FromResult(new FrontendCommandResult(true, "unexpected")),
            CancellationToken.None);
        ApiRegressionAssertions.AssertTrue(!retry.Ok, "blank retry should not be able to reuse the previous login result");
    }

    public static async Task OAuthFlowRotationCancelsPendingLoopbackListenerTest()
    {
        var client = new FakeOAuthClient();
        var loopback = new BlockingLoopbackCallbackListener();
        var manager = new OAuthSessionManager(client, loopback);

        await manager.ListenAsync();
        ApiRegressionAssertions.AssertTrue(
            await loopback.Starts.WaitAsync(TimeSpan.FromSeconds(2)),
            "expected the first localhost listener to start");

        var result = await manager.CompleteAsync(
            new FrontendOAuthCompleteRequest("manual-callback", "Manual"),
            (_, _, _) => Task.FromResult(new FrontendCommandResult(true, "saved")),
            CancellationToken.None);

        ApiRegressionAssertions.AssertTrue(result.Ok, result.Message);
        ApiRegressionAssertions.AssertEqual(1, loopback.CancelCalls);

        await manager.ListenAsync();
        ApiRegressionAssertions.AssertTrue(
            await loopback.Starts.WaitAsync(TimeSpan.FromSeconds(2)),
            "expected a fresh localhost listener to start after rotating the OAuth flow");
        ApiRegressionAssertions.AssertEqual(2, loopback.StartCalls);
    }

    public static async Task ReorderAccountsRejectsPartialPayloadTest()
    {
        using var temp = TempDir.Create();
        using var env = new EnvironmentVariableScope("USERPROFILE", temp.Path);
        var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = temp.Path
        });
        var store = new AppConfigStore(appPaths.ConfigPath);
        await store.SaveAsync(new AppConfig
        {
            Accounts =
            [
                new AccountRecord
                {
                    ProviderId = "openai",
                    AccountId = "a",
                    Label = "A",
                    CredentialRef = "oauth:openai:a",
                    ManualOrder = 1
                },
                new AccountRecord
                {
                    ProviderId = "openai",
                    AccountId = "b",
                    Label = "B",
                    CredentialRef = "oauth:openai:b",
                    ManualOrder = 2
                }
            ]
        });

        var service = new FrontendBackendService(new ProbeStatusStore());
        var result = await service.ReorderAccountsAsync(["openai/a"], CancellationToken.None);
        ApiRegressionAssertions.AssertTrue(!result.Ok, "partial payload should be rejected");
        ApiRegressionAssertions.AssertContains(result.Message, "覆盖全部账号");

        var reloaded = await store.LoadAsync();
        ApiRegressionAssertions.AssertEqual(2, reloaded.Accounts.Count);
        ApiRegressionAssertions.AssertSequenceEqual(["a", "b"], reloaded.Accounts.OrderBy(account => account.ManualOrder).Select(account => account.AccountId).ToArray());
    }

    public static async Task ReorderAccountsAcceptsFullPayloadTest()
    {
        using var temp = TempDir.Create();
        using var env = new EnvironmentVariableScope("USERPROFILE", temp.Path);
        var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = temp.Path
        });
        var store = new AppConfigStore(appPaths.ConfigPath);
        await store.SaveAsync(new AppConfig
        {
            Accounts =
            [
                new AccountRecord
                {
                    ProviderId = "openai",
                    AccountId = "a",
                    Label = "A",
                    CredentialRef = "oauth:openai:a",
                    ManualOrder = 1
                },
                new AccountRecord
                {
                    ProviderId = "compatible",
                    AccountId = "b",
                    Label = "B",
                    CredentialRef = "api-key:compatible:b",
                    ManualOrder = 2
                },
                new AccountRecord
                {
                    ProviderId = "openai",
                    AccountId = "c",
                    Label = "C",
                    CredentialRef = "oauth:openai:c",
                    ManualOrder = 3
                }
            ]
        });

        var service = new FrontendBackendService(new ProbeStatusStore());
        var result = await service.ReorderAccountsAsync(
            ["openai/c", "compatible/b", "openai/a"],
            CancellationToken.None);

        ApiRegressionAssertions.AssertTrue(result.Ok, result.Message);

        var reloaded = await store.LoadAsync();
        ApiRegressionAssertions.AssertSequenceEqual(
            ["openai/c", "compatible/b", "openai/a"],
            reloaded.Accounts
                .OrderBy(account => account.ManualOrder)
                .Select(account => $"{account.ProviderId}/{account.AccountId}")
                .ToArray());
        ApiRegressionAssertions.AssertEqual(3, reloaded.Accounts.Count);
    }
}

internal sealed class FakeOAuthClient : IOpenAiOAuthClient
{
    private int _flowCounter;

    public int ManualInputCalls { get; private set; }
    public List<string> ManualInputs { get; } = [];
    public TaskCompletionSource<bool> ExchangeCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public OAuthPendingFlow BeginLogin()
    {
        var flowId = Interlocked.Increment(ref _flowCounter);
        return new OAuthPendingFlow
        {
            AuthorizationUrl = new Uri($"https://auth.example.test/authorize?flow={flowId}", UriKind.Absolute),
            State = $"state-{flowId}",
            CodeVerifier = $"verifier-{flowId}",
            RedirectUri = new Uri("http://localhost:1455/auth/callback", UriKind.Absolute),
            Options = new OAuthOptions
            {
                AuthorizationEndpoint = new Uri("https://auth.example.test/authorize", UriKind.Absolute),
                TokenEndpoint = new Uri("https://auth.example.test/token", UriKind.Absolute),
                RedirectUri = new Uri("http://localhost:1455/auth/callback", UriKind.Absolute)
            }
        };
    }

    public void OpenSystemBrowser(Uri authorizationUrl)
    {
    }

    public Task<OAuthTokens> ExchangeCodeAsync(OAuthPendingFlow flow, string code, CancellationToken cancellationToken = default)
    {
        ExchangeCompleted.TrySetResult(true);
        return Task.FromResult(new OAuthTokens
        {
            AccessToken = "captured-access",
            RefreshToken = "captured-refresh",
            AccountId = "captured-account"
        });
    }

    public Task<OAuthTokens> CompleteManualInputAsync(OAuthPendingFlow flow, string callbackUrlOrCode, CancellationToken cancellationToken = default)
    {
        ManualInputCalls++;
        ManualInputs.Add(callbackUrlOrCode);
        return Task.FromResult(new OAuthTokens
        {
            AccessToken = "manual-access",
            RefreshToken = "manual-refresh",
            AccountId = "manual-account"
        });
    }
}

internal sealed class FakeLoopbackCallbackListener : ILoopbackCallbackListener
{
    private readonly string _code;

    public FakeLoopbackCallbackListener(string code)
    {
        _code = code;
    }

    public Task<ManualCallbackParseResult> WaitForCallbackAsync(
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ManualCallbackParseResult
        {
            Code = _code,
            State = expectedState,
            WasFullCallbackUrl = true
        });

    public void CancelPendingWait()
    {
    }
}

internal sealed class BlockingLoopbackCallbackListener : ILoopbackCallbackListener
{
    private TaskCompletionSource<ManualCallbackParseResult>? _currentWait;
    private bool _active;

    public SemaphoreSlim Starts { get; } = new(0);
    public int StartCalls { get; private set; }
    public int CancelCalls { get; private set; }

    public Task<ManualCallbackParseResult> WaitForCallbackAsync(
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (_active)
        {
            throw new IOException("Loopback listener is still active.");
        }

        _active = true;
        StartCalls++;
        _currentWait = new TaskCompletionSource<ManualCallbackParseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Starts.Release();
        return _currentWait.Task;
    }

    public void CancelPendingWait()
    {
        if (!_active)
        {
            return;
        }

        CancelCalls++;
        _active = false;
        _currentWait?.TrySetCanceled();
        _currentWait = null;
    }
}

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _originalValue;

    public EnvironmentVariableScope(string name, string? value)
    {
        _name = name;
        _originalValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, _originalValue);
    }
}

internal static class ApiRegressionAssertions
{
    public static void AssertTrue(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new Exception(message ?? "Expected true.");
        }
    }

    public static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"Expected {expected}, got {actual}.");
        }
    }

    public static void AssertContains(string text, string expected)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new Exception($"Expected text to contain {expected}.");
        }
    }

    public static void AssertSequenceEqual(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count != actual.Count)
        {
            throw new Exception($"Expected sequence length {expected.Count}, actual {actual.Count}.");
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(expected[index], actual[index], StringComparison.Ordinal))
            {
                throw new Exception($"Expected sequence item {index} to be '{expected[index]}', actual '{actual[index]}'.");
            }
        }
    }
}
