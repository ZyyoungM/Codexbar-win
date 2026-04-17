using System.Collections.Concurrent;

namespace CodexBar.Core;

public sealed class InMemorySecretStore : ISecretStore, IOAuthTokenStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, OAuthTokens> _tokens = new(StringComparer.Ordinal);

    public Task WriteSecretAsync(string credentialRef, string secret, CancellationToken cancellationToken = default)
    {
        _secrets[credentialRef] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> ReadSecretAsync(string credentialRef, CancellationToken cancellationToken = default)
        => Task.FromResult(_secrets.TryGetValue(credentialRef, out var secret) ? secret : null);

    public Task DeleteSecretAsync(string credentialRef, CancellationToken cancellationToken = default)
    {
        _secrets.TryRemove(credentialRef, out _);
        return Task.CompletedTask;
    }

    public Task WriteTokensAsync(string credentialRef, OAuthTokens tokens, CancellationToken cancellationToken = default)
    {
        _tokens[credentialRef] = tokens;
        return Task.CompletedTask;
    }

    public Task<OAuthTokens?> ReadTokensAsync(string credentialRef, CancellationToken cancellationToken = default)
        => Task.FromResult(_tokens.TryGetValue(credentialRef, out var tokens) ? tokens : null);

    public Task DeleteTokensAsync(string credentialRef, CancellationToken cancellationToken = default)
    {
        _tokens.TryRemove(credentialRef, out _);
        return Task.CompletedTask;
    }
}
