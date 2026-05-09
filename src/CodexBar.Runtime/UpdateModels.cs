using System.Diagnostics;

namespace CodexBar.Runtime;

public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? Prerelease = null)
    : IComparable<SemanticVersion>
{
    public static SemanticVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
        {
            throw new FormatException($"Invalid semantic version: {value}");
        }

        return version;
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var metadataIndex = text.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            text = text[..metadataIndex];
        }

        string? prerelease = null;
        var prereleaseIndex = text.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0)
        {
            prerelease = text[(prereleaseIndex + 1)..];
            text = text[..prereleaseIndex];
            if (string.IsNullOrWhiteSpace(prerelease))
            {
                return false;
            }
        }

        var parts = text.Split('.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch) ||
            major < 0 ||
            minor < 0 ||
            patch < 0)
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
        {
            return minor;
        }

        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0)
        {
            return patch;
        }

        if (string.IsNullOrWhiteSpace(Prerelease) && string.IsNullOrWhiteSpace(other.Prerelease))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(Prerelease))
        {
            return 1;
        }

        if (string.IsNullOrWhiteSpace(other.Prerelease))
        {
            return -1;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public override string ToString()
        => string.IsNullOrWhiteSpace(Prerelease)
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var count = Math.Max(leftParts.Length, rightParts.Length);
        for (var index = 0; index < count; index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }

            if (index >= rightParts.Length)
            {
                return 1;
            }

            var leftIsNumber = int.TryParse(leftParts[index], out var leftNumber);
            var rightIsNumber = int.TryParse(rightParts[index], out var rightNumber);
            if (leftIsNumber && rightIsNumber)
            {
                var number = leftNumber.CompareTo(rightNumber);
                if (number != 0)
                {
                    return number;
                }

                continue;
            }

            if (leftIsNumber)
            {
                return -1;
            }

            if (rightIsNumber)
            {
                return 1;
            }

            var text = string.Compare(leftParts[index], rightParts[index], StringComparison.Ordinal);
            if (text != 0)
            {
                return text;
            }
        }

        return 0;
    }
}

public sealed record UpdateServiceOptions(string Owner = "ZyyoungM", string Repository = "Codexbar-win")
{
    public Uri ReleasesApiUri => new($"https://api.github.com/repos/{Owner}/{Repository}/releases");
}

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    NoStableRelease,
    AssetMissing,
    InvalidVersion,
    NetworkError,
    Failed
}

public sealed record UpdateAsset
{
    public required string Name { get; init; }
    public required Uri DownloadUrl { get; init; }
    public long SizeBytes { get; init; }
}

public sealed record UpdateReleaseInfo
{
    public required SemanticVersion Version { get; init; }
    public required string TagName { get; init; }
    public required string Name { get; init; }
    public required Uri ReleasePageUrl { get; init; }
    public required string Summary { get; init; }
    public required UpdateAsset ZipAsset { get; init; }
    public UpdateAsset? ChecksumAsset { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}

public sealed record UpdateCheckResult
{
    public required UpdateCheckStatus Status { get; init; }
    public required string Message { get; init; }
    public required SemanticVersion CurrentVersion { get; init; }
    public UpdateReleaseInfo? Release { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool HasUpdate => Status == UpdateCheckStatus.UpdateAvailable && Release is not null;
}

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Percent => TotalBytes is > 0 ? BytesReceived * 100d / TotalBytes.Value : null;
}

public sealed record UpdateChecksumResult
{
    public required bool IsMatch { get; init; }
    public required bool HasOfficialChecksum { get; init; }
    public required string CalculatedSha256 { get; init; }
    public string? ExpectedSha256 { get; init; }
    public string? Warning { get; init; }
    public required string Message { get; init; }
}

public sealed record UpdateDownloadResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? ZipPath { get; init; }
    public UpdateChecksumResult? Checksum { get; init; }
}

public sealed record UpdateInstallerValidationResult(bool IsValid, string Message);

public sealed record UpdateInstallRequest
{
    public required int CurrentProcessId { get; init; }
    public required string InstallDirectory { get; init; }
    public required string ZipPath { get; init; }
    public required string TargetVersion { get; init; }
    public required string BackupDirectory { get; init; }
    public required string RestartExecutableName { get; init; }
    public required string LogPath { get; init; }
    public required string TempRoot { get; init; }
}

public sealed record UpdateInstallerLaunchResult(bool Started, string Message, string? LogPath = null);

public interface IUpdateInstallerProcessStarter
{
    void Start(ProcessStartInfo startInfo);
}

