using CodexBar.Core;

namespace CodexBar.Runtime;

public static class CodexLaunchEnvironmentBuilder
{
    public static async Task<IReadOnlyDictionary<string, string>> BuildAsync(
        AppConfig config,
        ISecretStore secretStore,
        CancellationToken cancellationToken = default)
    {
        if (config.ActiveSelection is null)
        {
            return new Dictionary<string, string>();
        }

        var selection = config.ActiveSelection;
        var provider = config.Providers.FirstOrDefault(p =>
            string.Equals(p.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (provider?.Kind != ProviderKind.OpenAiCompatible)
        {
            return new Dictionary<string, string>();
        }

        var account = config.Accounts.FirstOrDefault(a =>
            string.Equals(a.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.AccountId, selection.AccountId, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            return new Dictionary<string, string>();
        }

        var apiKey = await secretStore.ReadSecretAsync(account.CredentialRef, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new Dictionary<string, string>();
        }

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OPENAI_API_KEY"] = apiKey
        };
        if (!string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            environment["OPENAI_BASE_URL"] = provider.BaseUrl;
        }

        return environment;
    }
}
