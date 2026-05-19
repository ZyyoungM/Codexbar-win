using CodexBar.CodexCompat;
using CodexBar.Core;

namespace CodexBar.Application;

public sealed class GatewayResolutionWorkflow
{
    private readonly AppPaths _appPaths;
    private readonly IOAuthTokenStore _tokenStore;
    private readonly CodexHomeLocator? _homeLocator;

    public GatewayResolutionWorkflow(
        AppPaths appPaths,
        IOAuthTokenStore tokenStore,
        CodexHomeLocator? homeLocator = null)
    {
        _appPaths = appPaths;
        _tokenStore = tokenStore;
        _homeLocator = homeLocator;
    }

    public async Task<OpenAiAggregateGatewayDecision> ResolveAsync(
        AppConfig config,
        CodexSelection requestedSelection,
        IDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
        => await NewGatewayService().ResolveSelectionAsync(config, requestedSelection, environment, cancellationToken);

    public async Task<OpenAiAggregateGatewayDecision?> ResolvePreviewAsync(
        AppConfig config,
        string providerId = "openai",
        IDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (config.Settings.OpenAiAccountMode != OpenAiAccountMode.AggregateGateway)
        {
            return null;
        }

        var fallbackAccount = config.Accounts.FirstOrDefault(OpenAiQuotaPolicy.IsOpenAiOAuthAccount);
        var requested = config.ActiveSelection is not null &&
                        string.Equals(config.ActiveSelection.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
            ? config.ActiveSelection
            : fallbackAccount is null
                ? null
                : new CodexSelection { ProviderId = fallbackAccount.ProviderId, AccountId = fallbackAccount.AccountId };

        if (requested is null)
        {
            return null;
        }

        return await ResolveAsync(config, requested, environment, cancellationToken);
    }

    private OpenAiAggregateGatewayService NewGatewayService()
        => new(_appPaths, _tokenStore, _homeLocator);
}
