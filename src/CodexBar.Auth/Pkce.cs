using System.Security.Cryptography;
using System.Text;

namespace CodexBar.Auth;

internal static class Pkce
{
    public static string CreateVerifier()
        => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string CreateChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    public static string CreateState()
        => Base64Url(RandomNumberGenerator.GetBytes(24));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

