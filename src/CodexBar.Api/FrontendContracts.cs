namespace CodexBar.Api;

public enum FrontendConnectionStatus
{
    Online,
    Offline,
    Checking
}

public sealed record FrontendCommandResult(bool Ok, string Message);

public sealed record FrontendAccountDto(
    string ProviderId,
    string AccountId,
    string Name,
    string Type,
    string? Email,
    string? BaseUrl,
    bool IsActive,
    FrontendConnectionStatus Status,
    int? Usage5h,
    int? UsageWeekly,
    string? Usage5hRefreshText,
    string? UsageWeeklyRefreshText,
    long? UsageDaily,
    long? UsageWeeklyTokens,
    long? UsageMonthly);

public sealed record FrontendDashboardDto(
    string HomePath,
    string RoutingMode,
    string Model,
    string ReasoningEffort,
    string LastRefreshText,
    string? QuotaStatusText,
    string FooterNote,
    IReadOnlyList<FrontendAccountDto> Accounts);

public sealed record FrontendGatewayPreviewDto(
    string RequestedAccountLabel,
    string ResolvedAccountLabel,
    string DecisionMessage);

public sealed record FrontendSettingsDto(
    string AppStatePath,
    string CodexHomePath,
    string CodexDesktopPath,
    string CodexCliPath,
    string AccountSortMode,
    string ActivationBehavior,
    string OpenAiAccountMode,
    bool StartupEnabled,
    FrontendGatewayPreviewDto? GatewayPreview);

public sealed record FrontendOAuthStateDto(
    string AuthorizationUrl,
    bool IsListening,
    bool HasCapturedTokens,
    bool IsCompleted,
    string StatusMessage,
    string? ErrorMessage,
    string? SuccessMessage);

public sealed record FrontendSettingsSaveRequest(
    string CodexDesktopPath,
    string CodexCliPath,
    string AccountSortMode,
    string ActivationBehavior,
    string OpenAiAccountMode,
    bool StartupEnabled);

public sealed record FrontendPathDetectRequest(
    string Path);

public sealed record FrontendLaunchRequest(
    string CodexDesktopPath,
    string CodexCliPath,
    string Target);

public sealed record FrontendAccountActionRequest(
    string ProviderId,
    string AccountId);

public sealed record FrontendEditAccountRequest(
    string ProviderId,
    string AccountId,
    string AccountLabel,
    string? ProviderName,
    string? BaseUrl,
    string? CodexProviderId,
    string? ApiKey);

public sealed record FrontendReorderAccountsRequest(
    IReadOnlyList<string> OrderedKeys);

public sealed record FrontendCompatibleProviderRequest(
    string ProviderId,
    string? CodexProviderId,
    string ProviderName,
    string BaseUrl,
    string AccountId,
    string AccountLabel,
    string ApiKey);

public sealed record FrontendOAuthCompleteRequest(
    string CallbackInput,
    string Label);
