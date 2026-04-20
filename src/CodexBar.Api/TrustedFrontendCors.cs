using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace CodexBar.Api;

public static class TrustedFrontendCors
{
    private static readonly string[] TrustedOriginsInternal =
    [
        "http://127.0.0.1:5057",
        "http://localhost:5057",
        "http://127.0.0.1:5173",
        "http://localhost:5173",
        "http://127.0.0.1:4173",
        "http://localhost:4173"
    ];

    public static IReadOnlyList<string> TrustedOrigins => TrustedOriginsInternal;

    public static void Apply(CorsPolicyBuilder policy)
    {
        policy
            .WithOrigins(TrustedOriginsInternal)
            .WithMethods("GET", "POST", "DELETE")
            .WithHeaders(HeaderNames.ContentType)
            .WithExposedHeaders(HeaderNames.ContentDisposition);
    }

    public static bool IsTrustedOrigin(string? origin)
    {
        var normalized = Normalize(origin);
        return normalized is not null &&
               TrustedOriginsInternal.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static string? Normalize(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) ||
            string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}
