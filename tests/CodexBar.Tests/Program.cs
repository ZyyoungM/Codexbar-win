using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CodexBar.Auth;
using CodexBar.CodexCompat;
using CodexBar.Core;
using CodexBar.Runtime;

var tests = new (string Name, Func<Task> Run)[]
{
    ("home locator respects CODEX_HOME", HomeLocatorTest),
    ("paths tolerate duplicate environment key casing", DuplicateEnvironmentKeyTest),
    ("toml editor preserves unknown keys", TomlEditorTest),
    ("compatible activation writes only active state", CompatibleActivationTest),
    ("compatible activation supports custom codex provider alias", CompatibleActivationCustomProviderAliasTest),
    ("compatible activation preserves oauth identity snapshot", CompatibleActivationPreservesOAuthIdentityTest),
    ("oauth activation writes codex-compatible last_refresh", OAuthActivationWritesLastRefreshTest),
    ("transaction rolls back on validation failure", RollbackTest),
    ("manual callback parser accepts URL and code", ManualCallbackParserTest),
    ("usage scanner reads shared history without writes", UsageScannerTest),
    ("usage scanner tolerates locked active session files", UsageScannerLockedFileTest),
    ("compatible provider probe suggests missing v1 path", CompatibleProviderProbeSuggestsV1Test),
    ("usage attribution maps sessions by switch intervals", UsageAttributionTest),
    ("switch journal renames provider ids", SwitchJournalRenameProviderTest),
    ("aggregate gateway reroutes openai to lower-usage account", AggregateGatewayRerouteTest),
    ("aggregate gateway prefers lower official quota pressure over local history", AggregateGatewayPrefersOfficialQuotaTest),
    ("aggregate gateway leaves manual switch selections unchanged", AggregateGatewayManualModeTest),
    ("aggregate gateway avoids accounts that need reauth when a healthy account exists", AggregateGatewayAvoidsNeedsReauthTest),
    ("desktop locator prefers desktop inferred from current cli path", DesktopLocatorPrefersCliInferredDesktopTest),
    ("desktop locator prefers latest packaged Codex version", DesktopLocatorPrefersLatestPackagedVersionTest),
    ("desktop locator detects packaged Codex without configured path", DesktopLocatorDetectsPackagedVersionWithoutConfiguredPathTest),
    ("launch service skips process start when write only", LaunchServiceWriteOnlyTest),
    ("launch service starts desktop with clean environment", LaunchServiceDesktopTest),
    ("compatible launch injects active API key", CompatibleLaunchInjectsActiveApiKeyTest),
    ("app config persists manual account order", AppConfigManualOrderTest),
    ("quota formatter shows remaining quota and 5h reset time as hh:mm", QuotaFormatterFiveHourResetTest),
    ("quota formatter shows weekly reset as date unless within 24h", QuotaFormatterWeeklyResetTest),
    ("official OpenAI usage refresh maps plan and quota windows", OfficialOpenAiUsageRefreshTest),
    ("official OpenAI usage refresh marks unauthorized accounts for reauth", OfficialOpenAiUsageUnauthorizedTest),
    ("account csv imports compatible secrets", AccountCsvCompatibleSecretTest),
    ("account csv exports oauth metadata without secrets by default", AccountCsvOAuthSecretSafetyTest)
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

    await File.WriteAllTextAsync(Path.Combine(sessions, "busy.jsonl"), """
        {"timestamp":"2026-04-01T00:10:00Z","type":"session_meta","payload":{"timestamp":"2026-04-01T00:10:00Z"}}
        {"timestamp":"2026-04-01T00:11:00Z","usage":{"input_tokens":100,"output_tokens":20,"cached_input_tokens":0}}
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
        Timestamp = DateTimeOffset.Parse("2026-04-01T00:00:00Z"),
        Selection = new CodexSelection { ProviderId = "openai", AccountId = "busy", SelectedAt = DateTimeOffset.Parse("2026-04-01T00:00:00Z") },
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
    }
    finally
    {
        Environment.SetEnvironmentVariable("ELECTRON_RUN_AS_NODE", null);
        Environment.SetEnvironmentVariable("CODEX_INTERNAL_ORIGINATOR_OVERRIDE", null);
        Environment.SetEnvironmentVariable("CODEX_SHELL", null);
        Environment.SetEnvironmentVariable("CODEX_THREAD_ID", null);
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

    AssertEqual("5h 剩余 75% · 下次 13:45", compact);
    AssertEqual("5h 剩余额度：75% | 下次刷新：13:45", detailed);
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

    AssertEqual("week 剩余 40% · 下次 2026-04-20", far);
    AssertEqual("week 剩余 40% · 下次 20:30", near);
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

static CodexActivationService NewActivationService(AppPaths appPaths, InMemorySecretStore secrets)
    => new(
        new CodexHomeLocator(),
        new CodexConfigStore(),
        new CodexAuthStore(),
        new CodexStateTransaction(appPaths),
        new CodexIntegrityChecker(),
        secrets,
        secrets);

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
