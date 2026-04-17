using System.Security.Cryptography;

namespace CodexBar.CodexCompat;

internal static class FileHash
{
    public static string? Sha256OrNull(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

