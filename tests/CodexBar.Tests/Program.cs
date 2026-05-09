using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexBar.Auth;
using CodexBar.Tests;
using CodexBar.CodexCompat;
using CodexBar.Core;
using CodexBar.Runtime;

if (args.Length > 0 && string.Equals(args[0], "__single-instance-forward__", StringComparison.Ordinal))
{
    using var secondary = new SingleInstanceService(args[1]);
    Environment.ExitCode = !secondary.IsPrimary && secondary.TryNotifyPrimary(args.Skip(2).ToArray())
        ? 0
        : 1;
    return;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("home locator respects CODEX_HOME", HomeLocatorTest),
    ("paths tolerate duplicate environment key casing", DuplicateEnvironmentKeyTest),
    ("toml editor preserves unknown keys", TomlEditorTest),
    ("compatible activation writes only active state", CompatibleActivationTest),
    ("compatible activation supports custom codex provider alias", CompatibleActivationCustomProviderAliasTest),
    ("compatible activation preserves oauth identity snapshot", CompatibleActivationPreservesOAuthIdentityTest),
    ("oauth activation writes codex-compatible last_refresh", OAuthActivationWritesLastRefreshTest),
    ("oauth activation writes selected workspace account id", OAuthActivationWritesSelectedWorkspaceAccountIdTest),
    ("openai oauth url can restrict a workspace", OpenAiOAuthUrlCanRestrictWorkspaceTest),
    ("openai oauth token response stores chatgpt account id", OpenAiOAuthTokenResponseStoresChatGptAccountIdTest),
    ("transaction rolls back on validation failure", RollbackTest),
    ("manual callback parser accepts URL and code", ManualCallbackParserTest),
    ("openai workspace discovery reads id token organizations", OpenAiWorkspaceDiscoveryReadsIdTokenOrganizationsTest),
    ("openai workspace discovery reads chatgpt account list", OpenAiWorkspaceDiscoveryReadsChatGptAccountListTest),
    ("openai workspace discovery sends codex desktop account headers", OpenAiWorkspaceDiscoverySendsCodexDesktopAccountHeadersTest),
    ("openai workspace discovery uses selection hint for save", OpenAiWorkspaceDiscoveryUsesSelectionHintForSaveTest),
    ("openai workspace discovery prefers chatgpt accounts over org ids", OpenAiWorkspaceDiscoveryPrefersChatGptAccountsOverOrgIdsTest),
    ("openai workspace discovery ignores org ids when chatgpt account list is forbidden", OpenAiWorkspaceDiscoveryIgnoresOrgIdsWhenChatGptAccountListForbiddenTest),
    ("openai oauth account key avoids shared account id collisions", OpenAiOAuthAccountKeyAvoidsSharedAccountIdCollisionTest),
    ("openai oauth account key reuses matching legacy records", OpenAiOAuthAccountKeyReusesMatchingLegacyRecordTest),
    ("openai oauth account key treats subject fallback as account id", OpenAiOAuthAccountKeyTreatsSubjectFallbackAsAccountIdTest),
    ("openai oauth account key separates same-login workspaces", OpenAiOAuthAccountKeySeparatesSameLoginWorkspacesTest),
    ("trusted frontend cors only allows known loopback origins", ApiRegressionTests.TrustedFrontendCorsTest),
    ("oauth manual fallback prefers current input over captured tokens", ApiRegressionTests.OAuthManualFallbackUsesCurrentInputTest),
    ("oauth save success resets captured state for the next login attempt", ApiRegressionTests.OAuthSuccessfulSaveResetsAttemptStateTest),
    ("oauth flow rotation cancels stale loopback listener before restarting localhost capture", ApiRegressionTests.OAuthFlowRotationCancelsPendingLoopbackListenerTest),
    ("oauth start passes allowed workspace id", ApiRegressionTests.OAuthStartPassesAllowedWorkspaceIdTest),
    ("oauth workspace selection restarts login without manual id", ApiRegressionTests.OAuthWorkspaceSelectionRestartsLoginWithoutManualIdTest),
    ("oauth complete rejects allowed workspace mismatch", ApiRegressionTests.OAuthCompleteRejectsAllowedWorkspaceMismatchTest),
    ("account reorder requires complete payload coverage", ApiRegressionTests.ReorderAccountsRejectsPartialPayloadTest),
    ("account reorder accepts full payload and preserves all accounts", ApiRegressionTests.ReorderAccountsAcceptsFullPayloadTest),
    ("usage scanner reads shared history without writes", UsageScannerTest),
    ("usage scanner tolerates locked active session files", UsageScannerLockedFileTest),
    ("usage scanner uses cumulative token snapshots without double counting", UsageScannerUsesCumulativeTokenSnapshotsTest),
    ("usage attribution respects compatible token reset marker", UsageAttributionRespectsCompatibleTokenResetTest),
    ("compatible provider probe suggests missing v1 path", CompatibleProviderProbeSuggestsV1Test),
    ("usage attribution maps sessions by switch intervals", UsageAttributionTest),
    ("switch journal renames provider ids", SwitchJournalRenameProviderTest),
    ("aggregate gateway reroutes openai to lower-usage account", AggregateGatewayRerouteTest),
    ("aggregate gateway prefers lower official quota pressure over local history", AggregateGatewayPrefersOfficialQuotaTest),
    ("aggregate gateway avoids same quota scope when routing for capacity", AggregateGatewayAvoidsSameQuotaScopeTest),
    ("aggregate gateway leaves manual switch selections unchanged", AggregateGatewayManualModeTest),
    ("aggregate gateway avoids accounts that need reauth when a healthy account exists", AggregateGatewayAvoidsNeedsReauthTest),
    ("desktop locator prefers desktop inferred from current cli path", DesktopLocatorPrefersCliInferredDesktopTest),
    ("desktop locator prefers latest packaged Codex version", DesktopLocatorPrefersLatestPackagedVersionTest),
    ("desktop locator detects packaged Codex without configured path", DesktopLocatorDetectsPackagedVersionWithoutConfiguredPathTest),
    ("startup command resolver keeps single-instance launch semantics", StartupCommandResolverTest),
    ("single instance service forwards args to primary process", SingleInstanceForwardingTest),
    ("launch service skips process start when write only", LaunchServiceWriteOnlyTest),
    ("launch service starts desktop with clean environment", LaunchServiceDesktopTest),
    ("compatible launch injects active API key", CompatibleLaunchInjectsActiveApiKeyTest),
    ("desktop process service detects desktop without matching cli", CodexDesktopProcessServiceDetectsDesktopTest),
    ("desktop process service requests normal close only", CodexDesktopProcessServiceNormalCloseTest),
    ("desktop process service refuses unclosable desktop without kill", CodexDesktopProcessServiceNoSilentKillTest),
    ("desktop process service terminates only after confirmation path", CodexDesktopProcessServiceTerminateAfterConfirmationTest),
    ("desktop process service falls back when terminate no-ops", CodexDesktopProcessServiceForceTerminateFallbackTest),
    ("desktop process service force terminates tracked pids only", CodexDesktopProcessServiceForceTerminateTrackedPidsOnlyTest),
    ("app config persists manual account order", AppConfigManualOrderTest),
    ("app config persists overlay startup preference", AppConfigOverlayStartupPreferenceTest),
    ("app config persists restart confirmation suppression", AppConfigRestartConfirmationSuppressionTest),
    ("app config persists account card density preference", AppConfigAccountCardDensityPreferenceTest),
    ("quota formatter shows remaining quota and 5h reset time as hh:mm", QuotaFormatterFiveHourResetTest),
    ("quota formatter shows weekly reset as date unless within 24h", QuotaFormatterWeeklyResetTest),
    ("quota formatter supports inline flyout labels", QuotaFormatterInlineLabelTest),
    ("official OpenAI usage refresh maps plan and quota windows", OfficialOpenAiUsageRefreshTest),
    ("official OpenAI usage refresh sends workspace header and stores quota scope", OfficialOpenAiUsageRefreshWorkspaceScopeTest),
    ("official OpenAI usage refresh maps team plan", OfficialOpenAiUsageRefreshMapsTeamPlanTest),
    ("openai account display shows team plan and workspace", OpenAiAccountDisplayShowsTeamPlanAndWorkspaceTest),
    ("official OpenAI usage refresh marks unauthorized accounts for reauth", OfficialOpenAiUsageUnauthorizedTest),
    ("session archive exports and imports shared history only", SessionArchiveExportImportTest),
    ("session archive imports conflicts without overwriting", SessionArchiveConflictImportTest),
    ("session archive rejects unsafe zip paths", SessionArchiveUnsafePathTest),
    ("account csv imports compatible secrets", AccountCsvCompatibleSecretTest),
    ("account csv preserves oauth workspace metadata", AccountCsvOAuthWorkspaceMetadataTest),
    ("account csv exports oauth metadata without secrets by default", AccountCsvOAuthSecretSafetyTest),
    ("update semver comparison handles stable and prerelease versions", UpdateSemverComparisonTest),
    ("update check ignores draft and prerelease releases", UpdateCheckIgnoresDraftAndPrereleaseTest),
    ("update check selects matching portable zip asset", UpdateCheckSelectsPortableZipAssetTest),
    ("update checksum verification accepts matching official checksum", UpdateChecksumMatchTest),
    ("update checksum verification rejects mismatched official checksum", UpdateChecksumMismatchTest),
    ("update check reports network failures readably", UpdateCheckNetworkFailureTest),
    ("update launcher refuses dangerous target directories", UpdateLauncherDangerousTargetDirectoryTest),
    ("updater helper refuses windows system directories", UpdaterHelperRefusesWindowsSystemDirectoryTest),
    ("update launcher arguments avoid codex history and credential paths", UpdateLauncherArgumentsAvoidSensitivePathsTest),
    ("update check skips current or older remote versions", UpdateCheckSkipsCurrentOrOlderVersionsTest)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failed > 0)
{
    Environment.ExitCode = 1;
}

static Task HomeLocatorTest()
{
    using var temp = TempDir.Create();
    var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = temp.Path,
        ["USERPROFILE"] = Path.Combine(temp.Path, "profile")
    };
    var home = new CodexHomeLocator().Resolve(env);
    AssertEqual(Path.GetFullPath(temp.Path), home.RootPath);
    AssertTrue(home.IsExplicitlyOverridden);
    AssertEqual(Path.Combine(home.RootPath, "sessions"), home.SessionsPath);
    return Task.CompletedTask;
}

static Task DuplicateEnvironmentKeyTest()
{
    using var temp = TempDir.Create();
    var env = new Dictionary<string, string?>
    {
        ["USERPROFILE"] = temp.Path,
        ["Path"] = "one",
        ["PATH"] = "two"
    };

    var appPaths = AppPaths.Resolve(env);
    var home = new CodexHomeLocator().Resolve(env);

    AssertEqual(Path.Combine(temp.Path, ".codexbar"), appPaths.AppRoot);
    AssertEqual(Path.Combine(temp.Path, ".codex"), home.RootPath);
    return Task.CompletedTask;
}

static Task StartupCommandResolverTest()
{
    AssertEqual(StartupCommand.Open, StartupCommandResolver.Resolve([]));
    AssertEqual(StartupCommand.Open, StartupCommandResolver.Resolve(["--open"]));
    AssertEqual(StartupCommand.Overlay, StartupCommandResolver.Resolve(["--overlay"]));
    AssertEqual(StartupCommand.Settings, StartupCommandResolver.Resolve(["--settings"]));
    AssertEqual(StartupCommand.TrayOnly, StartupCommandResolver.Resolve(["--tray-only"]));
    AssertEqual(StartupCommand.Settings, StartupCommandResolver.Resolve(["--overlay", "--settings"]));
    return Task.CompletedTask;
}

static async Task SingleInstanceForwardingTest()
{
    var name = $"Local\\CodexBarWinTest-{Guid.NewGuid():N}";
    using var primary = new SingleInstanceService(name);
    AssertTrue(primary.IsPrimary);

    var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    primary.ArgumentsReceived += args => received.TrySetResult(args);

    var helper = new ProcessStartInfo
    {
        FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Missing host process path."),
        UseShellExecute = false
    };
    if (string.Equals(Path.GetFileName(helper.FileName), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
    {
        helper.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    }

    helper.ArgumentList.Add("__single-instance-forward__");
    helper.ArgumentList.Add(name);
    helper.ArgumentList.Add("--settings");

    using var process = Process.Start(helper) ?? throw new InvalidOperationException("Failed to start helper process.");
    await process.WaitForExitAsync();
    AssertEqual(0, process.ExitCode);

    var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
    AssertTrue(ReferenceEquals(completed, received.Task), "primary instance did not receive forwarded args");
    AssertSequenceEqual(["--settings"], await received.Task);
}

static Task TomlEditorTest()
{
    var doc = CodexConfigDocument.Parse("""
        custom_key = "keep"
        model = "old"

        [model_providers.openai]
        stale = true

        [other]
        value = 1
        """);
    doc.SetString("model", "gpt-5");
    doc.SetString("model_provider", "openai");
    doc.RemoveSections("model_providers.openai");
    var text = doc.ToString();
    AssertContains(text, "custom_key = \"keep\"");
    AssertContains(text, "model = \"gpt-5\"");
    AssertContains(text, "model_provider = \"openai\"");
    AssertDoesNotContain(text, "model_providers.openai");
    AssertContains(text, "[other]");
    return Task.CompletedTask;
}

static async Task CompatibleActivationTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    Directory.CreateDirectory(Path.Combine(codexHome, "sessions"));
    Directory.CreateDirectory(Path.Combine(codexHome, "archived_sessions"));
    var sessionPath = Path.Combine(codexHome, "sessions", "session.jsonl");
    await File.WriteAllTextAsync(sessionPath, "{\"type\":\"message\"}\n");
    await File.WriteAllTextAsync(Path.Combine(codexHome, "config.toml"), "unknown = \"preserve\"\nmodel = \"old\"\n");
    await File.WriteAllTextAsync(Path.Combine(codexHome, "auth.json"), "{\"auth_mode\":\"chatgpt\"}\n");

    var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = temp.Path
    });
    var secrets = new InMemorySecretStore();
    await secrets.WriteSecretAsync("api-key:test:default", "sk-test");
    var config = new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "test",
                DisplayName = "Test API",
                Kind = ProviderKind.OpenAiCompatible,
                AuthMode = AuthMode.ApiKey,
                BaseUrl = "https://example.test/v1"
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "test",
                AccountId = "default",
                Label = "Default",
                CredentialRef = "api-key:test:default"
            }
        ]
    };

    var selection = new CodexSelection { ProviderId = "test", AccountId = "default" };
    var result = await NewActivationService(appPaths, secrets).ActivateAsync(config, selection, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    });

    AssertTrue(result.ValidationPassed, result.Message);
    var configText = await File.ReadAllTextAsync(Path.Combine(codexHome, "config.toml"));
    var authText = await File.ReadAllTextAsync(Path.Combine(codexHome, "auth.json"));
    AssertContains(configText, "unknown = \"preserve\"");
    AssertContains(configText, "model_provider = \"openai\"");
    AssertContains(configText, "openai_base_url = \"https://example.test/v1\"");
    AssertDoesNotContain(configText, "[model_providers.openai]");
    AssertDoesNotContain(configText, "[model_providers.test]");
    AssertContains(authText, "\"auth_mode\": \"apikey\"");
    AssertContains(authText, "\"OPENAI_API_KEY\": \"sk-test\"");
    AssertEqual("{\"type\":\"message\"}\n", await File.ReadAllTextAsync(sessionPath));
}

static async Task CompatibleActivationCustomProviderAliasTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    Directory.CreateDirectory(codexHome);
    await File.WriteAllTextAsync(Path.Combine(codexHome, "config.toml"), "model = \"old\"\n");
    await File.WriteAllTextAsync(Path.Combine(codexHome, "auth.json"), "{\"auth_mode\":\"chatgpt\"}\n");

    var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = temp.Path
    });
    var secrets = new InMemorySecretStore();
    await secrets.WriteSecretAsync("api-key:test:default", "sk-test");
    var config = new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "test",
                CodexProviderId = "openai-custom",
                DisplayName = "Test API",
                Kind = ProviderKind.OpenAiCompatible,
                AuthMode = AuthMode.ApiKey,
                BaseUrl = "https://example.test/v1"
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "test",
                AccountId = "default",
                Label = "Default",
                CredentialRef = "api-key:test:default"
            }
        ]
    };

    var result = await NewActivationService(appPaths, secrets).ActivateAsync(config, new CodexSelection
    {
        ProviderId = "test",
        AccountId = "default"
    }, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    });

    AssertTrue(result.ValidationPassed, result.Message);
    var configText = await File.ReadAllTextAsync(Path.Combine(codexHome, "config.toml"));
    AssertContains(configText, "model_provider = \"openai-custom\"");
    AssertContains(configText, "[model_providers.openai-custom]");
    AssertContains(configText, "base_url = \"https://example.test/v1\"");
    AssertDoesNotContain(configText, "openai_base_url");
}

static async Task CompatibleActivationPreservesOAuthIdentityTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    Directory.CreateDirectory(codexHome);
    var lastRefresh = DateTimeOffset.Parse("2026-04-17T01:00:00Z");
    await File.WriteAllTextAsync(Path.Combine(codexHome, "config.toml"), "model = \"old\"\n");
    await File.WriteAllTextAsync(Path.Combine(codexHome, "auth.json"), $$"""
        {
          "auth_mode": "chatgpt",
          "OPENAI_API_KEY": null,
          "tokens": {
            "access_token": "keep-access",
            "refresh_token": "keep-refresh",
            "id_token": "keep-id",
            "last_refresh": "{{lastRefresh:O}}"
          },
          "last_refresh": "{{lastRefresh:O}}"
        }
        """);

    var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = temp.Path
    });
    var secrets = new InMemorySecretStore();
    await secrets.WriteSecretAsync("api-key:test:default", "sk-test");
    var config = new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "test",
                DisplayName = "Test API",
                Kind = ProviderKind.OpenAiCompatible,
                AuthMode = AuthMode.ApiKey,
                BaseUrl = "https://example.test/v1"
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "test",
                AccountId = "default",
                Label = "Default",
                CredentialRef = "api-key:test:default"
            }
        ]
    };

    await NewActivationService(appPaths, secrets).ActivateAsync(config, new CodexSelection
    {
        ProviderId = "test",
        AccountId = "default"
    }, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    });

    using var auth = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(codexHome, "auth.json")));
    var root = auth.RootElement;
    AssertEqual("apikey", root.GetProperty("auth_mode").GetString());
    AssertEqual("sk-test", root.GetProperty("OPENAI_API_KEY").GetString());
    AssertTrue(root.TryGetProperty("tokens", out var tokens));
    AssertEqual("keep-access", tokens.GetProperty("access_token").GetString());
    AssertEqual(lastRefresh, root.GetProperty("last_refresh").GetDateTimeOffset());
}

static async Task OAuthActivationWritesLastRefreshTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    Directory.CreateDirectory(codexHome);

    var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = temp.Path
    });
    var secrets = new InMemorySecretStore();
    var lastRefresh = DateTimeOffset.Parse("2026-04-17T01:00:00Z");
    await secrets.WriteTokensAsync("oauth:openai:acct", new OAuthTokens
    {
        AccessToken = "access-token",
        RefreshToken = "refresh-token",
        IdToken = "id-token",
        AccountId = "account-id",
        LastRefresh = lastRefresh
    });
    var config = new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct",
                Label = "Acct",
                CredentialRef = "oauth:openai:acct"
            }
        ]
    };

    var result = await NewActivationService(appPaths, secrets).ActivateAsync(config, new CodexSelection
    {
        ProviderId = "openai",
        AccountId = "acct"
    }, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    });

    AssertTrue(result.ValidationPassed, result.Message);
    using var auth = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(codexHome, "auth.json")));
    var root = auth.RootElement;
    AssertEqual("chatgpt", root.GetProperty("auth_mode").GetString());
    AssertTrue(root.TryGetProperty("tokens", out var tokens));
    AssertEqual("access-token", tokens.GetProperty("access_token").GetString());
    AssertTrue(root.TryGetProperty("last_refresh", out var writtenLastRefresh));
    AssertEqual(lastRefresh, writtenLastRefresh.GetDateTimeOffset());
}

static async Task OAuthActivationWritesSelectedWorkspaceAccountIdTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    Directory.CreateDirectory(codexHome);

    var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = temp.Path
    });
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:acct", new OAuthTokens
    {
        AccessToken = "access-token",
        RefreshToken = "refresh-token",
        IdToken = "id-token",
        AccountId = "workspace-old"
    });
    var config = new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct",
                Label = "Team Space",
                OpenAiAccountId = "workspace-team",
                WorkspaceId = "workspace-team",
                WorkspaceName = "Team Space",
                CredentialRef = "oauth:openai:acct"
            }
        ]
    };

    var result = await NewActivationService(appPaths, secrets).ActivateAsync(config, new CodexSelection
    {
        ProviderId = "openai",
        AccountId = "acct"
    }, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    });

    AssertTrue(result.ValidationPassed, result.Message);
    using var auth = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(codexHome, "auth.json")));
    var tokens = auth.RootElement.GetProperty("tokens");
    AssertEqual("workspace-team", tokens.GetProperty("account_id").GetString());
}

static Task OpenAiOAuthUrlCanRestrictWorkspaceTest()
{
    var client = new OpenAIOAuthClient();
    var flow = client.BeginLogin(new OAuthOptions
    {
        AuthorizationEndpoint = new Uri("https://auth.example.test/oauth/authorize", UriKind.Absolute),
        AllowedWorkspaceId = "workspace-team"
    });

    AssertContains(flow.AuthorizationUrl.ToString(), "allowed_workspace_id=workspace-team");
    return Task.CompletedTask;
}

static async Task OpenAiOAuthTokenResponseStoresChatGptAccountIdTest()
{
    var handler = new StubHttpMessageHandler(request =>
    {
        AssertEqual(HttpMethod.Post, request.Method);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "access_token": "access-token",
                  "refresh_token": "refresh-token",
                  "id_token": "{{CreateUnsignedJwt("""
                    {
                      "https://api.openai.com/auth": {
                        "chatgpt_account_id": "workspace-team"
                      }
                    }
                    """)}}"
                }
                """, Encoding.UTF8, "application/json")
        };
    });
    var client = new OpenAIOAuthClient(new HttpClient(handler));
    var tokens = await client.ExchangeCodeAsync(new OAuthPendingFlow
    {
        AuthorizationUrl = new Uri("https://auth.example.test/oauth/authorize", UriKind.Absolute),
        State = "state",
        CodeVerifier = "verifier",
        RedirectUri = new Uri("http://localhost:1455/auth/callback", UriKind.Absolute),
        Options = new OAuthOptions
        {
            TokenEndpoint = new Uri("https://auth.example.test/oauth/token", UriKind.Absolute)
        }
    }, "code");

    AssertEqual("workspace-team", tokens.AccountId);
}

static async Task RollbackTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    Directory.CreateDirectory(codexHome);
    var configPath = Path.Combine(codexHome, "config.toml");
    var authPath = Path.Combine(codexHome, "auth.json");
    await File.WriteAllTextAsync(configPath, "model = \"before\"\n");
    await File.WriteAllTextAsync(authPath, "{\"before\":true}\n");

    var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = temp.Path
    });
    var home = new CodexHomeLocator().Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    });
    var transaction = new CodexStateTransaction(appPaths);
    var result = await transaction.WriteActivationAsync(
        home,
        new CodexSelection { ProviderId = "x", AccountId = "y" },
        "model = \"after\"\n",
        "{\"after\":true}\n",
        () => new ValidationReport { Errors = ["forced failure"] });

    AssertTrue(result.RollbackApplied);
    AssertEqual("model = \"before\"\n", await File.ReadAllTextAsync(configPath));
    AssertEqual("{\"before\":true}\n", await File.ReadAllTextAsync(authPath));
}

static Task ManualCallbackParserTest()
{
    var full = ManualCallbackParser.Parse("http://localhost:1455/auth/callback?code=abc&state=xyz");
    AssertEqual("abc", full.Code);
    AssertEqual("xyz", full.State);
    AssertTrue(full.WasFullCallbackUrl);

    var codeOnly = ManualCallbackParser.Parse("abc123");
    AssertEqual("abc123", codeOnly.Code);
    AssertTrue(codeOnly.State is null);
    return Task.CompletedTask;
}

static async Task UsageScannerTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    var sessions = Path.Combine(codexHome, "sessions");
    var archived = Path.Combine(codexHome, "archived_sessions");
    Directory.CreateDirectory(sessions);
    Directory.CreateDirectory(archived);
    await File.WriteAllTextAsync(Path.Combine(sessions, "a.jsonl"), "{\"timestamp\":\"2026-04-15T00:00:00Z\",\"usage\":{\"input_tokens\":10,\"output_tokens\":5,\"cached_input_tokens\":2}}\n");
    var home = new CodexHomeLocator().Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    });
    var summary = await new UsageScanner().ScanAsync(home, DateTimeOffset.Parse("2026-04-01T00:00:00Z"), DateTimeOffset.Parse("2026-04-30T00:00:00Z"));
    AssertEqual(10L, summary.InputTokens);
    AssertEqual(5L, summary.OutputTokens);
    AssertEqual(2L, summary.CachedInputTokens);
    AssertEqual(1, summary.EventsScanned);
}

static async Task UsageScannerLockedFileTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    var sessions = Path.Combine(codexHome, "sessions");
    Directory.CreateDirectory(sessions);
    await File.WriteAllTextAsync(Path.Combine(sessions, "readable.jsonl"), "{\"timestamp\":\"2026-04-15T00:00:00Z\",\"usage\":{\"input_tokens\":4,\"output_tokens\":3,\"cached_input_tokens\":1}}\n");
    var lockedPath = Path.Combine(sessions, "active.jsonl");
    await File.WriteAllTextAsync(lockedPath, "{\"timestamp\":\"2026-04-15T00:00:00Z\",\"usage\":{\"input_tokens\":100,\"output_tokens\":50,\"cached_input_tokens\":0}}\n");

    using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    var home = new CodexHomeLocator().Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    });

    var summary = await new UsageScanner().ScanAsync(home, DateTimeOffset.Parse("2026-04-01T00:00:00Z"), DateTimeOffset.Parse("2026-04-30T00:00:00Z"));
    AssertEqual(4L, summary.InputTokens);
    AssertEqual(3L, summary.OutputTokens);
    AssertEqual(1L, summary.CachedInputTokens);
    AssertEqual(1, summary.EventsScanned);
}

static async Task UsageScannerUsesCumulativeTokenSnapshotsTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    var sessions = Path.Combine(codexHome, "sessions");
    Directory.CreateDirectory(sessions);

    var sessionPath = Path.Combine(sessions, "new-format.jsonl");
    await File.WriteAllTextAsync(sessionPath, string.Join('\n',
    [
        "{\"type\":\"session_meta\",\"timestamp\":\"2026-04-15T00:00:00Z\",\"payload\":{\"id\":\"session-1\",\"timestamp\":\"2026-04-15T00:00:00Z\"}}",
        "{\"type\":\"event_msg\",\"timestamp\":\"2026-04-15T00:01:00Z\",\"payload\":{\"info\":{\"total_token_usage\":{\"input_tokens\":100000,\"output_tokens\":50000,\"cached_input_tokens\":10000},\"last_token_usage\":{\"input_tokens\":1,\"output_tokens\":2,\"cached_input_tokens\":3}}}}",
        "{\"type\":\"event_msg\",\"timestamp\":\"2026-04-15T00:02:00Z\",\"payload\":{\"info\":{\"total_token_usage\":{\"input_tokens\":220000,\"output_tokens\":110000,\"cached_input_tokens\":20000},\"last_token_usage\":{\"input_tokens\":4,\"output_tokens\":5,\"cached_input_tokens\":6}}}}",
        "{\"type\":\"event_msg\",\"timestamp\":\"2026-04-15T00:03:00Z\",\"payload\":{\"info\":{\"total_token_usage\":{\"input_tokens\":340000,\"output_tokens\":170000,\"cached_input_tokens\":30000},\"last_token_usage\":{\"input_tokens\":7,\"output_tokens\":8,\"cached_input_tokens\":9}}}}"
    ]) + "\n");

    var home = new CodexHomeLocator().Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    });

    var summary = await new UsageScanner().ScanAsync(home, DateTimeOffset.Parse("2026-04-01T00:00:00Z"), DateTimeOffset.Parse("2026-04-30T00:00:00Z"));
    AssertEqual(340000L, summary.InputTokens);
    AssertEqual(170000L, summary.OutputTokens);
    AssertEqual(30000L, summary.CachedInputTokens);
    AssertEqual(3, summary.EventsScanned);
}

static async Task CompatibleProviderProbeSuggestsV1Test()
{
    var secrets = new InMemorySecretStore();
    await secrets.WriteSecretAsync("api-key:compatible:default", "sk-test");
    var config = new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "compatible",
                DisplayName = "Compatible",
                Kind = ProviderKind.OpenAiCompatible,
                BaseUrl = "https://gateway.example"
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "compatible",
                AccountId = "default",
                Label = "Default",
                CredentialRef = "api-key:compatible:default"
            }
        ]
    };

    var handler = new StubHttpMessageHandler(request =>
    {
        AssertEqual("Bearer", request.Headers.Authorization?.Scheme);
        AssertEqual("sk-test", request.Headers.Authorization?.Parameter);
        return request.RequestUri?.AbsoluteUri == "https://gateway.example/v1/models"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    var result = (await new CompatibleProviderProbeService(secrets, new HttpClient(handler))
        .ProbeAsync(config, config.Accounts)).Single();

    AssertTrue(!result.Success);
    AssertEqual(404, result.StatusCode);
    AssertEqual("https://gateway.example/v1", result.SuggestedBaseUrl);
}

static async Task UsageAttributionTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    var sessions = Path.Combine(codexHome, "sessions");
    Directory.CreateDirectory(sessions);

    await File.WriteAllTextAsync(Path.Combine(sessions, "before.jsonl"), """
        {"timestamp":"2026-03-31T23:40:00Z","type":"session_meta","payload":{"timestamp":"2026-03-31T23:40:00Z"}}
        {"timestamp":"2026-03-31T23:41:00Z","usage":{"input_tokens":1,"output_tokens":1,"cached_input_tokens":0}}
        """);
    await File.WriteAllTextAsync(Path.Combine(sessions, "a.jsonl"), """
        {"timestamp":"2026-04-01T00:10:00Z","type":"session_meta","payload":{"timestamp":"2026-04-01T00:10:00Z"}}
        {"timestamp":"2026-04-01T00:11:00Z","usage":{"input_tokens":10,"output_tokens":5,"cached_input_tokens":2}}
        """);
    await File.WriteAllTextAsync(Path.Combine(sessions, "b.jsonl"), """
        {"timestamp":"2026-04-01T01:10:00Z","type":"session_meta","payload":{"timestamp":"2026-04-01T01:10:00Z"}}
        {"timestamp":"2026-04-01T01:11:00Z","usage":{"input_tokens":20,"output_tokens":10,"cached_input_tokens":3}}
        """);

    var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    };
    var appPaths = AppPaths.Resolve(env);
    var journal = new SwitchJournalStore(appPaths.SwitchJournalPath);
    await journal.AppendEntryAsync(new SwitchJournalEntry
    {
        Timestamp = DateTimeOffset.Parse("2026-04-01T00:00:00Z"),
        Selection = new CodexSelection { ProviderId = "openai", AccountId = "a", SelectedAt = DateTimeOffset.Parse("2026-04-01T00:00:00Z") },
        Status = "ok",
        Message = "activated a"
    });
    await journal.AppendEntryAsync(new SwitchJournalEntry
    {
        Timestamp = DateTimeOffset.Parse("2026-04-01T01:00:00Z"),
        Selection = new CodexSelection { ProviderId = "openai", AccountId = "b", SelectedAt = DateTimeOffset.Parse("2026-04-01T01:00:00Z") },
        Status = "ok",
        Message = "activated b"
    });

    var config = new AppConfig
    {
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "a",
                Label = "A",
                CredentialRef = "oauth:openai:a"
            },
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "b",
                Label = "B",
                CredentialRef = "oauth:openai:b"
            }
        ]
    };

    var dashboard = await new UsageAttributionService(new UsageScanner(), journal)
        .BuildDashboardAsync(config, new CodexHomeLocator().Resolve(env), DateTimeOffset.Parse("2026-04-02T00:00:00Z"));

    AssertEqual(47L, dashboard.Last7Days.TotalTokens);
    AssertEqual(47L, dashboard.Lifetime.TotalTokens);
    AssertEqual(1, dashboard.UnattributedSessions);
    AssertEqual(15L, dashboard.Accounts.Single(account => account.AccountId == "a").Last7Days.TotalTokens);
    AssertEqual(30L, dashboard.Accounts.Single(account => account.AccountId == "b").Last7Days.TotalTokens);
    AssertEqual(15L, dashboard.Accounts.Single(account => account.AccountId == "a").Lifetime.TotalTokens);
    AssertEqual(30L, dashboard.Accounts.Single(account => account.AccountId == "b").Lifetime.TotalTokens);
}

static async Task UsageAttributionRespectsCompatibleTokenResetTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    var sessions = Path.Combine(codexHome, "sessions");
    Directory.CreateDirectory(sessions);

    await File.WriteAllTextAsync(Path.Combine(sessions, "before-reset.jsonl"), """
        {"timestamp":"2026-04-01T00:10:00Z","type":"session_meta","payload":{"timestamp":"2026-04-01T00:10:00Z"}}
        {"timestamp":"2026-04-01T00:11:00Z","usage":{"input_tokens":100,"output_tokens":20,"cached_input_tokens":0}}
        """);
    await File.WriteAllTextAsync(Path.Combine(sessions, "after-reset.jsonl"), """
        {"timestamp":"2026-04-01T02:10:00Z","type":"session_meta","payload":{"timestamp":"2026-04-01T02:10:00Z"}}
        {"timestamp":"2026-04-01T02:11:00Z","usage":{"input_tokens":10,"output_tokens":5,"cached_input_tokens":1}}
        """);

    var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    };
    var appPaths = AppPaths.Resolve(env);
    var journal = new SwitchJournalStore(appPaths.SwitchJournalPath);
    await journal.AppendEntryAsync(new SwitchJournalEntry
    {
        Timestamp = DateTimeOffset.Parse("2026-04-01T00:00:00Z"),
        Selection = new CodexSelection { ProviderId = "compatible", AccountId = "default", SelectedAt = DateTimeOffset.Parse("2026-04-01T00:00:00Z") },
        Status = "ok",
        Message = "activated compatible"
    });

    var config = new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "compatible",
                DisplayName = "Compatible",
                Kind = ProviderKind.OpenAiCompatible,
                AuthMode = AuthMode.ApiKey
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "compatible",
                AccountId = "default",
                Label = "Default",
                CredentialRef = "api-key:compatible:default",
                TokenCountResetAt = DateTimeOffset.Parse("2026-04-01T01:00:00Z")
            }
        ]
    };

    var dashboard = await new UsageAttributionService(new UsageScanner(), journal)
        .BuildDashboardAsync(config, new CodexHomeLocator().Resolve(env), DateTimeOffset.Parse("2026-04-02T00:00:00Z"));
    var account = dashboard.Accounts.Single(item => item.ProviderId == "compatible" && item.AccountId == "default");

    AssertEqual(15L, account.Last7Days.TotalTokens);
    AssertEqual(15L, account.Lifetime.TotalTokens);
}

static async Task SwitchJournalRenameProviderTest()
{
    using var temp = TempDir.Create();
    var path = Path.Combine(temp.Path, "switch-journal.jsonl");
    var store = new SwitchJournalStore(path);
    await store.AppendAsync(new CodexSelection
    {
        ProviderId = "compatible",
        AccountId = "default"
    }, "ok", "Activation written.");
    await store.AppendAsync(new CodexSelection
    {
        ProviderId = "openai",
        AccountId = "acct"
    }, "ok", "Activation written.");

    await store.RenameProviderAsync("compatible", "gateway");

    var entries = await store.ReadAllAsync();
    AssertEqual("gateway", entries[0].Selection.ProviderId);
    AssertEqual("openai", entries[1].Selection.ProviderId);
}

static async Task AggregateGatewayRerouteTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    var sessions = Path.Combine(codexHome, "sessions");
    Directory.CreateDirectory(sessions);
    var selectedAt = DateTimeOffset.Now.AddMinutes(-30);
    var sessionStartedAt = selectedAt.AddMinutes(10);
    var usageAt = sessionStartedAt.AddMinutes(1);

    await File.WriteAllTextAsync(Path.Combine(sessions, "busy.jsonl"),
        $$$"""
        {"timestamp":"{{{sessionStartedAt:O}}}","type":"session_meta","payload":{"timestamp":"{{{sessionStartedAt:O}}}"}}
        {"timestamp":"{{{usageAt:O}}}","usage":{"input_tokens":100,"output_tokens":20,"cached_input_tokens":0}}
        """);

    var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    };
    var appPaths = AppPaths.Resolve(env);
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:busy", new OAuthTokens { AccessToken = "busy-access", RefreshToken = "busy-refresh", IdToken = "busy-id" });
    await secrets.WriteTokensAsync("oauth:openai:idle", new OAuthTokens { AccessToken = "idle-access", RefreshToken = "idle-refresh", IdToken = "idle-id" });

    var journal = new SwitchJournalStore(appPaths.SwitchJournalPath);
    await journal.AppendEntryAsync(new SwitchJournalEntry
    {
        Timestamp = selectedAt,
        Selection = new CodexSelection { ProviderId = "openai", AccountId = "busy", SelectedAt = selectedAt },
        Status = "ok",
        Message = "activated busy"
    });

    var config = new AppConfig
    {
        Settings = new AppSettings
        {
            OpenAiAccountMode = OpenAiAccountMode.AggregateGateway
        },
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "busy",
                Label = "Busy",
                CredentialRef = "oauth:openai:busy",
                ManualOrder = 1
            },
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "idle",
                Label = "Idle",
                CredentialRef = "oauth:openai:idle",
                ManualOrder = 2
            }
        ]
    };

    var decision = await new OpenAiAggregateGatewayService(appPaths, secrets)
        .ResolveSelectionAsync(config, new CodexSelection { ProviderId = "openai", AccountId = "busy" }, env);

    AssertTrue(decision.WasRerouted);
    AssertEqual("idle", decision.ResolvedSelection.AccountId);
}

static async Task AggregateGatewayPrefersOfficialQuotaTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    var sessions = Path.Combine(codexHome, "sessions");
    Directory.CreateDirectory(sessions);

    await File.WriteAllTextAsync(Path.Combine(sessions, "roomy.jsonl"), """
        {"timestamp":"2026-04-01T00:10:00Z","type":"session_meta","payload":{"timestamp":"2026-04-01T00:10:00Z"}}
        {"timestamp":"2026-04-01T00:11:00Z","usage":{"input_tokens":400,"output_tokens":200,"cached_input_tokens":0}}
        """);

    var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    };
    var appPaths = AppPaths.Resolve(env);
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:busy", new OAuthTokens { AccessToken = "busy-access" });
    await secrets.WriteTokensAsync("oauth:openai:roomy", new OAuthTokens { AccessToken = "roomy-access" });

    var journal = new SwitchJournalStore(appPaths.SwitchJournalPath);
    await journal.AppendEntryAsync(new SwitchJournalEntry
    {
        Timestamp = DateTimeOffset.Parse("2026-04-01T00:00:00Z"),
        Selection = new CodexSelection { ProviderId = "openai", AccountId = "roomy", SelectedAt = DateTimeOffset.Parse("2026-04-01T00:00:00Z") },
        Status = "ok",
        Message = "activated roomy"
    });

    var config = new AppConfig
    {
        Settings = new AppSettings
        {
            OpenAiAccountMode = OpenAiAccountMode.AggregateGateway
        },
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "busy",
                Label = "Busy",
                CredentialRef = "oauth:openai:busy",
                FiveHourQuota = new QuotaUsageSnapshot { Used = 90, Limit = 100, WindowSeconds = 18000 },
                WeeklyQuota = new QuotaUsageSnapshot { Used = 70, Limit = 100, WindowSeconds = 604800 },
                ManualOrder = 1
            },
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "roomy",
                Label = "Roomy",
                CredentialRef = "oauth:openai:roomy",
                FiveHourQuota = new QuotaUsageSnapshot { Used = 10, Limit = 100, WindowSeconds = 18000 },
                WeeklyQuota = new QuotaUsageSnapshot { Used = 20, Limit = 100, WindowSeconds = 604800 },
                ManualOrder = 2
            }
        ]
    };

    var decision = await new OpenAiAggregateGatewayService(appPaths, secrets)
        .ResolveSelectionAsync(config, new CodexSelection { ProviderId = "openai", AccountId = "busy" }, env);

    AssertTrue(decision.WasRerouted);
    AssertEqual("roomy", decision.ResolvedSelection.AccountId);
    AssertContains(decision.Message, "5h 剩余 90%");
}

static async Task AggregateGatewayAvoidsSameQuotaScopeTest()
{
    using var temp = TempDir.Create();
    var codexHome = Path.Combine(temp.Path, ".codex");
    Directory.CreateDirectory(Path.Combine(codexHome, "sessions"));

    var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = temp.Path
    };
    var appPaths = AppPaths.Resolve(env);
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:busy", new OAuthTokens { AccessToken = "busy-access" });
    await secrets.WriteTokensAsync("oauth:openai:same", new OAuthTokens { AccessToken = "same-access" });
    await secrets.WriteTokensAsync("oauth:openai:different", new OAuthTokens { AccessToken = "different-access" });

    var config = new AppConfig
    {
        Settings = new AppSettings
        {
            OpenAiAccountMode = OpenAiAccountMode.AggregateGateway
        },
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "busy",
                Label = "Busy",
                CredentialRef = "oauth:openai:busy",
                QuotaScopeKey = "shared-scope",
                FiveHourQuota = new QuotaUsageSnapshot { Used = 90, Limit = 100 },
                WeeklyQuota = new QuotaUsageSnapshot { Used = 90, Limit = 100 },
                ManualOrder = 1
            },
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "same",
                Label = "Same Scope",
                CredentialRef = "oauth:openai:same",
                QuotaScopeKey = "shared-scope",
                FiveHourQuota = new QuotaUsageSnapshot { Used = 10, Limit = 100 },
                WeeklyQuota = new QuotaUsageSnapshot { Used = 10, Limit = 100 },
                ManualOrder = 2
            },
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "different",
                Label = "Different Scope",
                CredentialRef = "oauth:openai:different",
                QuotaScopeKey = "different-scope",
                FiveHourQuota = new QuotaUsageSnapshot { Used = 50, Limit = 100 },
                WeeklyQuota = new QuotaUsageSnapshot { Used = 50, Limit = 100 },
                ManualOrder = 3
            }
        ]
    };

    var decision = await new OpenAiAggregateGatewayService(appPaths, secrets)
        .ResolveSelectionAsync(config, new CodexSelection { ProviderId = "openai", AccountId = "busy" }, env);

    AssertTrue(decision.WasRerouted);
    AssertEqual("different", decision.ResolvedSelection.AccountId);
    AssertContains(decision.Message, "Different Scope");
}

static async Task AggregateGatewayManualModeTest()
{
    using var temp = TempDir.Create();
    var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = temp.Path
    };
    var appPaths = AppPaths.Resolve(env);
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:acct", new OAuthTokens { AccessToken = "access", RefreshToken = "refresh", IdToken = "id" });

    var requested = new CodexSelection { ProviderId = "openai", AccountId = "acct" };
    var config = new AppConfig
    {
        Settings = new AppSettings
        {
            OpenAiAccountMode = OpenAiAccountMode.ManualSwitch
        },
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct",
                Label = "Acct",
                CredentialRef = "oauth:openai:acct"
            }
        ]
    };

    var decision = await new OpenAiAggregateGatewayService(appPaths, secrets)
        .ResolveSelectionAsync(config, requested, env);

    AssertTrue(!decision.WasRerouted);
    AssertEqual(requested.ProviderId, decision.ResolvedSelection.ProviderId);
    AssertEqual(requested.AccountId, decision.ResolvedSelection.AccountId);
}

static async Task AggregateGatewayAvoidsNeedsReauthTest()
{
    using var temp = TempDir.Create();
    var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = temp.Path
    };
    var appPaths = AppPaths.Resolve(env);
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:reauth", new OAuthTokens { AccessToken = "reauth-access" });
    await secrets.WriteTokensAsync("oauth:openai:healthy", new OAuthTokens { AccessToken = "healthy-access" });

    var config = new AppConfig
    {
        Settings = new AppSettings
        {
            OpenAiAccountMode = OpenAiAccountMode.AggregateGateway
        },
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "reauth",
                Label = "Needs Reauth",
                CredentialRef = "oauth:openai:reauth",
                Status = AccountStatus.NeedsReauth,
                OfficialUsageError = "Official quota fetch was unauthorized. Re-auth may be required.",
                FiveHourQuota = new QuotaUsageSnapshot { Used = 5, Limit = 100, WindowSeconds = 18000 },
                WeeklyQuota = new QuotaUsageSnapshot { Used = 5, Limit = 100, WindowSeconds = 604800 }
            },
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "healthy",
                Label = "Healthy",
                CredentialRef = "oauth:openai:healthy",
                Status = AccountStatus.Active,
                FiveHourQuota = new QuotaUsageSnapshot { Used = 25, Limit = 100, WindowSeconds = 18000 },
                WeeklyQuota = new QuotaUsageSnapshot { Used = 25, Limit = 100, WindowSeconds = 604800 }
            }
        ]
    };

    var decision = await new OpenAiAggregateGatewayService(appPaths, secrets)
        .ResolveSelectionAsync(config, new CodexSelection { ProviderId = "openai", AccountId = "reauth" }, env);

    AssertTrue(decision.WasRerouted);
    AssertEqual("healthy", decision.ResolvedSelection.AccountId);
}

static Task DesktopLocatorPrefersLatestPackagedVersionTest()
{
    using var temp = TempDir.Create();
    var windowsAppsRoot = Path.Combine(temp.Path, "WindowsApps");
    var oldDesktop = CreateFakeDesktopExecutable(windowsAppsRoot, "OpenAI.Codex_26.409.7971.0_x64__2p2nqsd0c76g0");
    var newDesktop = CreateFakeDesktopExecutable(windowsAppsRoot, "OpenAI.Codex_26.415.1938.0_x64__2p2nqsd0c76g0");
    var locator = new CodexDesktopLocator(
        windowsAppsRoots: [windowsAppsRoot],
        localAppData: temp.Path,
        programFiles: Path.Combine(temp.Path, "ProgramFiles"),
        programFilesX86: Path.Combine(temp.Path, "ProgramFilesX86"),
        pathEnvironment: string.Empty);

    var located = locator.Locate(oldDesktop);

    AssertEqual(newDesktop, located);

    return Task.CompletedTask;
}

static Task DesktopLocatorPrefersCliInferredDesktopTest()
{
    using var temp = TempDir.Create();
    var windowsAppsRoot = Path.Combine(temp.Path, "WindowsApps");
    var staleDesktop = CreateFakeDesktopExecutable(windowsAppsRoot, "OpenAI.Codex_26.409.7971.0_x64__2p2nqsd0c76g0");

    var currentCli = Path.Combine(temp.Path, "current", "app", "resources", "codex.exe");
    var currentDesktop = Path.Combine(temp.Path, "current", "app", "Codex.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(currentCli)!);
    File.WriteAllText(currentCli, "cli");
    File.WriteAllText(currentDesktop, "desktop");
    var locator = new CodexDesktopLocator(
        windowsAppsRoots: Array.Empty<string>(),
        localAppData: temp.Path,
        programFiles: Path.Combine(temp.Path, "ProgramFiles"),
        programFilesX86: Path.Combine(temp.Path, "ProgramFilesX86"),
        pathEnvironment: Path.GetDirectoryName(currentCli));

    var located = locator.Locate(staleDesktop);

    AssertEqual(currentDesktop, located);

    return Task.CompletedTask;
}

static Task DesktopLocatorDetectsPackagedVersionWithoutConfiguredPathTest()
{
    using var temp = TempDir.Create();
    var windowsAppsRoot = Path.Combine(temp.Path, "WindowsApps");
    var latestDesktop = CreateFakeDesktopExecutable(windowsAppsRoot, "OpenAI.Codex_26.415.1938.0_x64__2p2nqsd0c76g0");
    CreateFakeDesktopExecutable(windowsAppsRoot, "OpenAI.Codex_26.409.7971.0_x64__2p2nqsd0c76g0");
    var locator = new CodexDesktopLocator(
        windowsAppsRoots: [windowsAppsRoot],
        localAppData: temp.Path,
        programFiles: Path.Combine(temp.Path, "ProgramFiles"),
        programFilesX86: Path.Combine(temp.Path, "ProgramFilesX86"),
        pathEnvironment: string.Empty);

    var located = locator.Locate();

    AssertEqual(latestDesktop, located);

    return Task.CompletedTask;
}

static async Task LaunchServiceWriteOnlyTest()
{
    var launcher = new FakeProcessLauncher();
    var service = new CodexLaunchService(processLauncher: launcher);
    var result = await service.LaunchIfConfiguredAsync(new AppSettings
    {
        ActivationBehavior = ActivationBehavior.WriteConfigOnly
    });

    AssertTrue(!result.Attempted);
    AssertTrue(!result.Launched);
    AssertTrue(launcher.StartCalls.Count == 0);
}

static async Task LaunchServiceDesktopTest()
{
    var currentExe = Environment.ProcessPath ?? throw new Exception("Current process path is unavailable.");
    Environment.SetEnvironmentVariable("ELECTRON_RUN_AS_NODE", "1");
    Environment.SetEnvironmentVariable("CODEX_INTERNAL_ORIGINATOR_OVERRIDE", "Codex Desktop");
    Environment.SetEnvironmentVariable("CODEX_SHELL", "1");
    Environment.SetEnvironmentVariable("CODEX_THREAD_ID", "test-thread");
    Environment.SetEnvironmentVariable("DOTNET_ROOT", @"D:\portable\.dotnet");
    Environment.SetEnvironmentVariable("DOTNET_ROOT_X64", @"D:\portable\.dotnet");
    Environment.SetEnvironmentVariable("DOTNET_CLI_HOME", @"D:\portable");
    Environment.SetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0");
    Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", @"D:\portable\.dotnet\dotnet.exe");
    Environment.SetEnvironmentVariable("NUGET_PACKAGES", @"D:\portable\.nuget\packages");
    var launcher = new FakeProcessLauncher();
    var service = new CodexLaunchService(processLauncher: launcher);
    try
    {
        var result = await service.LaunchIfConfiguredAsync(new AppSettings
        {
            ActivationBehavior = ActivationBehavior.LaunchNewCodex,
            CodexDesktopPath = currentExe
        });

        var startInfo = launcher.StartCalls.Single();
        AssertTrue(result.Attempted);
        AssertTrue(result.Launched, result.Message);
        AssertEqual("desktop", result.Target?.Kind);
        AssertEqual(currentExe, startInfo.FileName);
        AssertTrue(!startInfo.UseShellExecute);
        AssertTrue(!HasEnvironmentVariable(startInfo, "ELECTRON_RUN_AS_NODE"));
        AssertTrue(!HasEnvironmentVariable(startInfo, "CODEX_INTERNAL_ORIGINATOR_OVERRIDE"));
        AssertTrue(!HasEnvironmentVariable(startInfo, "CODEX_SHELL"));
        AssertTrue(!HasEnvironmentVariable(startInfo, "CODEX_THREAD_ID"));
        AssertTrue(!HasEnvironmentVariable(startInfo, "DOTNET_ROOT"));
        AssertTrue(!HasEnvironmentVariable(startInfo, "DOTNET_ROOT_X64"));
        AssertTrue(!HasEnvironmentVariable(startInfo, "DOTNET_CLI_HOME"));
        AssertTrue(!HasEnvironmentVariable(startInfo, "DOTNET_MULTILEVEL_LOOKUP"));
        AssertTrue(!HasEnvironmentVariable(startInfo, "DOTNET_HOST_PATH"));
        AssertTrue(!HasEnvironmentVariable(startInfo, "NUGET_PACKAGES"));
    }
    finally
    {
        Environment.SetEnvironmentVariable("ELECTRON_RUN_AS_NODE", null);
        Environment.SetEnvironmentVariable("CODEX_INTERNAL_ORIGINATOR_OVERRIDE", null);
        Environment.SetEnvironmentVariable("CODEX_SHELL", null);
        Environment.SetEnvironmentVariable("CODEX_THREAD_ID", null);
        Environment.SetEnvironmentVariable("DOTNET_ROOT", null);
        Environment.SetEnvironmentVariable("DOTNET_ROOT_X64", null);
        Environment.SetEnvironmentVariable("DOTNET_CLI_HOME", null);
        Environment.SetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", null);
        Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", null);
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", null);
    }
}

static async Task CompatibleLaunchInjectsActiveApiKeyTest()
{
    var currentExe = Environment.ProcessPath ?? throw new Exception("Current process path is unavailable.");
    var secretStore = new InMemorySecretStore();
    await secretStore.WriteSecretAsync("api-key:compatible:acct", "sk-test");
    var config = new AppConfig
    {
        ActiveSelection = new CodexSelection
        {
            ProviderId = "compatible",
            AccountId = "acct"
        },
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "compatible",
                DisplayName = "Compatible",
                Kind = ProviderKind.OpenAiCompatible,
                BaseUrl = "https://example.test/v1"
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "compatible",
                AccountId = "acct",
                Label = "Compatible account",
                CredentialRef = "api-key:compatible:acct"
            }
        ]
    };
    var launchEnvironment = await CodexLaunchEnvironmentBuilder.BuildAsync(config, secretStore);
    var launcher = new FakeProcessLauncher();
    var service = new CodexLaunchService(processLauncher: launcher);

    var result = await service.LaunchAsync(new AppSettings
    {
        CodexDesktopPath = currentExe
    }, launchEnvironment);

    var startInfo = launcher.StartCalls.Single();
    AssertTrue(result.Launched, result.Message);
    AssertTrue(!startInfo.UseShellExecute);
    AssertEqual("sk-test", startInfo.EnvironmentVariables["OPENAI_API_KEY"]);
    AssertEqual("https://example.test/v1", startInfo.EnvironmentVariables["OPENAI_BASE_URL"]);
}

static Task CodexDesktopProcessServiceDetectsDesktopTest()
{
    using var temp = TempDir.Create();
    var desktopPath = Path.Combine(temp.Path, "app", "Codex.exe");
    var cliPath = Path.Combine(temp.Path, "app", "resources", "codex.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(cliPath)!);
    File.WriteAllText(desktopPath, "desktop");
    File.WriteAllText(cliPath, "cli");
    var provider = new FakeCodexDesktopProcessProvider([
        new FakeCodexDesktopProcess(1, "codex", cliPath, hasMainWindow: true, parentProcessId: 999),
        new FakeCodexDesktopProcess(2, "Codex", desktopPath, hasMainWindow: true)
    ]);
    var service = new CodexDesktopProcessService(
        new CodexDesktopLocator(
            windowsAppsRoots: [],
            localAppData: temp.Path,
            programFiles: Path.Combine(temp.Path, "ProgramFiles"),
            programFilesX86: Path.Combine(temp.Path, "ProgramFilesX86"),
            pathEnvironment: string.Empty),
        provider);

    var status = service.GetStatus(desktopPath);

    AssertTrue(status.IsRunning);
    AssertEqual(1, status.Processes.Count);
    AssertEqual(2, status.Processes.Single().ProcessId);
    return Task.CompletedTask;
}

static async Task CodexDesktopProcessServiceNormalCloseTest()
{
    using var temp = TempDir.Create();
    var desktopPath = Path.Combine(temp.Path, "Codex.exe");
    File.WriteAllText(desktopPath, "desktop");
    var process = new FakeCodexDesktopProcess(7, "Codex", desktopPath, hasMainWindow: true);
    var service = new CodexDesktopProcessService(
        new CodexDesktopLocator(
            windowsAppsRoots: [],
            localAppData: temp.Path,
            programFiles: Path.Combine(temp.Path, "ProgramFiles"),
            programFilesX86: Path.Combine(temp.Path, "ProgramFilesX86"),
            pathEnvironment: string.Empty),
        new FakeCodexDesktopProcessProvider([process]));
    var status = service.GetStatus(desktopPath);

    var result = await service.RequestCloseAsync(status, desktopPath, TimeSpan.FromMilliseconds(50));

    AssertTrue(result.CloseRequested);
    AssertTrue(result.AllExited, result.Message);
    AssertEqual(1, process.CloseRequests);
    AssertTrue(process.HasExited);
}

static async Task CodexDesktopProcessServiceNoSilentKillTest()
{
    using var temp = TempDir.Create();
    var desktopPath = Path.Combine(temp.Path, "Codex.exe");
    File.WriteAllText(desktopPath, "desktop");
    var process = new FakeCodexDesktopProcess(8, "Codex", desktopPath, hasMainWindow: false);
    var service = new CodexDesktopProcessService(
        new CodexDesktopLocator(
            windowsAppsRoots: [],
            localAppData: temp.Path,
            programFiles: Path.Combine(temp.Path, "ProgramFiles"),
            programFilesX86: Path.Combine(temp.Path, "ProgramFilesX86"),
            pathEnvironment: string.Empty),
        new FakeCodexDesktopProcessProvider([process]));
    var status = service.GetStatus(desktopPath);

    var result = await service.RequestCloseAsync(status, desktopPath, TimeSpan.FromMilliseconds(50));

    AssertTrue(!result.CloseRequested);
    AssertTrue(!result.AllExited);
    AssertEqual(0, process.CloseRequests);
    AssertTrue(!process.HasExited);
    AssertContains(result.Message, "避免静默强杀");
}

static async Task CodexDesktopProcessServiceTerminateAfterConfirmationTest()
{
    using var temp = TempDir.Create();
    var desktopPath = Path.Combine(temp.Path, "app", "Codex.exe");
    var appServerPath = Path.Combine(temp.Path, "app", "resources", "codex.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(appServerPath)!);
    File.WriteAllText(desktopPath, "desktop");
    File.WriteAllText(appServerPath, "server");
    var process = new FakeCodexDesktopProcess(9, "Codex", desktopPath, hasMainWindow: true, exitsOnClose: true);
    var appServer = new FakeCodexDesktopProcess(10, "codex", appServerPath, hasMainWindow: false, parentProcessId: 9);
    var service = new CodexDesktopProcessService(
        new CodexDesktopLocator(
            windowsAppsRoots: [],
            localAppData: temp.Path,
            programFiles: Path.Combine(temp.Path, "ProgramFiles"),
            programFilesX86: Path.Combine(temp.Path, "ProgramFilesX86"),
            pathEnvironment: string.Empty),
        new FakeCodexDesktopProcessProvider([process, appServer]));
    var status = service.GetStatus(desktopPath);

    AssertEqual(2, status.Processes.Count);
    var closeResult = await service.RequestCloseAsync(status, desktopPath, TimeSpan.FromMilliseconds(20));

    AssertTrue(closeResult.CloseRequested);
    AssertTrue(!closeResult.AllExited);
    AssertEqual(1, process.CloseRequests);
    AssertEqual(0, appServer.CloseRequests);
    AssertEqual(0, process.TerminateRequests);
    AssertEqual(0, appServer.TerminateRequests);
    AssertTrue(process.HasExited);
    AssertTrue(!appServer.HasExited);

    var terminateResult = await service.TerminateAfterUserConfirmationAsync(status, desktopPath, TimeSpan.FromMilliseconds(20));

    AssertTrue(terminateResult.TerminateRequested);
    AssertTrue(terminateResult.AllExited, terminateResult.Message);
    AssertEqual(0, process.TerminateRequests);
    AssertEqual(1, appServer.TerminateRequests);
    AssertTrue(appServer.HasExited);
}

static async Task CodexDesktopProcessServiceForceTerminateFallbackTest()
{
    using var temp = TempDir.Create();
    var desktopPath = Path.Combine(temp.Path, "app", "Codex.exe");
    var appServerPath = Path.Combine(temp.Path, "app", "resources", "codex.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(appServerPath)!);
    File.WriteAllText(desktopPath, "desktop");
    File.WriteAllText(appServerPath, "server");
    var process = new FakeCodexDesktopProcess(11, "Codex", desktopPath, hasMainWindow: false, exitsOnTerminate: false);
    var appServer = new FakeCodexDesktopProcess(12, "codex", appServerPath, hasMainWindow: false, parentProcessId: 11, exitsOnTerminate: false);
    var provider = new FakeCodexDesktopProcessProvider([process, appServer]);
    var fallback = new FakeCodexDesktopForceTerminator([process, appServer]);
    var service = new CodexDesktopProcessService(
        new CodexDesktopLocator(
            windowsAppsRoots: [],
            localAppData: temp.Path,
            programFiles: Path.Combine(temp.Path, "ProgramFiles"),
            programFilesX86: Path.Combine(temp.Path, "ProgramFilesX86"),
            pathEnvironment: string.Empty),
        provider,
        fallback);
    var status = service.GetStatus(desktopPath);

    var terminateResult = await service.TerminateAfterUserConfirmationAsync(status, desktopPath, TimeSpan.FromMilliseconds(20));

    AssertTrue(terminateResult.TerminateRequested);
    AssertTrue(terminateResult.AllExited, terminateResult.Message);
    var debugInfo = $"roots={string.Join(",", terminateResult.AttemptedRootProcessIds)} force={string.Join(",", fallback.AttemptedProcessIds)} rootTerminate={process.TerminateRequests} childTerminate={appServer.TerminateRequests}";
    AssertEqual(1, terminateResult.AttemptedRootProcessIds.Count);
    AssertEqual(11, terminateResult.AttemptedRootProcessIds.Single());
    AssertEqual(2, fallback.AttemptedProcessIds.Count);
    AssertEqual(12, fallback.AttemptedProcessIds[0]);
    AssertEqual(11, fallback.AttemptedProcessIds[1]);
    AssertTrue(process.TerminateRequests == 1, debugInfo);
    AssertTrue(appServer.TerminateRequests == 1, debugInfo);
    AssertTrue(process.HasExited);
    AssertTrue(appServer.HasExited);
}

static async Task CodexDesktopProcessServiceForceTerminateTrackedPidsOnlyTest()
{
    using var temp = TempDir.Create();
    var desktopPath = Path.Combine(temp.Path, "app", "Codex.exe");
    var workerPath = Path.Combine(temp.Path, "app", "Codex.Helper.exe");
    var appServerPath = Path.Combine(temp.Path, "app", "resources", "codex.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(appServerPath)!);
    File.WriteAllText(desktopPath, "desktop");
    File.WriteAllText(workerPath, "worker");
    File.WriteAllText(appServerPath, "server");
    var parentLauncher = new FakeCodexDesktopProcess(99, "CodexBar.Win", Path.Combine(temp.Path, "CodexBar.Win.exe"), hasMainWindow: false);
    var root = new FakeCodexDesktopProcess(21, "Codex", desktopPath, hasMainWindow: false, parentProcessId: 99, exitsOnTerminate: false);
    var worker = new FakeCodexDesktopProcess(22, "Codex", workerPath, hasMainWindow: false, parentProcessId: 21, exitsOnTerminate: false);
    var appServer = new FakeCodexDesktopProcess(23, "codex", appServerPath, hasMainWindow: false, parentProcessId: 21, exitsOnTerminate: false);
    var provider = new FakeCodexDesktopProcessProvider([parentLauncher, root, worker, appServer]);
    var fallback = new FakeCodexDesktopForceTerminator([root, worker, appServer]);
    var service = new CodexDesktopProcessService(
        new CodexDesktopLocator(
            windowsAppsRoots: [],
            localAppData: temp.Path,
            programFiles: Path.Combine(temp.Path, "ProgramFiles"),
            programFilesX86: Path.Combine(temp.Path, "ProgramFilesX86"),
            pathEnvironment: string.Empty),
        provider,
        fallback);
    var status = service.GetStatus(desktopPath);

    var terminateResult = await service.TerminateAfterUserConfirmationAsync(status, desktopPath, TimeSpan.FromMilliseconds(20));

    AssertTrue(terminateResult.AllExited, terminateResult.Message);
    AssertEqual(1, terminateResult.AttemptedRootProcessIds.Count);
    AssertEqual(21, terminateResult.AttemptedRootProcessIds.Single());
    AssertEqual(3, fallback.AttemptedProcessIds.Count);
    AssertEqual(22, fallback.AttemptedProcessIds[0]);
    AssertEqual(23, fallback.AttemptedProcessIds[1]);
    AssertEqual(21, fallback.AttemptedProcessIds[2]);
    AssertTrue(!parentLauncher.HasExited);
    AssertTrue(root.HasExited);
    AssertTrue(worker.HasExited);
    AssertTrue(appServer.HasExited);
}

static async Task AppConfigManualOrderTest()
{
    using var temp = TempDir.Create();
    var store = new AppConfigStore(Path.Combine(temp.Path, "config.json"));
    await store.SaveAsync(new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "compatible",
                DisplayName = "Compatible",
                Kind = ProviderKind.OpenAiCompatible,
                BaseUrl = "https://example.test/v1"
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "compatible",
                AccountId = "one",
                Label = "One",
                CredentialRef = "api-key:compatible:one",
                ManualOrder = 7
            }
        ]
    });

    var loaded = await store.LoadAsync();
    AssertEqual(7, loaded.Accounts.Single().ManualOrder);
}

static Task OpenAiOAuthAccountKeyAvoidsSharedAccountIdCollisionTest()
{
    var config = new AppConfig
    {
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct-shared",
                Label = "First",
                SubjectId = "sub-one",
                CredentialRef = "oauth:openai:acct-shared"
            }
        ]
    };

    var accountId = OpenAiOAuthAccountKey.ResolveAccountId(
        config,
        new OAuthTokens
        {
            AccessToken = "second-access",
            AccountId = "acct-shared"
        },
        new OAuthIdentity("sub-two", "two@example.test", null));

    AssertTrue(!string.Equals("acct-shared", accountId, StringComparison.Ordinal));
    AssertTrue(accountId.StartsWith("oauth-", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task OpenAiWorkspaceDiscoveryReadsIdTokenOrganizationsTest()
{
    var tokens = new OAuthTokens
    {
        AccessToken = "access-token",
        AccountId = "acct-team",
        IdToken = CreateUnsignedJwt("""
            {
              "sub": "user-sub",
              "email": "me@example.test",
              "organizations": [
                {
                  "account_id": "acct-personal",
                  "name": "Personal",
                  "type": "personal",
                  "seat_type": "owner",
                  "quota_scope_key": "quota-personal"
                },
                {
                  "account_id": "acct-team",
                  "name": "Research Team",
                  "type": "business",
                  "seat_type": "member",
                  "quota_scope_key": "quota-team"
                }
              ]
            }
            """)
    };
    var identity = OAuthIdentityExtractor.Extract(tokens);

    var workspaces = OpenAiWorkspaceDiscovery.Discover(tokens, identity).ToList();
    var personal = workspaces.Single(workspace => workspace.WorkspaceId == "acct-personal");
    var team = workspaces.Single(workspace => workspace.WorkspaceId == "acct-team");

    AssertEqual(2, workspaces.Count);
    AssertEqual("Personal", personal.WorkspaceName);
    AssertEqual("personal", personal.WorkspaceType);
    AssertEqual("owner", personal.SeatType);
    AssertEqual("quota-personal", personal.QuotaScopeKey);
    AssertEqual("Research Team", team.WorkspaceName);
    AssertTrue(team.IsCurrent);
    return Task.CompletedTask;
}

static async Task OpenAiWorkspaceDiscoveryReadsChatGptAccountListTest()
{
    string? sentAuthorization = null;
    var handler = new StubHttpMessageHandler(request =>
    {
        sentAuthorization = request.Headers.Authorization?.Scheme;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "items": [
                    {
                      "id": "workspace-account",
                      "name": "Work",
                      "structure": "workspace",
                      "current_user_role": "standard-user"
                    },
                    {
                      "id": "personal-account",
                      "name": null,
                      "structure": "personal",
                      "current_user_role": "account-owner"
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
        };
    });

    var workspaces = await OpenAiWorkspaceDiscovery.DiscoverAsync(
        new OAuthTokens
        {
            AccessToken = "access-token",
            AccountId = "personal-account"
        },
        new OAuthIdentity("same-sub", "me@example.test", null),
        new HttpClient(handler));

    var work = workspaces.Single(workspace => workspace.WorkspaceId == "workspace-account");
    var personal = workspaces.Single(workspace => workspace.WorkspaceId == "personal-account");

    AssertEqual("Bearer", sentAuthorization);
    AssertEqual("Work", work.WorkspaceName);
    AssertEqual("workspace", work.WorkspaceType);
    AssertEqual("standard-user", work.SeatType);
    AssertTrue(!work.IsCurrent);
    AssertEqual("Personal", personal.WorkspaceName);
    AssertEqual("personal", personal.WorkspaceType);
    AssertEqual("account-owner", personal.SeatType);
    AssertTrue(personal.IsCurrent);
}

static async Task OpenAiWorkspaceDiscoverySendsCodexDesktopAccountHeadersTest()
{
    string? sentOriginator = null;
    string? sentAccountHeader = null;
    string? sentUserAgent = null;
    var handler = new StubHttpMessageHandler(request =>
    {
        sentOriginator = request.Headers.TryGetValues("originator", out var originatorValues)
            ? originatorValues.SingleOrDefault()
            : null;
        sentAccountHeader = request.Headers.TryGetValues("ChatGPT-Account-Id", out var accountValues)
            ? accountValues.SingleOrDefault()
            : null;
        sentUserAgent = request.Headers.UserAgent.ToString();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "items": [
                    {
                      "id": "workspace-account",
                      "name": "Work",
                      "structure": "workspace",
                      "current_user_role": "standard-user"
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
        };
    });

    await OpenAiWorkspaceDiscovery.DiscoverAsync(
        new OAuthTokens
        {
            AccessToken = "access-token",
            IdToken = CreateUnsignedJwt("""
                {
                  "https://api.openai.com/auth": {
                    "chatgpt_account_id": "workspace-account"
                  }
                }
                """)
        },
        new OAuthIdentity("same-sub", "me@example.test", null),
        new HttpClient(handler));

    AssertEqual("Codex Desktop", sentOriginator);
    AssertEqual("workspace-account", sentAccountHeader);
    AssertContains(sentUserAgent ?? "", "Codex");
}

static async Task OpenAiWorkspaceDiscoveryUsesSelectionHintForSaveTest()
{
    var handler = new StubHttpMessageHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("forbidden", Encoding.UTF8, "text/plain")
        });
    var tokens = new OAuthTokens
    {
        AccessToken = "access-token",
        AccountId = "org-team",
        IdToken = CreateUnsignedJwt("""
            {
              "https://api.openai.com/auth": {
                "chatgpt_account_id": "org-team",
                "chatgpt_plan_type": "plus"
              }
            }
            """)
    };
    var hint = new OpenAiWorkspaceDescriptor(
        "org-team",
        "Work",
        "workspace",
        "member",
        "team-scope",
        false);

    var workspace = await OpenAiWorkspaceDiscovery.ResolveCurrentForSaveAsync(
        tokens,
        OAuthIdentityExtractor.Extract(tokens),
        hint,
        new HttpClient(handler));

    AssertEqual("org-team", workspace.WorkspaceId);
    AssertEqual("Work", workspace.WorkspaceName);
    AssertEqual("workspace", workspace.WorkspaceType);
    AssertEqual("member", workspace.SeatType);
    AssertEqual("team-scope", workspace.QuotaScopeKey);
}

static async Task OpenAiWorkspaceDiscoveryPrefersChatGptAccountsOverOrgIdsTest()
{
    var handler = new StubHttpMessageHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "items": [
                    {
                      "id": "workspace-account",
                      "name": "Work",
                      "structure": "workspace",
                      "current_user_role": "standard-user"
                    },
                    {
                      "id": "personal-account",
                      "name": null,
                      "structure": "personal",
                      "current_user_role": "account-owner"
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
        });

    var workspaces = await OpenAiWorkspaceDiscovery.DiscoverAsync(
        new OAuthTokens
        {
            AccessToken = "access-token",
            AccountId = "org-personal",
            IdToken = CreateUnsignedJwt("""
                {
                  "https://api.openai.com/auth": {
                    "chatgpt_account_id": "personal-account",
                    "organizations": [
                      {
                        "id": "org-personal",
                        "title": "Personal",
                        "role": "owner"
                      }
                    ]
                  }
                }
                """)
        },
        new OAuthIdentity("same-sub", "me@example.test", null),
        new HttpClient(handler));

    AssertEqual(2, workspaces.Count);
    AssertTrue(workspaces.All(workspace => workspace.WorkspaceId != "org-personal"));
    AssertTrue(workspaces.Single(workspace => workspace.WorkspaceId == "personal-account").IsCurrent);
    AssertTrue(!workspaces.Single(workspace => workspace.WorkspaceId == "workspace-account").IsCurrent);
}

static async Task OpenAiWorkspaceDiscoveryIgnoresOrgIdsWhenChatGptAccountListForbiddenTest()
{
    var handler = new StubHttpMessageHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("forbidden", Encoding.UTF8, "text/plain")
        });

    var workspaces = await OpenAiWorkspaceDiscovery.DiscoverAsync(
        new OAuthTokens
        {
            AccessToken = "access-token",
            AccountId = "workspace-team",
            IdToken = CreateUnsignedJwt("""
                {
                  "https://api.openai.com/auth": {
                    "chatgpt_account_id": "workspace-team",
                    "chatgpt_plan_type": "business",
                    "organizations": [
                      {
                        "id": "org-personal",
                        "title": "Personal",
                        "role": "owner"
                      }
                    ]
                  }
                }
                """)
        },
        new OAuthIdentity("same-sub", "me@example.test", null),
        new HttpClient(handler));

    AssertEqual(1, workspaces.Count);
    AssertEqual("workspace-team", workspaces.Single().WorkspaceId);
    AssertTrue(workspaces.Single().IsCurrent);
}

static Task OpenAiOAuthAccountKeyReusesMatchingLegacyRecordTest()
{
    var config = new AppConfig
    {
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct-shared",
                Label = "First",
                SubjectId = "sub-one",
                CredentialRef = "oauth:openai:acct-shared"
            }
        ]
    };

    var accountId = OpenAiOAuthAccountKey.ResolveAccountId(
        config,
        new OAuthTokens
        {
            AccessToken = "first-access",
            AccountId = "acct-shared"
        },
        new OAuthIdentity("sub-one", "one@example.test", null));

    AssertEqual("acct-shared", accountId);
    return Task.CompletedTask;
}

static Task OpenAiOAuthAccountKeyTreatsSubjectFallbackAsAccountIdTest()
{
    var config = new AppConfig
    {
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct-only",
                Label = "Fallback",
                CredentialRef = "oauth:openai:acct-only"
            }
        ]
    };

    var accountId = OpenAiOAuthAccountKey.ResolveAccountId(
        config,
        new OAuthTokens
        {
            AccessToken = "fallback-access",
            AccountId = "acct-only"
        },
        new OAuthIdentity("acct-only", null, null));

    AssertEqual("acct-only", accountId);
    return Task.CompletedTask;
}

static Task OpenAiOAuthAccountKeySeparatesSameLoginWorkspacesTest()
{
    var config = new AppConfig
    {
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "oauth-personal",
                Label = "me@example.test - Personal",
                Email = "me@example.test",
                SubjectId = "same-sub",
                OpenAiAccountId = "acct-personal",
                WorkspaceId = "acct-personal",
                CredentialRef = "oauth:openai:oauth-personal"
            }
        ]
    };

    var accountId = OpenAiOAuthAccountKey.ResolveAccountId(
        config,
        new OAuthTokens
        {
            AccessToken = "team-access",
            AccountId = "acct-team"
        },
        new OAuthIdentity("same-sub", "me@example.test", null));

    AssertTrue(!string.Equals("oauth-personal", accountId, StringComparison.Ordinal));
    AssertTrue(accountId.StartsWith("oauth-", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static async Task AppConfigRestartConfirmationSuppressionTest()
{
    using var temp = TempDir.Create();
    var store = new AppConfigStore(Path.Combine(temp.Path, "config.json"));
    await store.SaveAsync(new AppConfig
    {
        Settings = new AppSettings
        {
            SuppressRestartConfirmation = true
        }
    });

    var loaded = await store.LoadAsync();
    AssertTrue(loaded.Settings.SuppressRestartConfirmation);
}

static async Task AppConfigOverlayStartupPreferenceTest()
{
    using var temp = TempDir.Create();
    var store = new AppConfigStore(Path.Combine(temp.Path, "config.json"));
    await store.SaveAsync(new AppConfig
    {
        Settings = new AppSettings
        {
            OpenOverlayOnStartup = true
        }
    });

    var loaded = await store.LoadAsync();
    AssertTrue(loaded.Settings.OpenOverlayOnStartup);
}

static async Task AppConfigAccountCardDensityPreferenceTest()
{
    using var temp = TempDir.Create();
    var store = new AppConfigStore(Path.Combine(temp.Path, "config.json"));
    await store.SaveAsync(new AppConfig
    {
        Settings = new AppSettings
        {
            AccountCardDensity = AccountCardDensity.Compact
        }
    });

    var loaded = await store.LoadAsync();
    AssertEqual(AccountCardDensity.Compact, loaded.Settings.AccountCardDensity);
}

static Task QuotaFormatterFiveHourResetTest()
{
    var now = new DateTimeOffset(2026, 4, 16, 9, 0, 0, TimeSpan.FromHours(8));
    var snapshot = new QuotaUsageSnapshot
    {
        Used = 25,
        Limit = 100,
        ResetAt = new DateTimeOffset(2026, 4, 16, 13, 45, 0, TimeSpan.FromHours(8))
    };

    var compact = OpenAiQuotaDisplayFormatter.FormatCompactRemaining(snapshot, "5h", now);
    var detailed = OpenAiQuotaDisplayFormatter.FormatDetailedRemaining(snapshot, "5h", now);
    var label = OpenAiQuotaDisplayFormatter.FormatQuotaLabel(snapshot, "5h 额度", now);

    AssertEqual("5h 剩余 75% · 下次 13:45", compact);
    AssertEqual("5h 剩余额度：75% | 下次刷新：13:45", detailed);
    AssertEqual("5h 额度 刷新于 13:45", label);
    return Task.CompletedTask;
}

static Task QuotaFormatterWeeklyResetTest()
{
    var now = new DateTimeOffset(2026, 4, 16, 9, 0, 0, TimeSpan.FromHours(8));
    var farSnapshot = new QuotaUsageSnapshot
    {
        Used = 60,
        Limit = 100,
        ResetAt = new DateTimeOffset(2026, 4, 20, 8, 0, 0, TimeSpan.FromHours(8))
    };
    var nearSnapshot = new QuotaUsageSnapshot
    {
        Used = 60,
        Limit = 100,
        ResetAt = new DateTimeOffset(2026, 4, 16, 20, 30, 0, TimeSpan.FromHours(8))
    };

    var far = OpenAiQuotaDisplayFormatter.FormatCompactRemaining(farSnapshot, "week", now);
    var near = OpenAiQuotaDisplayFormatter.FormatCompactRemaining(nearSnapshot, "week", now);
    var farLabel = OpenAiQuotaDisplayFormatter.FormatQuotaLabel(farSnapshot, "周额度", now);
    var nearLabel = OpenAiQuotaDisplayFormatter.FormatQuotaLabel(nearSnapshot, "周额度", now);

    AssertEqual("week 剩余 40% · 下次 2026-04-20", far);
    AssertEqual("week 剩余 40% · 下次 20:30", near);
    AssertEqual("周额度 刷新于 04-20", farLabel);
    AssertEqual("周额度 刷新于 20:30", nearLabel);
    return Task.CompletedTask;
}

static Task QuotaFormatterInlineLabelTest()
{
    var now = new DateTimeOffset(2026, 4, 16, 9, 0, 0, TimeSpan.FromHours(8));
    var fiveHourSnapshot = new QuotaUsageSnapshot
    {
        Used = 25,
        Limit = 100,
        ResetAt = new DateTimeOffset(2026, 4, 16, 13, 45, 0, TimeSpan.FromHours(8))
    };
    var farWeeklySnapshot = new QuotaUsageSnapshot
    {
        Used = 60,
        Limit = 100,
        ResetAt = new DateTimeOffset(2026, 4, 20, 8, 0, 0, TimeSpan.FromHours(8))
    };
    var nearWeeklySnapshot = new QuotaUsageSnapshot
    {
        Used = 60,
        Limit = 100,
        ResetAt = new DateTimeOffset(2026, 4, 16, 20, 30, 0, TimeSpan.FromHours(8))
    };

    AssertEqual("5h@13:45", OpenAiQuotaDisplayFormatter.FormatInlineQuotaLabel(fiveHourSnapshot, "5h 额度", now));
    AssertEqual("周@04-20", OpenAiQuotaDisplayFormatter.FormatInlineQuotaLabel(farWeeklySnapshot, "周额度", now));
    AssertEqual("周@20:30", OpenAiQuotaDisplayFormatter.FormatInlineQuotaLabel(nearWeeklySnapshot, "week", now));
    AssertEqual("5h@--", OpenAiQuotaDisplayFormatter.FormatInlineQuotaLabel(new QuotaUsageSnapshot(), "5h", now));
    return Task.CompletedTask;
}

static async Task OfficialOpenAiUsageRefreshTest()
{
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:acct", new OAuthTokens
    {
        AccessToken = "access-token",
        AccountId = "team-account"
    });

    var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""
            {
              "plan_type": "plus",
              "rate_limit": {
                "primary_window": {
                  "used_percent": 25,
                  "limit_window_seconds": 18000,
                  "reset_after_seconds": 600
                },
                "secondary_window": {
                  "used_percent": 60,
                  "limit_window_seconds": 604800,
                  "reset_after_seconds": 3600
                }
              }
            }
            """, Encoding.UTF8, "application/json")
    });
    var httpClient = new HttpClient(handler);
    var service = new OpenAiOfficialUsageService(secrets, httpClient);
    var config = new AppConfig
    {
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct",
                Label = "Acct",
                CredentialRef = "oauth:openai:acct"
            }
        ]
    };

    var refresh = await service.RefreshAsync(config, TimeSpan.Zero);
    var account = refresh.Config.Accounts.Single();

    AssertTrue(refresh.Changed);
    AssertEqual(1, refresh.AccountsRefreshed);
    AssertEqual(AccountTier.Plus, account.Tier);
    AssertEqual("plus", account.OfficialPlanTypeRaw);
    AssertEqual(25, account.FiveHourQuota.Used);
    AssertEqual(100, account.FiveHourQuota.Limit);
    AssertEqual(18000, account.FiveHourQuota.WindowSeconds);
    AssertTrue(account.FiveHourQuota.ResetAt.HasValue);
    AssertEqual(60, account.WeeklyQuota.Used);
    AssertEqual(604800, account.WeeklyQuota.WindowSeconds);
    AssertTrue(account.OfficialUsageFetchedAt.HasValue);
    AssertTrue(string.IsNullOrWhiteSpace(account.OfficialUsageError));
}

static async Task OfficialOpenAiUsageRefreshWorkspaceScopeTest()
{
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:acct", new OAuthTokens
    {
        AccessToken = "access-token",
        AccountId = "token-default"
    });

    string? sentWorkspaceHeader = null;
    var handler = new StubHttpMessageHandler(request =>
    {
        sentWorkspaceHeader = request.Headers.TryGetValues("ChatGPT-Account-Id", out var values)
            ? values.SingleOrDefault()
            : null;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "plan_type": "business",
                  "quota_scope_key": "shared-team-scope",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 20,
                      "limit_window_seconds": 18000,
                      "reset_after_seconds": 600
                    },
                    "secondary_window": {
                      "used_percent": 40,
                      "limit_window_seconds": 604800,
                      "reset_after_seconds": 3600
                    }
                  }
                }
                """, Encoding.UTF8, "application/json")
        };
    });
    var service = new OpenAiOfficialUsageService(secrets, new HttpClient(handler));
    var config = new AppConfig
    {
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct",
                Label = "Team",
                WorkspaceId = "workspace-team",
                CredentialRef = "oauth:openai:acct"
            }
        ]
    };

    var refresh = await service.RefreshAsync(config, TimeSpan.Zero);
    var account = refresh.Config.Accounts.Single();

    AssertEqual("workspace-team", sentWorkspaceHeader);
    AssertEqual("shared-team-scope", account.QuotaScopeKey);
    AssertEqual("workspace-team", account.WorkspaceId);
}

static async Task OfficialOpenAiUsageRefreshMapsTeamPlanTest()
{
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:acct", new OAuthTokens
    {
        AccessToken = "access-token",
        AccountId = "team-account"
    });

    var handler = new StubHttpMessageHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "plan_type": "team",
                  "rate_limit": {
                    "primary_window": {
                      "used_percent": 43,
                      "limit_window_seconds": 18000,
                      "reset_after_seconds": 600
                    },
                    "secondary_window": {
                      "used_percent": 7,
                      "limit_window_seconds": 604800,
                      "reset_after_seconds": 3600
                    }
                  }
                }
                """, Encoding.UTF8, "application/json")
        });
    var service = new OpenAiOfficialUsageService(secrets, new HttpClient(handler));
    var config = new AppConfig
    {
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct",
                Label = "Team",
                CredentialRef = "oauth:openai:acct"
            }
        ]
    };

    var refresh = await service.RefreshAsync(config, TimeSpan.Zero);
    var account = refresh.Config.Accounts.Single();

    AssertEqual(AccountTier.Team, account.Tier);
    AssertEqual("team", OpenAiAccountDisplayFormatter.FormatTier(account));
}

static Task OpenAiAccountDisplayShowsTeamPlanAndWorkspaceTest()
{
    var account = new AccountRecord
    {
        ProviderId = "openai",
        AccountId = "acct",
        Label = "me@example.test · Personal",
        Email = "me@example.test",
        WorkspaceName = "Personal",
        OfficialPlanTypeRaw = "team",
        CredentialRef = "oauth:openai:acct"
    };

    AssertEqual("team", OpenAiAccountDisplayFormatter.FormatTier(account));
    AssertEqual("Team", OpenAiAccountDisplayFormatter.EffectiveWorkspaceName(account));
    return Task.CompletedTask;
}

static async Task OfficialOpenAiUsageUnauthorizedTest()
{
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:acct", new OAuthTokens
    {
        AccessToken = "access-token",
        AccountId = "team-account"
    });

    var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
    var service = new OpenAiOfficialUsageService(secrets, new HttpClient(handler));
    var config = new AppConfig
    {
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct",
                Label = "Acct",
                CredentialRef = "oauth:openai:acct",
                Status = AccountStatus.Active
            }
        ]
    };

    var refresh = await service.RefreshAsync(config, TimeSpan.Zero);
    var account = refresh.Config.Accounts.Single();

    AssertEqual(1, refresh.AccountsRefreshed);
    AssertEqual(1, refresh.FailedAccounts);
    AssertEqual(AccountStatus.NeedsReauth, account.Status);
    AssertContains(account.OfficialUsageError ?? "", "unauthorized");
    AssertTrue(account.OfficialUsageFetchedAt.HasValue);
}

static async Task SessionArchiveExportImportTest()
{
    using var temp = TempDir.Create();
    var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = Path.Combine(temp.Path, "profile")
    });
    var service = new SessionArchiveService(appPaths);
    var sourceHome = ResolveTestHome(Path.Combine(temp.Path, "source-home"), temp.Path);
    var targetHome = ResolveTestHome(Path.Combine(temp.Path, "target-home"), temp.Path);

    var sessionRelativePath = Path.Combine("2026", "04", "session-a.jsonl");
    var archivedRelativePath = Path.Combine("2026", "03", "session-b.jsonl");
    var sessionText = "{\"timestamp\":\"2026-04-20T00:00:00Z\",\"provider\":\"old-provider\",\"account_id\":\"old-account\",\"message\":\"active\"}\n";
    var archivedText = "{\"timestamp\":\"2026-03-20T00:00:00Z\",\"model_provider\":\"old-provider\",\"message\":\"archived\"}\n";

    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(sourceHome.SessionsPath, sessionRelativePath))!);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(sourceHome.ArchivedSessionsPath, archivedRelativePath))!);
    await File.WriteAllTextAsync(Path.Combine(sourceHome.SessionsPath, sessionRelativePath), sessionText);
    await File.WriteAllTextAsync(Path.Combine(sourceHome.ArchivedSessionsPath, archivedRelativePath), archivedText);
    Directory.CreateDirectory(sourceHome.RootPath);
    await File.WriteAllTextAsync(Path.Combine(sourceHome.RootPath, "session_index.jsonl"), """
{"id":"thread-a","thread_name":"A","updated_at":"2026-04-20T00:00:00Z"}
{"id":"thread-b","thread_name":"B","updated_at":"2026-03-20T00:00:00Z"}
""");
    await File.WriteAllTextAsync(sourceHome.ConfigPath, "model_provider = \"old-provider\"");
    await File.WriteAllTextAsync(sourceHome.AuthPath, "{\"access_token\":\"secret\"}");

    var archivePath = Path.Combine(temp.Path, "history.zip");
    var exportResult = await service.ExportAsync(sourceHome, archivePath, new SessionArchiveExportOptions(IncludeArchived: true));

    AssertEqual(1, exportResult.SessionsExported);
    AssertEqual(1, exportResult.ArchivedSessionsExported);
    AssertTrue(exportResult.SessionIndexExported);

    using (var archive = ZipFile.OpenRead(archivePath))
    {
        var entryNames = archive.Entries.Select(entry => entry.FullName).ToList();
        AssertTrue(entryNames.Contains("manifest.json"));
        AssertTrue(entryNames.Contains("session_index.jsonl"));
        AssertTrue(entryNames.Contains("sessions/2026/04/session-a.jsonl"));
        AssertTrue(entryNames.Contains("archived_sessions/2026/03/session-b.jsonl"));
        AssertTrue(!entryNames.Contains("config.toml"));
        AssertTrue(!entryNames.Contains("auth.json"));
    }

    Directory.CreateDirectory(targetHome.RootPath);
    await File.WriteAllTextAsync(targetHome.ConfigPath, "current-config");
    await File.WriteAllTextAsync(targetHome.AuthPath, "current-auth");

    var importResult = await service.ImportAsync(targetHome, archivePath);

    AssertEqual(1, importResult.Sessions.Copied);
    AssertEqual(1, importResult.ArchivedSessions.Copied);
    AssertEqual(2, importResult.SessionIndex.Merged);
    AssertEqual(sessionText, await File.ReadAllTextAsync(Path.Combine(targetHome.SessionsPath, sessionRelativePath)));
    AssertEqual(archivedText, await File.ReadAllTextAsync(Path.Combine(targetHome.ArchivedSessionsPath, archivedRelativePath)));
    AssertEqual("current-config", await File.ReadAllTextAsync(targetHome.ConfigPath));
    AssertEqual("current-auth", await File.ReadAllTextAsync(targetHome.AuthPath));
}

static async Task SessionArchiveConflictImportTest()
{
    using var temp = TempDir.Create();
    var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = Path.Combine(temp.Path, "profile")
    });
    var service = new SessionArchiveService(appPaths);
    var sourceHome = ResolveTestHome(Path.Combine(temp.Path, "source-home"), temp.Path);
    var targetHome = ResolveTestHome(Path.Combine(temp.Path, "target-home"), temp.Path);
    var sourceSession = Path.Combine(sourceHome.SessionsPath, "conflict.jsonl");
    var targetSession = Path.Combine(targetHome.SessionsPath, "conflict.jsonl");
    var importedSession = Path.Combine(targetHome.SessionsPath, "conflict.imported-1.jsonl");
    var sourceText = "{\"timestamp\":\"2026-04-21T00:00:00Z\",\"provider\":\"source\"}\n";
    var targetText = "{\"timestamp\":\"2026-04-21T00:00:00Z\",\"provider\":\"target\"}\n";

    Directory.CreateDirectory(sourceHome.SessionsPath);
    Directory.CreateDirectory(targetHome.SessionsPath);
    Directory.CreateDirectory(sourceHome.RootPath);
    await File.WriteAllTextAsync(sourceSession, sourceText);
    await File.WriteAllTextAsync(targetSession, targetText);
    await File.WriteAllTextAsync(Path.Combine(sourceHome.RootPath, "session_index.jsonl"), """
{"id":"thread-conflict","thread_name":"Conflict","updated_at":"2026-04-21T00:00:00Z"}
""");

    var archivePath = Path.Combine(temp.Path, "history.zip");
    await service.ExportAsync(sourceHome, archivePath);

    var firstImport = await service.ImportAsync(targetHome, archivePath);
    var secondImport = await service.ImportAsync(targetHome, archivePath);

    AssertEqual(0, firstImport.Sessions.Copied);
    AssertEqual(1, firstImport.Sessions.Renamed);
    AssertEqual(1, firstImport.SessionIndex.Merged);
    AssertEqual(targetText, await File.ReadAllTextAsync(targetSession));
    AssertEqual(sourceText, await File.ReadAllTextAsync(importedSession));
    AssertEqual(1, secondImport.Sessions.Skipped);
    AssertEqual(1, secondImport.SessionIndex.Skipped);
}

static async Task SessionArchiveUnsafePathTest()
{
    using var temp = TempDir.Create();
    var appPaths = AppPaths.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = Path.Combine(temp.Path, "profile")
    });
    var service = new SessionArchiveService(appPaths);
    var targetHome = ResolveTestHome(Path.Combine(temp.Path, "target-home"), temp.Path);
    var archivePath = Path.Combine(temp.Path, "unsafe.zip");

    using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
    {
        var entry = archive.CreateEntry("sessions/../evil.jsonl");
        await using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync("{}\n");
    }

    var rejected = false;
    try
    {
        await service.ImportAsync(targetHome, archivePath);
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }

    AssertTrue(rejected);
    AssertTrue(!File.Exists(Path.Combine(temp.Path, "evil.jsonl")));
    AssertTrue(!Directory.Exists(targetHome.SessionsPath) || !Directory.EnumerateFiles(targetHome.SessionsPath, "*", SearchOption.AllDirectories).Any());
}

static async Task AccountCsvCompatibleSecretTest()
{
    using var temp = TempDir.Create();
    var sourceSecrets = new InMemorySecretStore();
    await sourceSecrets.WriteSecretAsync("api-key:provider:acct", "sk-secret");
    var sourceConfig = new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "provider",
                CodexProviderId = "openai",
                DisplayName = "Provider",
                Kind = ProviderKind.OpenAiCompatible,
                BaseUrl = "https://example.test/v1"
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "provider",
                AccountId = "acct",
                Label = "Account",
                CredentialRef = "api-key:provider:acct",
                ManualOrder = 3
            }
        ]
    };

    var csv = Path.Combine(temp.Path, "accounts.csv");
    await new AccountCsvService(sourceSecrets, sourceSecrets).ExportAsync(sourceConfig, csv, new AccountCsvExportOptions(IncludeSecrets: true));

    var targetSecrets = new InMemorySecretStore();
    var (importedConfig, result) = await new AccountCsvService(targetSecrets, targetSecrets).ImportAsync(AppConfigStore.DefaultConfig(), csv);

    AssertEqual(1, result.AccountsImported);
    AssertEqual(1, result.SecretsImported);
    AssertEqual("Provider", importedConfig.Providers.Single(p => p.ProviderId == "provider").DisplayName);
    AssertEqual("openai", importedConfig.Providers.Single(p => p.ProviderId == "provider").CodexProviderId);
    AssertEqual(3, importedConfig.Accounts.Single(a => a.ProviderId == "provider").ManualOrder);
    AssertEqual("sk-secret", await targetSecrets.ReadSecretAsync("api-key:provider:acct"));
}

static async Task AccountCsvOAuthWorkspaceMetadataTest()
{
    using var temp = TempDir.Create();
    var secrets = new InMemorySecretStore();
    var config = new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct",
                Label = "me@example.test - Research",
                Email = "me@example.test",
                SubjectId = "subject",
                OpenAiAccountId = "workspace-account",
                WorkspaceId = "workspace-account",
                WorkspaceName = "Research",
                WorkspaceType = "business",
                SeatType = "member",
                QuotaScopeKey = "shared-scope",
                TokenCountResetAt = DateTimeOffset.Parse("2026-04-01T01:00:00Z"),
                CredentialRef = "oauth:openai:acct",
                ManualOrder = 4
            }
        ]
    };

    var csv = Path.Combine(temp.Path, "workspace.csv");
    await new AccountCsvService(secrets, secrets).ExportAsync(config, csv);
    var text = await File.ReadAllTextAsync(csv);
    AssertContains(text, "workspace_id");
    AssertContains(text, "Research");
    AssertContains(text, "shared-scope");

    var (importedConfig, result) = await new AccountCsvService(secrets, secrets)
        .ImportAsync(AppConfigStore.DefaultConfig(), csv);
    var account = importedConfig.Accounts.Single(account => account.ProviderId == "openai");

    AssertEqual(1, result.AccountsImported);
    AssertEqual("workspace-account", account.WorkspaceId);
    AssertEqual("Research", account.WorkspaceName);
    AssertEqual("business", account.WorkspaceType);
    AssertEqual("member", account.SeatType);
    AssertEqual("shared-scope", account.QuotaScopeKey);
    AssertEqual(DateTimeOffset.Parse("2026-04-01T01:00:00Z"), account.TokenCountResetAt);
}

static async Task AccountCsvOAuthSecretSafetyTest()
{
    using var temp = TempDir.Create();
    var secrets = new InMemorySecretStore();
    await secrets.WriteTokensAsync("oauth:openai:acct", new OAuthTokens
    {
        AccessToken = "access-secret",
        RefreshToken = "refresh-secret",
        IdToken = "id-secret",
        AccountId = "oauth-account"
    });
    var config = new AppConfig
    {
        Providers =
        [
            new ProviderDefinition
            {
                ProviderId = "openai",
                DisplayName = "OpenAI",
                Kind = ProviderKind.OpenAiOAuth,
                AuthMode = AuthMode.OAuth
            }
        ],
        Accounts =
        [
            new AccountRecord
            {
                ProviderId = "openai",
                AccountId = "acct",
                Label = "me@example.com",
                CredentialRef = "oauth:openai:acct"
            }
        ]
    };

    var metadataOnly = Path.Combine(temp.Path, "metadata.csv");
    await new AccountCsvService(secrets, secrets).ExportAsync(config, metadataOnly);
    var metadataText = await File.ReadAllTextAsync(metadataOnly);
    AssertDoesNotContain(metadataText, "access-secret");
    AssertDoesNotContain(metadataText, "refresh-secret");

    var withSecrets = Path.Combine(temp.Path, "with-secrets.csv");
    await new AccountCsvService(secrets, secrets).ExportAsync(config, withSecrets, new AccountCsvExportOptions(IncludeSecrets: true));
    var secretText = await File.ReadAllTextAsync(withSecrets);
    AssertContains(secretText, "access-secret");
    AssertContains(secretText, "refresh-secret");
}

static Task UpdateSemverComparisonTest()
{
    AssertTrue(SemanticVersion.Parse("v0.3.5").CompareTo(SemanticVersion.Parse("0.3.4")) > 0);
    AssertTrue(SemanticVersion.Parse("1.0.0-alpha").CompareTo(SemanticVersion.Parse("1.0.0")) < 0);
    AssertTrue(SemanticVersion.Parse("1.0.0-alpha.2").CompareTo(SemanticVersion.Parse("1.0.0-alpha.10")) < 0);
    AssertTrue(SemanticVersion.TryParse("release-latest", out _) == false);
    return Task.CompletedTask;
}

static async Task UpdateCheckIgnoresDraftAndPrereleaseTest()
{
    var releasesJson = """
        [
          {
            "tag_name": "v9.0.0",
            "name": "Draft",
            "draft": true,
            "prerelease": false,
            "html_url": "https://example.test/draft",
            "body": "draft",
            "published_at": "2026-05-01T00:00:00Z",
            "assets": []
          },
          {
            "tag_name": "v8.0.0-beta.1",
            "name": "Beta",
            "draft": false,
            "prerelease": true,
            "html_url": "https://example.test/beta",
            "body": "beta",
            "published_at": "2026-05-02T00:00:00Z",
            "assets": [
              {
                "name": "CodexBar-portable-win-x64-v8.0.0-beta.1.zip",
                "browser_download_url": "https://example.test/beta.zip",
                "size": 10
              }
            ]
          },
          {
            "tag_name": "v0.3.5",
            "name": "Stable",
            "draft": false,
            "prerelease": false,
            "html_url": "https://example.test/stable",
            "body": "stable release",
            "published_at": "2026-05-03T00:00:00Z",
            "assets": [
              {
                "name": "CodexBar-portable-win-x64-v0.3.5.zip",
                "browser_download_url": "https://example.test/stable.zip",
                "size": 20
              }
            ]
          }
        ]
        """;
    var service = NewUpdateService(releasesJson);

    var result = await service.CheckLatestAsync("0.3.4");

    AssertEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
    AssertEqual("0.3.5", result.Release?.Version.ToString());
    AssertEqual("CodexBar-portable-win-x64-v0.3.5.zip", result.Release?.ZipAsset.Name);
}

static async Task UpdateCheckSelectsPortableZipAssetTest()
{
    var releasesJson = """
        [
          {
            "tag_name": "v0.4.0",
            "name": "Release",
            "draft": false,
            "prerelease": false,
            "html_url": "https://example.test/release",
            "body": "Line one.\n\nLine two.",
            "published_at": "2026-05-03T00:00:00Z",
            "assets": [
              {
                "name": "source.zip",
                "browser_download_url": "https://example.test/source.zip",
                "size": 1
              },
              {
                "name": "CodexBar-portable-win-x64-v0.4.0.zip.sha256",
                "browser_download_url": "https://example.test/checksum",
                "size": 64
              },
              {
                "name": "CodexBar-portable-win-x64-v0.4.0.zip",
                "browser_download_url": "https://example.test/app.zip",
                "size": 12345
              }
            ]
          }
        ]
        """;
    var service = NewUpdateService(releasesJson);

    var result = await service.CheckLatestAsync("0.3.4");

    AssertEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
    AssertEqual("CodexBar-portable-win-x64-v0.4.0.zip", result.Release?.ZipAsset.Name);
    AssertEqual(12345L, result.Release?.ZipAsset.SizeBytes);
    AssertEqual("CodexBar-portable-win-x64-v0.4.0.zip.sha256", result.Release?.ChecksumAsset?.Name);
    AssertContains(result.Release?.Summary ?? "", "Line one.");
}

static async Task UpdateChecksumMatchTest()
{
    using var temp = TempDir.Create();
    var packagePath = Path.Combine(temp.Path, "CodexBar-portable-win-x64-v0.3.5.zip");
    await File.WriteAllTextAsync(packagePath, "package");
    var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("package"))).ToLowerInvariant();

    var result = await UpdateChecksumVerifier.VerifyAsync(
        packagePath,
        $"{expected}  CodexBar-portable-win-x64-v0.3.5.zip",
        "CodexBar-portable-win-x64-v0.3.5.zip");

    AssertTrue(result.IsMatch, result.Message);
    AssertTrue(result.HasOfficialChecksum);
    AssertEqual(expected, result.CalculatedSha256);
}

static async Task UpdateChecksumMismatchTest()
{
    using var temp = TempDir.Create();
    var packagePath = Path.Combine(temp.Path, "CodexBar-portable-win-x64-v0.3.5.zip");
    await File.WriteAllTextAsync(packagePath, "package");

    var result = await UpdateChecksumVerifier.VerifyAsync(
        packagePath,
        new string('0', 64) + "  CodexBar-portable-win-x64-v0.3.5.zip",
        "CodexBar-portable-win-x64-v0.3.5.zip");

    AssertTrue(!result.IsMatch);
    AssertContains(result.Message, "SHA256");
}

static async Task UpdateCheckNetworkFailureTest()
{
    var service = new UpdateService(
        new HttpClient(new StubHttpMessageHandler(_ => throw new HttpRequestException("offline"))),
        new UpdateServiceOptions("owner", "repo"));

    var result = await service.CheckLatestAsync("0.3.4");

    AssertEqual(UpdateCheckStatus.NetworkError, result.Status);
    AssertContains(result.Message, "网络");
    AssertContains(result.Message, "offline");
}

static Task UpdateLauncherDangerousTargetDirectoryTest()
{
    using var temp = TempDir.Create();
    var userHome = Path.Combine(temp.Path, "user");
    Directory.CreateDirectory(userHome);
    var driveRoot = Path.GetPathRoot(Path.GetFullPath(temp.Path)) ?? temp.Path;
    var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    AssertTrue(!UpdateInstallerLauncher.ValidateInstallDirectory("", userHome).IsValid);
    AssertTrue(!UpdateInstallerLauncher.ValidateInstallDirectory(driveRoot, userHome).IsValid);
    AssertTrue(!UpdateInstallerLauncher.ValidateInstallDirectory(userHome, userHome).IsValid);
    AssertTrue(!UpdateInstallerLauncher.ValidateInstallDirectory(Path.Combine(userHome, ".codex"), userHome).IsValid);
    AssertTrue(!UpdateInstallerLauncher.ValidateInstallDirectory(Path.Combine(userHome, ".codexbar"), userHome).IsValid);
    if (!string.IsNullOrWhiteSpace(windowsDirectory))
    {
        AssertTrue(!UpdateInstallerLauncher.ValidateInstallDirectory(windowsDirectory, userHome).IsValid);
        AssertTrue(!UpdateInstallerLauncher.ValidateInstallDirectory(Path.Combine(windowsDirectory, "System32"), userHome).IsValid);
    }

    AssertTrue(UpdateInstallerLauncher.ValidateInstallDirectory(Path.Combine(temp.Path, "CodexBar"), userHome).IsValid);
    return Task.CompletedTask;
}

static Task UpdaterHelperRefusesWindowsSystemDirectoryTest()
{
    var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    AssertTrue(!string.IsNullOrWhiteSpace(windowsDirectory), "Windows directory was not available.");

    AssertUpdaterHelperRejectsDirectory(windowsDirectory);
    AssertUpdaterHelperRejectsDirectory(Path.Combine(windowsDirectory, "System32"));
    return Task.CompletedTask;
}

static Task UpdateLauncherArgumentsAvoidSensitivePathsTest()
{
    using var temp = TempDir.Create();
    var installDirectory = Path.Combine(temp.Path, "CodexBar");
    var helperPath = Path.Combine(temp.Path, "helper", "CodexBar.Updater.exe");
    var zipPath = Path.Combine(temp.Path, "downloads", "CodexBar-portable-win-x64-v0.3.5.zip");
    var userHome = Path.Combine(temp.Path, "user");
    Directory.CreateDirectory(installDirectory);
    Directory.CreateDirectory(Path.GetDirectoryName(helperPath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
    File.WriteAllText(helperPath, "fake");
    File.WriteAllText(zipPath, "fake");

    var request = UpdateInstallerLauncher.CreateInstallRequest(
        currentProcessId: 123,
        installDirectory: installDirectory,
        zipPath: zipPath,
        targetVersion: "0.3.5",
        restartExecutableName: "CodexBar.Win.exe",
        tempRoot: Path.Combine(temp.Path, "update-temp"),
        userProfile: userHome);
    var startInfo = UpdateInstallerLauncher.BuildStartInfo(helperPath, request);
    var arguments = string.Join(" ", startInfo.ArgumentList);

    AssertDoesNotContain(arguments, ".codex");
    AssertDoesNotContain(arguments, "auth.json");
    AssertDoesNotContain(arguments, "sessions");
    AssertTrue(!HasEnvironmentVariable(startInfo, "CODEX_HOME"));
    AssertTrue(!HasEnvironmentVariable(startInfo, "OPENAI_API_KEY"));
    AssertEqual(Path.GetDirectoryName(helperPath), startInfo.WorkingDirectory);
    return Task.CompletedTask;
}

static async Task UpdateCheckSkipsCurrentOrOlderVersionsTest()
{
    var releasesJson = """
        [
          {
            "tag_name": "v0.3.4",
            "name": "Current",
            "draft": false,
            "prerelease": false,
            "html_url": "https://example.test/current",
            "body": "current",
            "published_at": "2026-05-03T00:00:00Z",
            "assets": [
              {
                "name": "CodexBar-portable-win-x64-v0.3.4.zip",
                "browser_download_url": "https://example.test/current.zip",
                "size": 20
              }
            ]
          },
          {
            "tag_name": "v0.3.3",
            "name": "Older",
            "draft": false,
            "prerelease": false,
            "html_url": "https://example.test/older",
            "body": "older",
            "published_at": "2026-05-02T00:00:00Z",
            "assets": [
              {
                "name": "CodexBar-portable-win-x64-v0.3.3.zip",
                "browser_download_url": "https://example.test/older.zip",
                "size": 20
              }
            ]
          }
        ]
        """;
    var service = NewUpdateService(releasesJson);

    var result = await service.CheckLatestAsync("0.3.4");

    AssertEqual(UpdateCheckStatus.UpToDate, result.Status);
    AssertTrue(!result.HasUpdate);
}

static UpdateService NewUpdateService(string releasesJson)
    => new(
        new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(releasesJson, Encoding.UTF8, "application/json")
        })),
        new UpdateServiceOptions("owner", "repo"));

static void AssertUpdaterHelperRejectsDirectory(string installDirectory)
{
    var assemblyPath = Path.Combine(AppContext.BaseDirectory, "CodexBar.Updater.dll");
    var assembly = Assembly.LoadFrom(assemblyPath);
    var safetyType = assembly.GetType("CodexBar.Updater.UpdateSafety")
        ?? throw new Exception("Updater safety type was not found.");
    var method = safetyType.GetMethod("ValidateInstallDirectory", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new Exception("Updater safety method was not found.");

    try
    {
        method.Invoke(null, [installDirectory]);
    }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
    {
        return;
    }

    throw new Exception($"Updater helper accepted unsafe install directory: {installDirectory}");
}

static string CreateUnsignedJwt(string payloadJson)
{
    static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    var header = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
    var payload = Base64Url(Encoding.UTF8.GetBytes(payloadJson));
    return $"{header}.{payload}.";
}

static CodexActivationService NewActivationService(AppPaths appPaths, InMemorySecretStore secrets)
    => new(
        new CodexHomeLocator(),
        new CodexConfigStore(),
        new CodexAuthStore(),
        new CodexStateTransaction(appPaths),
        new CodexIntegrityChecker(),
        secrets,
        secrets);

static CodexHomeState ResolveTestHome(string codexHome, string userProfileRoot)
    => new CodexHomeLocator().Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CODEX_HOME"] = codexHome,
        ["USERPROFILE"] = Path.Combine(userProfileRoot, "user")
    });

static void AssertTrue(bool condition, string? message = null)
{
    if (!condition)
    {
        throw new Exception(message ?? "Expected true.");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"Expected {expected}, got {actual}.");
    }
}

static void AssertContains(string text, string expected)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        throw new Exception($"Expected text to contain {expected}.");
    }
}

static void AssertDoesNotContain(string text, string value)
{
    if (text.Contains(value, StringComparison.Ordinal))
    {
        throw new Exception($"Expected text not to contain {value}.");
    }
}

static void AssertSequenceEqual(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
{
    if (expected.Count != actual.Count)
    {
        throw new Exception($"Expected sequence length {expected.Count}, actual {actual.Count}.");
    }

    for (var index = 0; index < expected.Count; index++)
    {
        if (!string.Equals(expected[index], actual[index], StringComparison.Ordinal))
        {
            throw new Exception($"Expected sequence item {index} to be '{expected[index]}', actual '{actual[index]}'.");
        }
    }
}

static bool HasEnvironmentVariable(ProcessStartInfo startInfo, string name)
    => startInfo.EnvironmentVariables.Keys
        .Cast<string>()
        .Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

static string CreateFakeDesktopExecutable(string windowsAppsRoot, string packageDirectoryName)
{
    var desktopPath = Path.Combine(windowsAppsRoot, packageDirectoryName, "app", "Codex.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
    File.WriteAllText(desktopPath, "fake");
    return desktopPath;
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codexbar-tests-" + Guid.NewGuid().ToString("N"));

    private TempDir()
    {
        Directory.CreateDirectory(Path);
    }

    public static TempDir Create() => new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best effort cleanup for locked temp files.
        }
    }
}

internal sealed class FakeProcessLauncher : IExternalProcessLauncher
{
    public List<ProcessStartInfo> StartCalls { get; } = [];

    public void Start(ProcessStartInfo startInfo)
    {
        StartCalls.Add(startInfo);
    }
}

internal sealed class FakeCodexDesktopProcessProvider : ICodexDesktopProcessProvider
{
    private readonly IReadOnlyList<ICodexDesktopProcess> _processes;

    public FakeCodexDesktopProcessProvider(IReadOnlyList<ICodexDesktopProcess> processes)
    {
        _processes = processes;
    }

    public IReadOnlyList<ICodexDesktopProcess> EnumerateCandidates()
        => _processes;
}

internal sealed class FakeCodexDesktopProcess : ICodexDesktopProcess
{
    private readonly bool _exitsOnClose;
    private readonly bool _exitsOnTerminate;

    public FakeCodexDesktopProcess(
        int id,
        string processName,
        string? executablePath,
        bool hasMainWindow,
        int? parentProcessId = null,
        bool exitsOnClose = true,
        bool exitsOnTerminate = true)
    {
        Id = id;
        ParentProcessId = parentProcessId;
        ProcessName = processName;
        ExecutablePath = executablePath;
        HasMainWindow = hasMainWindow;
        _exitsOnClose = exitsOnClose;
        _exitsOnTerminate = exitsOnTerminate;
    }

    public int Id { get; }

    public int? ParentProcessId { get; }

    public string ProcessName { get; }

    public string? ExecutablePath { get; }

    public bool HasMainWindow { get; }

    public bool HasExited { get; private set; }

    public int CloseRequests { get; private set; }

    public int TerminateRequests { get; private set; }

    public bool RequestClose()
    {
        CloseRequests++;
        if (_exitsOnClose)
        {
            HasExited = true;
        }

        return true;
    }

    public void Terminate()
    {
        TerminateRequests++;
        if (_exitsOnTerminate)
        {
            HasExited = true;
        }
    }

    public void MarkExited()
        => HasExited = true;

    public void Refresh()
    {
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => HasExited ? Task.CompletedTask : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

    public void Dispose()
    {
    }
}

internal sealed class FakeCodexDesktopForceTerminator : ICodexDesktopForceTerminator
{
    private readonly IReadOnlyList<FakeCodexDesktopProcess> _processes;

    public FakeCodexDesktopForceTerminator(IReadOnlyList<FakeCodexDesktopProcess> processes)
    {
        _processes = processes;
    }

    public List<int> AttemptedProcessIds { get; } = [];

    public CodexDesktopForceTerminateResult TryForceTerminateProcess(int processId)
    {
        AttemptedProcessIds.Add(processId);
        var byId = _processes.ToDictionary(process => process.Id);
        if (!byId.TryGetValue(processId, out var process))
        {
            return new CodexDesktopForceTerminateResult(false, $"PID {processId} not found.");
        }

        process.MarkExited();
        return new CodexDesktopForceTerminateResult(true);
    }
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_handler(request));
}
