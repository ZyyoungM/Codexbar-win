using CodexBar.Auth;
using CodexBar.CodexCompat;
using CodexBar.Core;
using CodexBar.Runtime;

var appPaths = AppPaths.Resolve();
appPaths.EnsureDirectories();
var logger = new DiagnosticLogger(appPaths);
var appConfigStore = new AppConfigStore(appPaths.ConfigPath);
var appConfig = await appConfigStore.LoadAsync();
var secretStore = new WindowsCredentialSecretStore();
var homeLocator = new CodexHomeLocator();
var integrityChecker = new CodexIntegrityChecker();

try
{
    var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
    switch (command)
    {
        case "home":
            PrintHome(homeLocator.Resolve());
            break;
        case "validate":
            PrintValidation(integrityChecker.Validate(homeLocator.Resolve()));
            break;
        case "scan":
            await ScanAsync(args.Skip(1).ToArray());
            break;
        case "scan-accounts":
            await ScanAccountsAsync();
            break;
        case "refresh-openai-usage":
            await RefreshOpenAiUsageAsync();
            break;
        case "probe-compatible":
            await ProbeCompatibleAsync(args.Skip(1).ToArray());
            break;
        case "add-compatible":
            await AddCompatibleAsync(args.Skip(1).ToArray());
            break;
        case "export-accounts":
            await ExportAccountsAsync(args.Skip(1).ToArray());
            break;
        case "import-accounts":
            await ImportAccountsAsync(args.Skip(1).ToArray());
            break;
        case "export-history":
            await ExportHistoryAsync(args.Skip(1).ToArray());
            break;
        case "import-history":
            await ImportHistoryAsync(args.Skip(1).ToArray());
            break;
        case "activate":
            await ActivateAsync(args.Skip(1).ToArray());
            break;
        case "resolve-openai":
            await ResolveOpenAiAsync(args.Skip(1).ToArray());
            break;
        case "config":
            await ConfigAsync(args.Skip(1).ToArray());
            break;
        case "locate-codex":
            await LocateCodexAsync();
            break;
        case "oauth-url":
            PrintOAuthUrl();
            break;
        default:
            PrintHelp();
            break;
    }
}
catch (Exception ex)
{
    logger.Error("cli.error", ex);
    Console.Error.WriteLine(DiagnosticLogger.Redact(ex.Message));
    Environment.ExitCode = 1;
}

void PrintHome(CodexHomeState home)
{
    Console.WriteLine($"CODEX_HOME: {home.RootPath}");
    Console.WriteLine($"config.toml: {home.ConfigPath}");
    Console.WriteLine($"auth.json: {home.AuthPath}");
    Console.WriteLine($"sessions: {home.SessionsPath}");
    Console.WriteLine($"archived_sessions: {home.ArchivedSessionsPath}");
    Console.WriteLine($"explicit: {home.IsExplicitlyOverridden}");
}

void PrintValidation(ValidationReport report)
{
    Console.WriteLine(report.IsValid ? "valid" : "invalid");
    foreach (var error in report.Errors)
    {
        Console.WriteLine($"error: {error}");
    }

    foreach (var warning in report.Warnings)
    {
        Console.WriteLine($"warning: {warning}");
    }
}

async Task ScanAsync(string[] commandArgs)
{
    var days = int.TryParse(commandArgs.FirstOrDefault(), out var parsed) ? parsed : 30;
    var now = DateTimeOffset.UtcNow;
    var summary = await new UsageScanner().ScanAsync(homeLocator.Resolve(), now.AddDays(-days), now);
    Console.WriteLine($"files: {summary.SessionFilesScanned}");
    Console.WriteLine($"events: {summary.EventsScanned}");
    Console.WriteLine($"input_tokens: {summary.InputTokens}");
    Console.WriteLine($"output_tokens: {summary.OutputTokens}");
    Console.WriteLine($"cached_input_tokens: {summary.CachedInputTokens}");
    Console.WriteLine($"estimated_cost_usd: {summary.EstimatedCostUsd:0.0000}");
}

async Task ScanAccountsAsync()
{
    var dashboard = await new UsageAttributionService(
        new UsageScanner(),
        new SwitchJournalStore(appPaths.SwitchJournalPath))
        .BuildDashboardAsync(appConfig, homeLocator.Resolve(), DateTimeOffset.Now);

    Console.WriteLine($"today_tokens: {dashboard.Today.TotalTokens}");
    Console.WriteLine($"last7_tokens: {dashboard.Last7Days.TotalTokens}");
    Console.WriteLine($"last30_tokens: {dashboard.Last30Days.TotalTokens}");
    Console.WriteLine($"lifetime_tokens: {dashboard.Lifetime.TotalTokens}");
    Console.WriteLine($"unattributed_sessions: {dashboard.UnattributedSessions}");
    foreach (var account in dashboard.Accounts
                 .OrderByDescending(account => account.Last30Days.TotalTokens)
                 .ThenBy(account => account.ProviderId, StringComparer.OrdinalIgnoreCase)
                 .ThenBy(account => account.AccountId, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine($"{account.ProviderId}/{account.AccountId}: today={account.Today.TotalTokens}, last7={account.Last7Days.TotalTokens}, last30={account.Last30Days.TotalTokens}, lifetime={account.Lifetime.TotalTokens}");
    }
}

async Task RefreshOpenAiUsageAsync()
{
    var refresh = await new OpenAiOfficialUsageService(secretStore)
        .RefreshAsync(appConfig, TimeSpan.Zero);
    appConfig = refresh.Config;
    if (refresh.Changed)
    {
        await appConfigStore.SaveAsync(appConfig);
    }

    Console.WriteLine($"accounts_refreshed: {refresh.AccountsRefreshed}");
    Console.WriteLine($"failed_accounts: {refresh.FailedAccounts}");
    foreach (var account in appConfig.Accounts
                 .Where(account => string.Equals(account.ProviderId, "openai", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(account => account.ManualOrder <= 0 ? int.MaxValue : account.ManualOrder)
                 .ThenBy(account => account.Label, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(
            $"{account.ProviderId}/{account.AccountId}: tier={FormatTier(account)}, 5h={OpenAiQuotaDisplayFormatter.FormatCompactRemaining(account.FiveHourQuota, "5h") ?? "(not available)"}, week={OpenAiQuotaDisplayFormatter.FormatCompactRemaining(account.WeeklyQuota, "week") ?? "(not available)"}, fetched_at={account.OfficialUsageFetchedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "(never)"}, error={account.OfficialUsageError ?? "(none)"}");
    }
}

async Task ProbeCompatibleAsync(string[] commandArgs)
{
    var options = ParseOptions(commandArgs);
    var providerId = options.GetValueOrDefault("provider-id");
    var accountId = options.GetValueOrDefault("account-id");
    var compatibleProviderIds = appConfig.Providers
        .Where(provider => provider.Kind == ProviderKind.OpenAiCompatible)
        .Select(provider => provider.ProviderId)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var accounts = appConfig.Accounts
        .Where(account => compatibleProviderIds.Contains(account.ProviderId))
        .Where(account => string.IsNullOrWhiteSpace(providerId) || string.Equals(account.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        .Where(account => string.IsNullOrWhiteSpace(accountId) || string.Equals(account.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (accounts.Count == 0)
    {
        Console.WriteLine("no compatible accounts to probe");
        return;
    }

    var results = await new CompatibleProviderProbeService(secretStore).ProbeAsync(appConfig, accounts);
    foreach (var result in results)
    {
        var status = result.StatusCode.HasValue ? $"http={result.StatusCode.Value}" : "http=(none)";
        var suggestion = string.IsNullOrWhiteSpace(result.SuggestedBaseUrl) ? "" : $" suggested_base_url={result.SuggestedBaseUrl}";
        Console.WriteLine($"{result.ProviderId}/{result.AccountId}: success={result.Success} {status} elapsed_ms={(int)Math.Round(result.Elapsed.TotalMilliseconds)} message={result.Message}{suggestion}");
    }
}

async Task AddCompatibleAsync(string[] commandArgs)
{
    var options = ParseOptions(commandArgs);
    var providerId = Required(options, "provider-id");
    var accountId = Required(options, "account-id");
    var providerName = options.GetValueOrDefault("name", providerId);
    var codexProviderId = options.GetValueOrDefault("codex-provider-id", "openai");
    var accountLabel = options.GetValueOrDefault("label", accountId);
    var baseUrl = Required(options, "base-url");
    var apiKey = Required(options, "api-key");
    var credentialRef = $"api-key:{providerId}:{accountId}";
    var existingAccount = appConfig.Accounts.FirstOrDefault(a => a.ProviderId == providerId && a.AccountId == accountId);

    await secretStore.WriteSecretAsync(credentialRef, apiKey);

    appConfig = appConfig with
    {
        Providers = Upsert(appConfig.Providers, p => p.ProviderId == providerId, new ProviderDefinition
        {
            ProviderId = providerId,
            CodexProviderId = string.IsNullOrWhiteSpace(codexProviderId) ? null : codexProviderId,
            DisplayName = providerName,
            Kind = ProviderKind.OpenAiCompatible,
            AuthMode = AuthMode.ApiKey,
            BaseUrl = baseUrl,
            WireApi = WireApi.Responses,
            SupportsMultiAccount = true
        }),
        Accounts = Upsert(appConfig.Accounts, a => a.ProviderId == providerId && a.AccountId == accountId, new AccountRecord
        {
            ProviderId = providerId,
            AccountId = accountId,
            Label = accountLabel,
            CredentialRef = credentialRef,
            Status = AccountStatus.Active,
            CreatedAt = existingAccount?.CreatedAt ?? DateTimeOffset.UtcNow,
            ManualOrder = existingAccount?.ManualOrder ?? NextManualOrder(appConfig.Accounts)
        })
    };

    await appConfigStore.SaveAsync(appConfig);
    Console.WriteLine($"added compatible account: {providerId}/{accountId}");
}

async Task ExportAccountsAsync(string[] commandArgs)
{
    var options = ParseOptions(commandArgs);
    var path = Required(options, "path");
    var includeSecrets = options.ContainsKey("include-secrets");
    await new AccountCsvService(secretStore, secretStore).ExportAsync(appConfig, path, new AccountCsvExportOptions(includeSecrets));
    Console.WriteLine(includeSecrets
        ? $"exported accounts with secrets: {path}"
        : $"exported account metadata only: {path}");
}

async Task ImportAccountsAsync(string[] commandArgs)
{
    var options = ParseOptions(commandArgs);
    var path = Required(options, "path");
    var (updatedConfig, result) = await new AccountCsvService(secretStore, secretStore).ImportAsync(appConfig, path);
    appConfig = updatedConfig;
    await appConfigStore.SaveAsync(appConfig);

    Console.WriteLine($"providers_imported: {result.ProvidersImported}");
    Console.WriteLine($"accounts_imported: {result.AccountsImported}");
    Console.WriteLine($"secrets_imported: {result.SecretsImported}");
    foreach (var warning in result.Warnings)
    {
        Console.WriteLine($"warning: {warning}");
    }
}

async Task ExportHistoryAsync(string[] commandArgs)
{
    var options = ParseOptions(commandArgs);
    var path = Required(options, "path");
    var result = await new SessionArchiveService(appPaths)
        .ExportAsync(homeLocator.Resolve(), path, new SessionArchiveExportOptions(!options.ContainsKey("skip-archived")));

    Console.WriteLine($"exported_history: {result.ArchivePath}");
    Console.WriteLine($"sessions: {result.SessionsExported}");
    Console.WriteLine($"archived_sessions: {result.ArchivedSessionsExported}");
    Console.WriteLine($"session_index: {result.SessionIndexExported}");
    Console.WriteLine($"files_skipped: {result.FilesSkipped}");
}

async Task ImportHistoryAsync(string[] commandArgs)
{
    var options = ParseOptions(commandArgs);
    var path = Required(options, "path");
    var result = await new SessionArchiveService(appPaths)
        .ImportAsync(homeLocator.Resolve(), path);

    Console.WriteLine($"sessions_copied: {result.Sessions.Copied}");
    Console.WriteLine($"sessions_skipped: {result.Sessions.Skipped}");
    Console.WriteLine($"sessions_renamed: {result.Sessions.Renamed}");
    Console.WriteLine($"archived_sessions_copied: {result.ArchivedSessions.Copied}");
    Console.WriteLine($"archived_sessions_skipped: {result.ArchivedSessions.Skipped}");
    Console.WriteLine($"archived_sessions_renamed: {result.ArchivedSessions.Renamed}");
    Console.WriteLine($"session_index_merged: {result.SessionIndex.Merged}");
    Console.WriteLine($"session_index_skipped: {result.SessionIndex.Skipped}");
    if (!string.IsNullOrWhiteSpace(result.SessionIndexBackupPath))
    {
        Console.WriteLine($"session_index_backup: {result.SessionIndexBackupPath}");
    }
}

async Task ActivateAsync(string[] commandArgs)
{
    var options = ParseOptions(commandArgs);
    var requestedSelection = new CodexSelection
    {
        ProviderId = Required(options, "provider-id"),
        AccountId = Required(options, "account-id")
    };
    var aggregateDecision = await new OpenAiAggregateGatewayService(appPaths, secretStore, homeLocator)
        .ResolveSelectionAsync(appConfig, requestedSelection);
    var selection = aggregateDecision.ResolvedSelection;

    var transaction = new CodexStateTransaction(appPaths);
    var service = new CodexActivationService(
        homeLocator,
        new CodexConfigStore(),
        new CodexAuthStore(),
        transaction,
        integrityChecker,
        secretStore,
        secretStore);

    var result = await service.ActivateAsync(appConfig, selection);
    var journalMessage = aggregateDecision.WasRerouted
        ? $"{aggregateDecision.Message} {result.Message}"
        : result.Message;
    await new SwitchJournalStore(appPaths.SwitchJournalPath).AppendAsync(result.Selection, result.ValidationPassed ? "ok" : "failed", journalMessage);

    if (!result.ValidationPassed)
    {
        Console.Error.WriteLine(result.Message);
        Environment.ExitCode = 2;
        return;
    }

    var activatedSelection = result.Selection;
    appConfig = appConfig with
    {
        ActiveSelection = activatedSelection,
        Accounts = appConfig.Accounts
            .Select(account => account.ProviderId == activatedSelection.ProviderId && account.AccountId == activatedSelection.AccountId
                ? account with { LastUsedAt = DateTimeOffset.UtcNow }
                : account)
            .ToList()
    };
    await appConfigStore.SaveAsync(appConfig);
    Console.WriteLine("activated");
    if (aggregateDecision.WasRerouted)
    {
        Console.WriteLine($"aggregate_gateway: requested {aggregateDecision.RequestedSelection.ProviderId}/{aggregateDecision.RequestedSelection.AccountId}, resolved {aggregateDecision.ResolvedSelection.ProviderId}/{aggregateDecision.ResolvedSelection.AccountId}");
        Console.WriteLine($"aggregate_gateway_message: {aggregateDecision.Message}");
    }

    var launchEnvironment = await CodexLaunchEnvironmentBuilder.BuildAsync(appConfig, secretStore);
    var launchResult = await new CodexLaunchService().LaunchIfConfiguredAsync(appConfig.Settings, launchEnvironment);
    if (!launchResult.Attempted)
    {
        return;
    }

    if (launchResult.Launched)
    {
        Console.WriteLine($"launch: {launchResult.Message}");
        return;
    }

    Console.Error.WriteLine($"warning: activated, but failed to launch Codex: {launchResult.Message}");
}

async Task ResolveOpenAiAsync(string[] commandArgs)
{
    var options = ParseOptions(commandArgs);
    var providerId = options.GetValueOrDefault("provider-id", "openai");
    var requestedAccountId =
        options.TryGetValue("account-id", out var explicitAccountId) ? explicitAccountId :
        appConfig.ActiveSelection?.ProviderId == providerId ? appConfig.ActiveSelection.AccountId :
        appConfig.Accounts.FirstOrDefault(account => account.ProviderId == providerId)?.AccountId;

    if (string.IsNullOrWhiteSpace(requestedAccountId))
    {
        throw new ArgumentException("No OpenAI account was found. Add at least one OAuth account first.");
    }

    var decision = await new OpenAiAggregateGatewayService(appPaths, secretStore, homeLocator)
        .ResolveSelectionAsync(appConfig, new CodexSelection
        {
            ProviderId = providerId,
            AccountId = requestedAccountId
        });

    Console.WriteLine($"requested: {decision.RequestedSelection.ProviderId}/{decision.RequestedSelection.AccountId}");
    Console.WriteLine($"resolved: {decision.ResolvedSelection.ProviderId}/{decision.ResolvedSelection.AccountId}");
    Console.WriteLine($"rerouted: {decision.WasRerouted}");
    Console.WriteLine($"message: {decision.Message}");
}

async Task ConfigAsync(string[] commandArgs)
{
    var subcommand = commandArgs.FirstOrDefault()?.ToLowerInvariant() ?? "show";
    if (subcommand is "show")
    {
        PrintSettings(appConfig.Settings);
        return;
    }

    if (subcommand is not "set")
    {
        throw new ArgumentException("config supports: show, set");
    }

    var options = ParseOptions(commandArgs.Skip(1).ToArray());
    var settings = appConfig.Settings;
    settings = options.TryGetValue("account-sort-mode", out var sortMode)
        ? settings with { AccountSortMode = ParseEnumOption<AccountSortMode>(sortMode) }
        : settings;
    settings = options.TryGetValue("activation-behavior", out var activationBehavior)
        ? settings with { ActivationBehavior = ParseEnumOption<ActivationBehavior>(activationBehavior) }
        : settings;
    settings = options.TryGetValue("openai-account-mode", out var openAiMode)
        ? settings with { OpenAiAccountMode = ParseEnumOption<OpenAiAccountMode>(openAiMode) }
        : settings;
    settings = options.TryGetValue("codex-desktop-path", out var desktopPath)
        ? settings with { CodexDesktopPath = EmptyToNull(desktopPath) }
        : settings;
    settings = options.TryGetValue("codex-cli-path", out var cliPath)
        ? settings with { CodexCliPath = EmptyToNull(cliPath) }
        : settings;

    appConfig = appConfig with { Settings = settings };
    await appConfigStore.SaveAsync(appConfig);
    PrintSettings(appConfig.Settings);
}

async Task LocateCodexAsync()
{
    var desktop = new CodexDesktopLocator().Locate(appConfig.Settings.CodexDesktopPath);
    var cli = await new CodexCliLocator().LocateAsync(appConfig.Settings.CodexCliPath);

    Console.WriteLine($"desktop: {desktop ?? "(not found)"}");
    Console.WriteLine($"cli: {cli?.Path ?? "(not found)"}");
    Console.WriteLine($"cli_version: {cli?.Version ?? "(unknown)"}");
}

void PrintOAuthUrl()
{
    var client = new OpenAIOAuthClient();
    var flow = client.BeginLogin();
    Console.WriteLine("Open this URL in your browser:");
    Console.WriteLine(flow.AuthorizationUrl);
    Console.WriteLine();
    Console.WriteLine("Keep these values only for this pending login:");
    Console.WriteLine($"state: {flow.State}");
    Console.WriteLine($"code_verifier: {flow.CodeVerifier}");
}

static Dictionary<string, string> ParseOptions(string[] commandArgs)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < commandArgs.Length; i++)
    {
        var key = commandArgs[i];
        if (!key.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        if (i + 1 >= commandArgs.Length || commandArgs[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            var flagName = key[2..];
            if (!IsBooleanFlag(flagName))
            {
                throw new ArgumentException($"Missing value for {key}");
            }

            options[flagName] = "true";
            continue;
        }

        options[key[2..]] = commandArgs[++i];
    }

    return options;
}

static string Required(Dictionary<string, string> options, string name)
    => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing --{name}");

static bool IsBooleanFlag(string name)
    => string.Equals(name, "include-secrets", StringComparison.OrdinalIgnoreCase)
       || string.Equals(name, "skip-archived", StringComparison.OrdinalIgnoreCase);

static List<T> Upsert<T>(IEnumerable<T> source, Func<T, bool> predicate, T item)
{
    var list = source.Where(entry => !predicate(entry)).ToList();
    list.Add(item);
    return list;
}

static int NextManualOrder(IEnumerable<AccountRecord> accounts)
    => accounts.Any() ? accounts.Max(account => account.ManualOrder) + 1 : 1;

static string? EmptyToNull(string value)
    => string.IsNullOrWhiteSpace(value) ? null : value;

static TEnum ParseEnumOption<TEnum>(string value) where TEnum : struct, Enum
{
    var normalized = NormalizeEnum(value);
    foreach (var name in Enum.GetNames<TEnum>())
    {
        if (NormalizeEnum(name) == normalized)
        {
            return Enum.Parse<TEnum>(name);
        }
    }

    throw new ArgumentException($"Invalid {typeof(TEnum).Name}: {value}");
}

static string NormalizeEnum(string value)
    => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

static void PrintSettings(AppSettings settings)
{
    Console.WriteLine($"account_sort_mode: {settings.AccountSortMode}");
    Console.WriteLine($"activation_behavior: {settings.ActivationBehavior}");
    Console.WriteLine($"openai_account_mode: {settings.OpenAiAccountMode}");
    Console.WriteLine($"codex_desktop_path: {settings.CodexDesktopPath ?? ""}");
    Console.WriteLine($"codex_cli_path: {settings.CodexCliPath ?? ""}");
}

static void PrintHelp()
{
    Console.WriteLine("CodexBar.Cli commands:");
    Console.WriteLine("  home");
    Console.WriteLine("  validate");
    Console.WriteLine("  scan [days]");
    Console.WriteLine("  scan-accounts");
    Console.WriteLine("  refresh-openai-usage");
    Console.WriteLine("  probe-compatible [--provider-id <id>] [--account-id <id>]");
    Console.WriteLine("  add-compatible --provider-id <id> [--codex-provider-id <id>] --name <name> --base-url <url> --account-id <id> --label <label> --api-key <key>");
    Console.WriteLine("  export-accounts --path <csv> [--include-secrets]");
    Console.WriteLine("  import-accounts --path <csv>");
    Console.WriteLine("  export-history --path <zip> [--skip-archived]");
    Console.WriteLine("  import-history --path <zip>");
    Console.WriteLine("  activate --provider-id <id> --account-id <id>");
    Console.WriteLine("  resolve-openai [--provider-id <id>] [--account-id <id>]");
    Console.WriteLine("  config show");
    Console.WriteLine("  config set [--account-sort-mode manual|usage] [--activation-behavior write-config-only|launch-new-codex] [--openai-account-mode manual-switch|aggregate-gateway] [--codex-desktop-path <path>] [--codex-cli-path <path>]");
    Console.WriteLine("  locate-codex");
    Console.WriteLine("  oauth-url");
}

static string FormatTier(AccountRecord account)
{
    if (account.Tier != AccountTier.Unknown)
    {
        return account.Tier.ToString().ToLowerInvariant();
    }

    return string.IsNullOrWhiteSpace(account.OfficialPlanTypeRaw)
        ? "unknown"
        : $"unknown({account.OfficialPlanTypeRaw})";
}
