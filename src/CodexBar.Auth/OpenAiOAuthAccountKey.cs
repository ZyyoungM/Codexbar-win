using System.Security.Cryptography;
using System.Text;
using CodexBar.Core;

namespace CodexBar.Auth;

public static class OpenAiOAuthAccountKey
{
    private const string ProviderId = "openai";

    public static string ResolveAccountId(AppConfig config, OAuthTokens tokens, OAuthIdentity identity)
    {
        var openAiAccountId = NormalizeOpenAiAccountId(tokens);
        var subjectId = EmptyToNull(identity.SubjectId);
        if (TextEquals(subjectId, openAiAccountId))
        {
            subjectId = null;
        }

        var email = EmptyToNull(identity.Email);

        var existingAccount = config.Accounts.FirstOrDefault(account =>
            IsOpenAiOAuthAccount(account) &&
            IsSameLogicalAccount(account, subjectId, email, openAiAccountId));
        if (existingAccount is not null)
        {
            return existingAccount.AccountId;
        }

        var keyMaterial = BuildStableKeyMaterial(subjectId, email, openAiAccountId);
        return keyMaterial is null
            ? Guid.NewGuid().ToString("N")
            : "oauth-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial)))
                .Substring(0, 32)
                .ToLowerInvariant();
    }

    public static string? NormalizeOpenAiAccountId(OAuthTokens tokens)
        => EmptyToNull(tokens.AccountId);

    private static bool IsSameLogicalAccount(
        AccountRecord account,
        string? subjectId,
        string? email,
        string? openAiAccountId)
    {
        var subjectMatches = TextEquals(account.SubjectId, subjectId);
        var emailMatches = TextEquals(account.Email, email);
        var openAiAccountMatches =
            TextEquals(account.OpenAiAccountId, openAiAccountId) ||
            (string.IsNullOrWhiteSpace(account.OpenAiAccountId) &&
             TextEquals(account.AccountId, openAiAccountId));

        if (subjectId is not null && openAiAccountId is not null)
        {
            return subjectMatches && openAiAccountMatches;
        }

        if (email is not null && openAiAccountId is not null)
        {
            return emailMatches && openAiAccountMatches;
        }

        if (subjectId is not null)
        {
            return subjectMatches;
        }

        if (email is not null)
        {
            return emailMatches;
        }

        return openAiAccountMatches;
    }

    private static string? BuildStableKeyMaterial(string? subjectId, string? email, string? openAiAccountId)
    {
        if (subjectId is not null && openAiAccountId is not null)
        {
            return $"sub:{subjectId}\nopenai_account:{openAiAccountId}";
        }

        if (email is not null && openAiAccountId is not null)
        {
            return $"email:{email.ToLowerInvariant()}\nopenai_account:{openAiAccountId}";
        }

        if (subjectId is not null)
        {
            return $"sub:{subjectId}";
        }

        if (email is not null)
        {
            return $"email:{email.ToLowerInvariant()}";
        }

        return openAiAccountId is null ? null : $"openai_account:{openAiAccountId}";
    }

    private static bool IsOpenAiOAuthAccount(AccountRecord account)
        => string.Equals(account.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase) &&
           account.CredentialRef.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase);

    private static bool TextEquals(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
