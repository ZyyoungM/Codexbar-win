namespace CodexBar.Auth;

public static class ManualCallbackParser
{
    public static ManualCallbackParseResult Parse(string input)
    {
        input = input.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Callback URL or code is empty.", nameof(input));
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            {
                throw new FormatException("Callback URL does not contain a code parameter.");
            }

            query.TryGetValue("state", out var state);
            return new ManualCallbackParseResult
            {
                Code = code,
                State = state,
                WasFullCallbackUrl = true
            };
        }

        return new ManualCallbackParseResult
        {
            Code = input,
            State = null,
            WasFullCallbackUrl = false
        };
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        if (query.StartsWith('?'))
        {
            query = query[1..];
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pieces[0].Replace('+', ' '));
            var value = pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1].Replace('+', ' ')) : string.Empty;
            result[key] = value;
        }

        return result;
    }
}

