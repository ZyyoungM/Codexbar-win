using CodexBar.Auth;
using CodexBar.Core;

namespace CodexBar.Application;

public sealed record CompatibleDraftProbeRequest
{
    public required string ProviderId { get; init; }
    public string? CodexProviderId { get; init; }
    public string? ProviderName { get; init; }
    public required string BaseUrl { get; init; }
    public required string AccountId { get; init; }
    public string? AccountLabel { get; init; }
    public string? ApiKey { get; init; }
    public string? CredentialRef { get; init; }
}

public sealed class CompatibleDraftProbeWorkflow
{
    private readonly ISecretStore? _savedSecretStore;

    public CompatibleDraftProbeWorkflow(ISecretStore? savedSecretStore = null)
    {
        _savedSecretStore = savedSecretStore;
    }

    public async Task<CompatibleProviderProbeResult> ProbeAsync(
        CompatibleDraftProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        var credentialRef = string.IsNullOrWhiteSpace(request.CredentialRef)
            ? $"api-key:{request.ProviderId}:{request.AccountId}"
            : request.CredentialRef.Trim();
        var store = await CreateProbeSecretStoreAsync(request, credentialRef, cancellationToken);

        var provider = new ProviderDefinition
        {
            ProviderId = request.ProviderId,
            CodexProviderId = request.CodexProviderId,
            DisplayName = string.IsNullOrWhiteSpace(request.ProviderName) ? request.ProviderId : request.ProviderName,
            Kind = ProviderKind.OpenAiCompatible,
            AuthMode = AuthMode.ApiKey,
            BaseUrl = request.BaseUrl,
            WireApi = WireApi.Responses,
            SupportsMultiAccount = true
        };
        var account = new AccountRecord
        {
            ProviderId = request.ProviderId,
            AccountId = request.AccountId,
            Label = string.IsNullOrWhiteSpace(request.AccountLabel) ? request.AccountId : request.AccountLabel,
            CredentialRef = credentialRef
        };

        return await new CompatibleProviderProbeService(store).ProbeAccountAsync(provider, account, cancellationToken);
    }

    private async Task<ISecretStore> CreateProbeSecretStoreAsync(
        CompatibleDraftProbeRequest request,
        string credentialRef,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return _savedSecretStore ?? new InMemorySecretStore();
        }

        var draftStore = new InMemorySecretStore();
        await draftStore.WriteSecretAsync(credentialRef, request.ApiKey, cancellationToken);
        return draftStore;
    }
}
