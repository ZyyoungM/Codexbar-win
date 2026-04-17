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
        ApplySharedConfig(configDocument, appConfig.ModelSettings);

        string authContent;
        if (provider.Kind == ProviderKind.OpenAiOAuth)
        {
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
            configDocument.SetString("openai_base_url", provider.BaseUrl);
            authContent = _authStore.SerializeCompatibleApiKey(apiKey);
        }

        CleanupLegacyKeys(configDocument);
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
        document.SetString("model_provider", "openai");
        document.SetString("model", settings.Model);
        document.SetString("review_model", settings.ReviewModel);
        document.SetString("model_reasoning_effort", settings.ModelReasoningEffort);
    }

    private static void CleanupLegacyKeys(CodexConfigDocument document)
    {
        document.RemoveTopLevelKey("oss_provider");
        document.RemoveTopLevelKey("model_catalog_json");
        document.RemoveTopLevelKey("preferred_auth_method");
        document.RemoveSections(
            "model_providers.OpenAI",
            "model_providers.openai",
            "model_providers.OpenAI/openai",
            "model_providers.openai/openai");
    }
}

