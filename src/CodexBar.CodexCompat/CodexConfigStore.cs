namespace CodexBar.CodexCompat;

public sealed class CodexConfigStore
{
    public async Task<CodexConfigDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return CodexConfigDocument.Parse(null);
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return CodexConfigDocument.Parse(content);
    }
}

