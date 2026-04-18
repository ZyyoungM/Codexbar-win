using System.Text.Json;
using CodexBar.Core;

namespace CodexBar.CodexCompat;

public sealed class CodexActivationService
{
    private readonly CodexHomeLocator _homeLocator;
    private readonly CodexConfigStore _configStore;
    private readonly CodexAuthStore _authStore;
    private readonly CodexStateTransaction _transaction;
    private readonly CodexIntegrityChecker _integrityChecker;
    private readonly ISecretStore _secretStore;
    private readonly IOAuthTokenStore _tokenStore;

    public CodexActivationService(
        CodexHomeLocator homeLocator,
        CodexConfigStore configStore,
        CodexAuthStore authStore,
        CodexStateTransaction transaction,
        CodexIntegrityChecker integrityChecker,
        ISecretStore secretStore,
        IOAuthTokenStore tokenStore)
    {
        _homeLocator = homeLocator;
        _configStore = configStore;
        _authStore = authStore;
        _transaction = transaction;
        _integrityChecker = integrityChecker;
        _secretStore = secretStore;
        _tokenStore = tokenStore;
    }

    public async Task<CodexSwitchResult> ActivateAsync(
        AppConfig appConfig,
        CodexSelection selection,
        IDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var provider = appConfig.Providers.SingleOrDefault(p => p.ProviderId == selection.ProviderId)
            ?? throw new InvalidOperationException($"Unknown provider: {selection.ProviderId}");
        var account = appConfig.Accounts.SingleOrDefault(a => a.ProviderId == provider.ProviderId && a.AccountId == selection.AccountId)
            ?? throw new InvalidOperationException($"Unknown account: {selection.AccountId}");
        var home = _homeLocator.Resolve(environment);

        var configDocument = await _configStore.ReadAsync(home.ConfigPath, cancellationToken);
        using var existingAuth = await _authStore.ReadAsync(home.AuthPath, cancellationToken);
        ApplySharedConfig(configDocument, appConfig.ModelSettings);

        string authContent;
        if (provider.Kind == ProviderKind.OpenAiOAuth)
        {
            configDocument.SetString("model_provider", "openai");
            configDocument.RemoveTopLevelKey("openai_base_url");
            var tokens = await _tokenStore.ReadTokensAsync(account.CredentialRef, cancellationToken)
                ?? throw new InvalidOperationException($"OAuth tokens are missing for {account.Label}.");
            authContent = _authStore.SerializeOpenAiOAuth(tokens);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(provider.BaseUrl))
            {
                throw new InvalidOperationException($"Provider {provider.DisplayName} is missing base_url.");
            }

            var apiKey = await _secretStore.ReadSecretAsync(account.CredentialRef, cancellationToken)
                ?? throw new InvalidOperationException($"API key is missing for {account.Label}.");
            var codexProviderId = EffectiveCodexProviderId(provider);
            configDocument.SetString("model_provider", codexProviderId);
            if (IsBuiltInOpenAiProvider(codexProviderId))
            {
                configDocument.SetString("openai_base_url", provider.BaseUrl);
            }
            else
            {
                configDocument.RemoveTopLevelKey("openai_base_url");
                var providerSection = ModelProviderSection(codexProviderId);
                configDocument.SetSectionString(providerSection, "name", provider.DisplayName);
                configDocument.SetSectionString(providerSection, "base_url", provider.BaseUrl);
                configDocument.SetSectionString(providerSection, "env_key", "OPENAI_API_KEY");
                configDocument.SetSectionString(providerSection, "wire_api", ToCodexWireApi(provider.WireApi));
            }

            authContent = _authStore.SerializeCompatibleApiKey(
                apiKey,
                ExtractPreservedTokens(existingAuth),
                ExtractPreservedLastRefresh(existingAuth));
        }

        CleanupLegacyKeys(
            configDocument,
            provider.Kind == ProviderKind.OpenAiCompatible &&
            !IsBuiltInOpenAiProvider(EffectiveCodexProviderId(provider))
                ? ModelProviderSection(EffectiveCodexProviderId(provider))
                : null);
        var configContent = configDocument.ToString();

        return await _transaction.WriteActivationAsync(
            home,
            selection,
            configContent,
            authContent,
            () => _integrityChecker.Validate(home),
            cancellationToken);
    }

    private static void ApplySharedConfig(CodexConfigDocument document, ModelSettings settings)
    {
        document.SetString("model", settings.Model);
        document.SetString("review_model", settings.ReviewModel);
        document.SetString("model_reasoning_effort", settings.ModelReasoningEffort);
    }

    private static string ModelProviderSection(string providerId)
        => IsBareTomlKey(providerId)
            ? $"model_providers.{providerId}"
            : $"model_providers.{QuoteTomlKey(providerId)}";

    private static string EffectiveCodexProviderId(ProviderDefinition provider)
        => !string.IsNullOrWhiteSpace(provider.CodexProviderId)
            ? provider.CodexProviderId.Trim()
            : provider.Kind == ProviderKind.OpenAiCompatible
                ? "openai"
                : provider.ProviderId;

    private static bool IsBuiltInOpenAiProvider(string providerId)
        => string.Equals(providerId, "openai", StringComparison.OrdinalIgnoreCase);

    private static string ToCodexWireApi(WireApi wireApi)
        => wireApi == WireApi.ChatCompletions ? "chat" : "responses";

    private static bool IsBareTomlKey(string value)
        => value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static string QuoteTomlKey(string value)
        => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void CleanupLegacyKeys(CodexConfigDocument document, string? preservedModelProviderSection)
    {
        document.RemoveTopLevelKey("oss_provider");
        document.RemoveTopLevelKey("model_catalog_json");
        document.RemoveTopLevelKey("preferred_auth_method");
        var legacySections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "model_providers.OpenAI",
            "model_providers.openai",
            "model_providers.OpenAI/openai",
            "model_providers.openai/openai"
        };
        document.RemoveSections(header =>
            legacySections.Contains(header) &&
            (string.IsNullOrWhiteSpace(preservedModelProviderSection) ||
             !string.Equals(header, preservedModelProviderSection, StringComparison.OrdinalIgnoreCase)));
    }

    private static JsonElement? ExtractPreservedTokens(JsonDocument? existingAuth)
    {
        if (existingAuth is null ||
            !existingAuth.RootElement.TryGetProperty("tokens", out var tokens) ||
            tokens.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return tokens.Clone();
    }

    private static DateTimeOffset? ExtractPreservedLastRefresh(JsonDocument? existingAuth)
    {
        if (existingAuth is null)
        {
            return null;
        }

        if (existingAuth.RootElement.TryGetProperty("last_refresh", out var lastRefresh) &&
            lastRefresh.ValueKind == JsonValueKind.String &&
            lastRefresh.TryGetDateTimeOffset(out var parsedLastRefresh))
        {
            return parsedLastRefresh;
        }

        if (existingAuth.RootElement.TryGetProperty("tokens", out var tokens) &&
            tokens.ValueKind == JsonValueKind.Object &&
            tokens.TryGetProperty("last_refresh", out var tokenLastRefresh) &&
            tokenLastRefresh.ValueKind == JsonValueKind.String &&
            tokenLastRefresh.TryGetDateTimeOffset(out parsedLastRefresh))
        {
            return parsedLastRefresh;
        }

        return null;
    }
}
