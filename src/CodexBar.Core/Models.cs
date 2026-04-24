using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBar.Core;

public enum ProviderKind
{
    OpenAiOAuth,
    OpenAiCompatible
}

public enum AuthMode
{
    OAuth,
    ApiKey
}

public enum WireApi
{
    Responses,
    ChatCompletions
}

public enum AccountStatus
{
    Active,
    Stale,
    NeedsReauth,
    Revoked
}

public enum AccountSortMode
{
    Manual,
    Usage
}

public enum ActivationBehavior
{
    WriteConfigOnly,
    LaunchNewCodex
}

public enum OpenAiAccountMode
{
    ManualSwitch,
    AggregateGateway
}

public enum AccountTier
{
    Unknown,
    Free,
    Go,
    Plus,
    Pro
}

public sealed record ProviderDefinition
{
    public required string ProviderId { get; init; }
    public string? CodexProviderId { get; init; }
    public required string DisplayName { get; init; }
    public required ProviderKind Kind { get; init; }
    public string? BaseUrl { get; init; }
    public WireApi WireApi { get; init; } = WireApi.Responses;
    public AuthMode AuthMode { get; init; } = AuthMode.ApiKey;
    public bool SupportsMultiAccount { get; init; } = true;
}

public sealed record AccountRecord
{
    public required string AccountId { get; init; }
    public required string ProviderId { get; init; }
    public required string Label { get; init; }
    public string? Email { get; init; }
    public string? SubjectId { get; init; }
    public string? OpenAiAccountId { get; init; }
    public AccountTier Tier { get; init; } = AccountTier.Unknown;
    public string? OfficialPlanTypeRaw { get; init; }
    public QuotaUsageSnapshot FiveHourQuota { get; init; } = new();
    public QuotaUsageSnapshot WeeklyQuota { get; init; } = new();
    public DateTimeOffset? OfficialUsageFetchedAt { get; init; }
    public string? OfficialUsageError { get; init; }
    public required string CredentialRef { get; init; }
    public AccountStatus Status { get; init; } = AccountStatus.Active;
    public DateTimeOffset? LastUsedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public int ManualOrder { get; init; }
}

public sealed record QuotaUsageSnapshot
{
    public int? Used { get; init; }
    public int? Limit { get; init; }
    public int? WindowSeconds { get; init; }
    public DateTimeOffset? ResetAt { get; init; }
    public bool HasValue => Used.HasValue || Limit.HasValue;
}

public sealed record ProfileRecord
{
    public required string ProfileId { get; init; }
    public required string ProviderId { get; init; }
    public required string AccountId { get; init; }
    public string? ConfigSnapshotPath { get; init; }
    public string? AuthSnapshotPath { get; init; }
    public string? SourceHome { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? Checksum { get; init; }
}

public sealed record CodexSelection
{
    public required string ProviderId { get; init; }
    public required string AccountId { get; init; }
    public DateTimeOffset SelectedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ModelSettings
{
    public string Model { get; init; } = "gpt-5";
    public string ReviewModel { get; init; } = "gpt-5";
    public string ModelReasoningEffort { get; init; } = "medium";
}

public sealed record AppSettings
{
    public AccountSortMode AccountSortMode { get; init; } = AccountSortMode.Manual;
    public ActivationBehavior ActivationBehavior { get; init; } = ActivationBehavior.WriteConfigOnly;
    public OpenAiAccountMode OpenAiAccountMode { get; init; } = OpenAiAccountMode.ManualSwitch;
    public bool OpenOverlayOnStartup { get; init; }
    public bool SuppressRestartConfirmation { get; init; }
    public string? CodexDesktopPath { get; init; }
    public string? CodexCliPath { get; init; }
}

public sealed record OAuthTokens
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("id_token")]
    public string? IdToken { get; init; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; init; }

    [JsonPropertyName("client_id")]
    public string? ClientId { get; init; }

    [JsonPropertyName("last_refresh")]
    public DateTimeOffset LastRefresh { get; init; } = DateTimeOffset.UtcNow;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record AppConfig
{
    public int SchemaVersion { get; init; } = 1;
    public ModelSettings ModelSettings { get; init; } = new();
    public AppSettings Settings { get; init; } = new();
    public CodexSelection? ActiveSelection { get; init; }
    public List<ProviderDefinition> Providers { get; init; } = [];
    public List<AccountRecord> Accounts { get; init; } = [];
    public List<ProfileRecord> Profiles { get; init; } = [];
}

public sealed record CodexHomeState
{
    public required string RootPath { get; init; }
    public required string ConfigPath { get; init; }
    public required string AuthPath { get; init; }
    public required string SessionsPath { get; init; }
    public required string ArchivedSessionsPath { get; init; }
    public bool IsExplicitlyOverridden { get; init; }
}

public sealed record CodexFileSnapshot
{
    public required string Path { get; init; }
    public string? Sha256 { get; init; }
    public DateTimeOffset? LastWriteTimeUtc { get; init; }
    public long Length { get; init; }
    public string? BackupPath { get; init; }
}

public sealed record CodexSwitchResult
{
    public required CodexSelection Selection { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public bool RollbackApplied { get; init; }
    public bool ValidationPassed { get; init; }
    public required string Message { get; init; }
}

public sealed record ValidationReport
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed record UsageSummary
{
    public static UsageSummary Empty => new();
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CachedInputTokens { get; init; }
    public long TotalTokens => InputTokens + OutputTokens;
    public decimal EstimatedCostUsd { get; init; }
    public int SessionFilesScanned { get; init; }
    public int EventsScanned { get; init; }
    public DateTimeOffset RangeStart { get; init; }
    public DateTimeOffset RangeEnd { get; init; }
}

public sealed record SessionUsageRecord
{
    public required string SessionPath { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CachedInputTokens { get; init; }
    public long TotalTokens => InputTokens + OutputTokens;
    public int EventsScanned { get; init; }
}

public sealed record AccountUsageSummary
{
    public required string ProviderId { get; init; }
    public required string AccountId { get; init; }
    public UsageSummary Today { get; init; } = UsageSummary.Empty;
    public UsageSummary Last7Days { get; init; } = UsageSummary.Empty;
    public UsageSummary Last30Days { get; init; } = UsageSummary.Empty;
    public UsageSummary Lifetime { get; init; } = UsageSummary.Empty;
}

public sealed record UsageDashboard
{
    public UsageSummary Today { get; init; } = UsageSummary.Empty;
    public UsageSummary Last7Days { get; init; } = UsageSummary.Empty;
    public UsageSummary Last30Days { get; init; } = UsageSummary.Empty;
    public UsageSummary Lifetime { get; init; } = UsageSummary.Empty;
    public IReadOnlyList<AccountUsageSummary> Accounts { get; init; } = [];
    public int UnattributedSessions { get; init; }
}

public interface ISecretStore
{
    Task WriteSecretAsync(string credentialRef, string secret, CancellationToken cancellationToken = default);
    Task<string?> ReadSecretAsync(string credentialRef, CancellationToken cancellationToken = default);
    Task DeleteSecretAsync(string credentialRef, CancellationToken cancellationToken = default);
}

public interface IOAuthTokenStore
{
    Task WriteTokensAsync(string credentialRef, OAuthTokens tokens, CancellationToken cancellationToken = default);
    Task<OAuthTokens?> ReadTokensAsync(string credentialRef, CancellationToken cancellationToken = default);
    Task DeleteTokensAsync(string credentialRef, CancellationToken cancellationToken = default);
}
