using CodexBar.CodexCompat;
using CodexBar.Core;

namespace CodexBar.Application;

public sealed record AccountActivationWorkflowResult(
    CodexSwitchResult SwitchResult,
    AppConfig UpdatedConfig,
    OpenAiAggregateGatewayDecision GatewayDecision);

public sealed class AccountActivationWorkflow
{
    private readonly AppPaths _appPaths;
    private readonly AppConfigStore _configStore;
    private readonly ISecretStore _secretStore;
    private readonly IOAuthTokenStore _tokenStore;
    private readonly CodexHomeLocator _homeLocator;

    public AccountActivationWorkflow(
        AppPaths appPaths,
        AppConfigStore configStore,
        ISecretStore secretStore,
        IOAuthTokenStore tokenStore,
        CodexHomeLocator? homeLocator = null)
    {
        _appPaths = appPaths;
        _configStore = configStore;
        _secretStore = secretStore;
        _tokenStore = tokenStore;
        _homeLocator = homeLocator ?? new CodexHomeLocator();
    }

    public async Task<AccountActivationWorkflowResult> ActivateAsync(
        AppConfig config,
        CodexSelection requestedSelection,
        IDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var gatewayDecision = await new OpenAiAggregateGatewayService(_appPaths, _tokenStore, _homeLocator)
            .ResolveSelectionAsync(config, requestedSelection, cancellationToken: cancellationToken);
        var switchResult = await NewActivationService()
            .ActivateAsync(config, gatewayDecision.ResolvedSelection, environment, cancellationToken);
        var journalMessage = gatewayDecision.WasRerouted
            ? $"{gatewayDecision.Message} {switchResult.Message}"
            : switchResult.Message;
        await new SwitchJournalStore(_appPaths.SwitchJournalPath)
            .AppendAsync(switchResult.Selection, switchResult.ValidationPassed ? "ok" : "failed", journalMessage, cancellationToken);

        if (!switchResult.ValidationPassed)
        {
            return new AccountActivationWorkflowResult(switchResult, config, gatewayDecision);
        }

        var updatedConfig = config with
        {
            ActiveSelection = switchResult.Selection,
            Accounts = config.Accounts
                .Select(account => AccountMatches(account, switchResult.Selection)
                    ? account with { LastUsedAt = DateTimeOffset.UtcNow }
                    : account)
                .ToList()
        };
        await _configStore.SaveAsync(updatedConfig, cancellationToken);
        return new AccountActivationWorkflowResult(switchResult, updatedConfig, gatewayDecision);
    }

    private CodexActivationService NewActivationService()
        => new(
            _homeLocator,
            new CodexConfigStore(),
            new CodexAuthStore(),
            new CodexStateTransaction(_appPaths),
            new CodexIntegrityChecker(),
            _secretStore,
            _tokenStore);

    private static bool AccountMatches(AccountRecord account, CodexSelection selection)
        => string.Equals(account.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(account.AccountId, selection.AccountId, StringComparison.OrdinalIgnoreCase);
}
