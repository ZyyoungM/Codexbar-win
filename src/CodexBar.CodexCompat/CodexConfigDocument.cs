using System.Text;
using System.Text.RegularExpressions;

namespace CodexBar.CodexCompat;

public sealed class CodexConfigDocument
{
    private readonly List<string> _lines;

    private CodexConfigDocument(IEnumerable<string> lines)
    {
        _lines = lines.ToList();
    }

    public static CodexConfigDocument Parse(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return new CodexConfigDocument([]);
        }

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        return new CodexConfigDocument(normalized.Split('\n'));
    }

    public void SetString(string key, string value)
        => SetRaw(key, Quote(value));

    public void SetBare(string key, string value)
        => SetRaw(key, value);

    public void SetSectionString(string sectionName, string key, string value)
        => SetSectionRaw(sectionName, key, Quote(value));

    public void RemoveTopLevelKey(string key)
    {
        var pattern = TopLevelKeyRegex(key);
        var end = FirstSectionIndex();
        for (var i = 0; i < end; i++)
        {
            if (pattern.IsMatch(_lines[i]))
            {
                _lines.RemoveAt(i);
                i--;
                end--;
            }
        }
    }

    public void RemoveSections(params string[] sectionNames)
    {
        var names = sectionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        RemoveSections(header => names.Contains(header));
    }

    public void RemoveSections(Func<string, bool> shouldRemove)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var header = TryGetSectionHeader(_lines[i]);
            if (header is null || !shouldRemove(header))
            {
                continue;
            }

            var end = i + 1;
            while (end < _lines.Count && TryGetSectionHeader(_lines[end]) is null)
            {
                end++;
            }

            _lines.RemoveRange(i, end - i);
            i--;
        }
    }

    public override string ToString()
    {
        TrimTrailingEmptyLines();
        return string.Join('\n', _lines) + "\n";
    }

    private void SetRaw(string key, string rawValue)
    {
        var pattern = TopLevelKeyRegex(key);
        var replacement = $"{key} = {rawValue}";
        var insertAt = FirstSectionIndex();

        for (var i = 0; i < insertAt; i++)
        {
            if (pattern.IsMatch(_lines[i]))
            {
                _lines[i] = replacement;
                return;
            }
        }

        if (insertAt > 0 && !string.IsNullOrWhiteSpace(_lines[insertAt - 1]))
        {
            _lines.Insert(insertAt, string.Empty);
            insertAt++;
        }

        _lines.Insert(insertAt, replacement);
    }

    private void SetSectionRaw(string sectionName, string key, string rawValue)
    {
        var pattern = TopLevelKeyRegex(key);
        var replacement = $"{key} = {rawValue}";
        var (start, end) = FindSection(sectionName);

        if (start < 0)
        {
            if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1]))
            {
                _lines.Add(string.Empty);
            }

            _lines.Add($"[{sectionName}]");
            _lines.Add(replacement);
            return;
        }

        for (var i = start + 1; i < end; i++)
        {
            if (pattern.IsMatch(_lines[i]))
            {
                _lines[i] = replacement;
                return;
            }
        }

        _lines.Insert(end, replacement);
    }

    private (int Start, int End) FindSection(string sectionName)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var header = TryGetSectionHeader(_lines[i]);
            if (header is null || !string.Equals(header, sectionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var end = i + 1;
            while (end < _lines.Count && TryGetSectionHeader(_lines[end]) is null)
            {
                end++;
            }

            return (i, end);
        }

        return (-1, -1);
    }

    private int FirstSectionIndex()
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            if (TryGetSectionHeader(_lines[i]) is not null)
            {
                return i;
            }
        }

        return _lines.Count;
    }

    private void TrimTrailingEmptyLines()
    {
        while (_lines.Count > 0 && string.IsNullOrWhiteSpace(_lines[^1]))
        {
            _lines.RemoveAt(_lines.Count - 1);
        }
    }

    private static Regex TopLevelKeyRegex(string key)
        => new($"^\\s*{Regex.Escape(key)}\\s*=", RegexOptions.Compiled);

    private static string? TryGetSectionHeader(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
        {
            return null;
        }

        return trimmed.Trim('[', ']').Trim();
    }

    private static string Quote(string value)
    {
        var escaped = new StringBuilder(value.Length + 2);
        escaped.Append('"');
        foreach (var c in value)
        {
            escaped.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c
            });
        }

        escaped.Append('"');
        return escaped.ToString();
    }
}
