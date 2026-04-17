using System.Text;
using System.Text.Json;
using CodexBar.Core;

namespace CodexBar.Auth;

public sealed record OAuthIdentity(string? SubjectId, string? Email, string? Name)
{
    public string BestDisplayName(string fallback)
        => FirstNonEmpty(Email, Name, SubjectId, fallback) ?? fallback;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public static class OAuthIdentityExtractor
{
    public static OAuthIdentity Extract(OAuthTokens tokens)
    {
        if (string.IsNullOrWhiteSpace(tokens.IdToken))
        {
            return new OAuthIdentity(tokens.AccountId, null, null);
        }

        try
        {
            var parts = tokens.IdToken.Split('.');
            if (parts.Length < 2)
            {
                return new OAuthIdentity(tokens.AccountId, null, null);
            }

            var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            var subject = ReadString(root, "sub") ?? tokens.AccountId;
            var email = ReadString(root, "email")
                ?? ReadString(root, "https://api.openai.com/email")
                ?? ReadString(root, "https://openai.com/email");
            var name = ReadString(root, "name")
                ?? ReadString(root, "given_name")
                ?? ReadString(root, "https://api.openai.com/name")
                ?? ReadString(root, "https://openai.com/name");

            return new OAuthIdentity(subject, email, name);
        }
        catch
        {
            return new OAuthIdentity(tokens.AccountId, null, null);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
