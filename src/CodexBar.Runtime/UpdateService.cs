using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CodexBar.Runtime;

public sealed class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly UpdateServiceOptions _options;

    public UpdateService(HttpClient? httpClient = null, UpdateServiceOptions? options = null)
    {
        _httpClient = httpClient ?? CreateHttpClient();
        _options = options ?? new UpdateServiceOptions();
    }

    public async Task<UpdateCheckResult> CheckLatestAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        if (!SemanticVersion.TryParse(currentVersion, out var localVersion))
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.InvalidVersion,
                CurrentVersion = default,
                Message = $"当前版本号格式异常：{currentVersion}"
            };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.ReleasesApiUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.NetworkError,
                    CurrentVersion = localVersion,
                    Message = $"检查更新失败：GitHub 返回 {(int)response.StatusCode} {response.ReasonPhrase}"
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream, JsonOptions, cancellationToken) ?? [];
            return SelectLatestStableRelease(releases, localVersion);
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.NetworkError,
                CurrentVersion = localVersion,
                Message = $"检查更新失败：网络请求异常（{DiagnosticLogger.Redact(ex.Message)}）"
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.NetworkError,
                CurrentVersion = localVersion,
                Message = $"检查更新失败：网络请求超时（{DiagnosticLogger.Redact(ex.Message)}）"
            };
        }
        catch (JsonException ex)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                CurrentVersion = localVersion,
                Message = $"检查更新失败：GitHub Release 数据无法解析（{DiagnosticLogger.Redact(ex.Message)}）"
            };
        }
    }

    public async Task<UpdateDownloadResult> DownloadUpdateAsync(
        UpdateReleaseInfo release,
        string? tempRoot = null,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = string.IsNullOrWhiteSpace(tempRoot)
            ? Path.Combine(Path.GetTempPath(), "CodexBarUpdate")
            : tempRoot;
        var versionDirectory = Path.Combine(root, release.Version.ToString());
        Directory.CreateDirectory(versionDirectory);

        var zipPath = Path.Combine(versionDirectory, release.ZipAsset.Name);
        try
        {
            await DownloadFileAsync(release.ZipAsset.DownloadUrl, zipPath, progress, cancellationToken);
            var length = new FileInfo(zipPath).Length;
            if (release.ZipAsset.SizeBytes > 0 && length != release.ZipAsset.SizeBytes)
            {
                return new UpdateDownloadResult
                {
                    Success = false,
                    ZipPath = zipPath,
                    Message = $"下载包大小不匹配：期望 {release.ZipAsset.SizeBytes} bytes，实际 {length} bytes。"
                };
            }

            string? checksumText = null;
            if (release.ChecksumAsset is not null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, release.ChecksumAsset.DownloadUrl);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new UpdateDownloadResult
                    {
                        Success = false,
                        ZipPath = zipPath,
                        Message = $"下载 SHA256 校验文件失败：GitHub 返回 {(int)response.StatusCode} {response.ReasonPhrase}"
                    };
                }

                checksumText = await response.Content.ReadAsStringAsync(cancellationToken);
            }

            var checksum = await UpdateChecksumVerifier.VerifyAsync(zipPath, checksumText, release.ZipAsset.Name, cancellationToken);
            if (!checksum.IsMatch)
            {
                return new UpdateDownloadResult
                {
                    Success = false,
                    ZipPath = zipPath,
                    Checksum = checksum,
                    Message = checksum.Message
                };
            }

            return new UpdateDownloadResult
            {
                Success = true,
                ZipPath = zipPath,
                Checksum = checksum,
                Message = checksum.HasOfficialChecksum
                    ? "下载完成，SHA256 校验通过。"
                    : "下载完成，已计算 SHA256；此 Release 未提供官方 checksum。"
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            return new UpdateDownloadResult
            {
                Success = false,
                ZipPath = File.Exists(zipPath) ? zipPath : null,
                Message = $"下载更新失败：{DiagnosticLogger.Redact(ex.Message)}"
            };
        }
    }

    public static string BuildReleasePageUrl(UpdateServiceOptions? options = null)
    {
        var value = options ?? new UpdateServiceOptions();
        return $"https://github.com/{value.Owner}/{value.Repository}/releases";
    }

    private async Task DownloadFileAsync(
        Uri uri,
        string destinationPath,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[1024 * 64];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report(new UpdateDownloadProgress(received, total));
        }
    }

    private static UpdateCheckResult SelectLatestStableRelease(IReadOnlyList<GitHubRelease> releases, SemanticVersion localVersion)
    {
        var newestInvalidVersion = false;
        foreach (var release in releases.Where(release => !release.Draft && !release.Prerelease))
        {
            if (!SemanticVersion.TryParse(release.TagName, out var remoteVersion))
            {
                newestInvalidVersion = true;
                continue;
            }

            if (remoteVersion.CompareTo(localVersion) <= 0)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpToDate,
                    CurrentVersion = localVersion,
                    Message = $"当前已是最新版本：v{localVersion}"
                };
            }

            var zipAssetName = $"CodexBar-portable-win-x64-v{remoteVersion}.zip";
            var zipAsset = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, zipAssetName, StringComparison.OrdinalIgnoreCase));
            if (zipAsset is null)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.AssetMissing,
                    CurrentVersion = localVersion,
                    Message = $"发现 v{remoteVersion}，但没有找到 {zipAssetName}。"
                };
            }

            var checksumAsset = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, zipAssetName + ".sha256", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(asset.Name, $"SHA256SUMS-v{remoteVersion}.txt", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));

            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
                CurrentVersion = localVersion,
                Release = new UpdateReleaseInfo
                {
                    Version = remoteVersion,
                    TagName = release.TagName,
                    Name = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
                    ReleasePageUrl = release.HtmlUrl ?? new Uri(BuildReleasePageUrl()),
                    Summary = SummarizeReleaseNotes(release.Body),
                    PublishedAt = release.PublishedAt,
                    ZipAsset = ToAsset(zipAsset),
                    ChecksumAsset = checksumAsset is null ? null : ToAsset(checksumAsset)
                },
                Message = $"发现新版本 v{remoteVersion}。"
            };
        }

        return new UpdateCheckResult
        {
            Status = newestInvalidVersion ? UpdateCheckStatus.InvalidVersion : UpdateCheckStatus.NoStableRelease,
            CurrentVersion = localVersion,
            Message = newestInvalidVersion
                ? "GitHub Release 版本号格式异常，无法判断更新。"
                : "没有找到可用的 stable Release。"
        };
    }

    private static string SummarizeReleaseNotes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "此 Release 未提供更新摘要。";
        }

        var lines = body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(6);
        var summary = string.Join(Environment.NewLine, lines);
        return summary.Length <= 700 ? summary : summary[..700] + "…";
    }

    private static UpdateAsset ToAsset(GitHubAsset asset)
        => new()
        {
            Name = asset.Name,
            DownloadUrl = asset.BrowserDownloadUrl ?? throw new InvalidDataException($"Release asset missing download URL: {asset.Name}"),
            SizeBytes = asset.Size
        };

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexBar-Windows-Updater");
        return client;
    }

    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = "";

        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("html_url")]
        public Uri? HtmlUrl { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed record GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("browser_download_url")]
        public Uri? BrowserDownloadUrl { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }
    }
}

public static partial class UpdateChecksumVerifier
{
    private static readonly Regex Sha256Regex = Sha256Pattern();

    public static async Task<UpdateChecksumResult> VerifyAsync(
        string filePath,
        string? checksumText,
        string assetName,
        CancellationToken cancellationToken = default)
    {
        var calculated = await ComputeSha256Async(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(checksumText))
        {
            return new UpdateChecksumResult
            {
                IsMatch = true,
                HasOfficialChecksum = false,
                CalculatedSha256 = calculated,
                Warning = "Release 未提供官方 SHA256 checksum；请人工核对显示的 SHA256。",
                Message = $"未提供官方 SHA256 checksum；已计算本地 SHA256：{calculated}"
            };
        }

        var expected = ExtractExpectedSha256(checksumText, assetName);
        if (expected is null)
        {
            return new UpdateChecksumResult
            {
                IsMatch = false,
                HasOfficialChecksum = true,
                CalculatedSha256 = calculated,
                Message = "SHA256 校验文件格式异常，未找到可用 checksum。"
            };
        }

        var matches = string.Equals(calculated, expected, StringComparison.OrdinalIgnoreCase);
        return new UpdateChecksumResult
        {
            IsMatch = matches,
            HasOfficialChecksum = true,
            CalculatedSha256 = calculated,
            ExpectedSha256 = expected.ToLowerInvariant(),
            Message = matches
                ? "SHA256 校验通过。"
                : $"SHA256 校验失败：期望 {expected.ToLowerInvariant()}，实际 {calculated}。"
        };
    }

    private static string? ExtractExpectedSha256(string checksumText, string assetName)
    {
        foreach (var line in checksumText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var match = Sha256Regex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (line.Contains(assetName, StringComparison.OrdinalIgnoreCase) ||
                !line.Contains(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }
        }

        return null;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [GeneratedRegex("[a-fA-F0-9]{64}", RegexOptions.Compiled)]
    private static partial Regex Sha256Pattern();
}

