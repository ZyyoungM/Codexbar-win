using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using CodexBar.Core;

namespace CodexBar.Auth;

public sealed record OpenAiWorkspaceDescriptor(
    string WorkspaceId,
    string WorkspaceName,
    string? WorkspaceType,
    string? SeatType,
    string? QuotaScopeKey,
    bool IsCurrent)
{
    public string DisplayLabel
    {
        get
        {
            var parts = new List<string> { WorkspaceName };
            if (!string.IsNullOrWhiteSpace(WorkspaceType))
            {
                parts.Add(WorkspaceType);
            }

            if (!string.IsNullOrWhiteSpace(SeatType))
            {
                parts.Add(SeatType);
            }

            return string.Join(" · ", parts);
        }
    }

    public OAuthTokens ApplyTo(OAuthTokens tokens)
        => tokens with { AccountId = WorkspaceId };
}

public static class OpenAiWorkspaceDiscovery
{
    private static readonly Uri AccountsEndpoint = new("https://chatgpt.com/backend-api/accounts");

    public static IReadOnlyList<OpenAiWorkspaceDescriptor> Discover(OAuthTokens tokens, OAuthIdentity identity)
    {
        var currentWorkspaceId = EmptyToNull(tokens.AccountId);
        var descriptors = new List<OpenAiWorkspaceDescriptor>();

        if (!string.IsNullOrWhiteSpace(tokens.IdToken))
        {
            TryCollectFromJwt(tokens.IdToken, currentWorkspaceId, descriptors);
        }

        if (tokens.ExtensionData is not null)
        {
            foreach (var value in tokens.ExtensionData.Values)
            {
                CollectFromElement(value, currentWorkspaceId, descriptors);
            }
        }

        if (currentWorkspaceId is not null &&
            descriptors.All(item => !TextEquals(item.WorkspaceId, currentWorkspaceId)))
        {
            descriptors.Add(new OpenAiWorkspaceDescriptor(
                currentWorkspaceId,
                "Current workspace",
                null,
                null,
                null,
                true));
        }

        if (descriptors.Count == 0)
        {
            var fallbackId = FirstNonEmpty(identity.SubjectId, identity.Email, Guid.NewGuid().ToString("N"))!;
            descriptors.Add(new OpenAiWorkspaceDescriptor(
                fallbackId,
                "OpenAI",
                null,
                null,
                null,
                true));
        }

        return MergeDescriptors(descriptors, currentWorkspaceId);
    }

    public static async Task<IReadOnlyList<OpenAiWorkspaceDescriptor>> DiscoverAsync(
        OAuthTokens tokens,
        OAuthIdentity identity,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        var currentWorkspaceId = EmptyToNull(tokens.AccountId);
        var descriptors = Discover(tokens, identity).ToList();
        var chatGptWorkspace = ReadChatGptWorkspaceFromJwt(tokens.IdToken, currentWorkspaceId);
        if (!string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            var chatGptAccounts = await FetchChatGptAccountsAsync(tokens, httpClient, cancellationToken);
            if (chatGptAccounts.Count > 0)
            {
                currentWorkspaceId = ResolveChatGptCurrentWorkspaceId(tokens, chatGptAccounts);
                return MergeDescriptors(MarkCurrent(chatGptAccounts, currentWorkspaceId), currentWorkspaceId);
            }
        }

        if (chatGptWorkspace is not null)
        {
            return MergeDescriptors([chatGptWorkspace], chatGptWorkspace.WorkspaceId);
        }

        return MergeDescriptors(descriptors, currentWorkspaceId);
    }

    public static OpenAiWorkspaceDescriptor CurrentOrFallback(OAuthTokens tokens, OAuthIdentity identity)
    {
        var discovered = Discover(tokens, identity);
        return discovered.FirstOrDefault(item => item.IsCurrent) ?? discovered.First();
    }

    public static async Task<OpenAiWorkspaceDescriptor> CurrentOrFallbackAsync(
        OAuthTokens tokens,
        OAuthIdentity identity,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        var discovered = await DiscoverAsync(tokens, identity, httpClient, cancellationToken);
        return discovered.FirstOrDefault(item => item.IsCurrent) ?? discovered.First();
    }

    public static async Task<OpenAiWorkspaceDescriptor> ResolveCurrentForSaveAsync(
        OAuthTokens tokens,
        OAuthIdentity identity,
        OpenAiWorkspaceDescriptor? selectedWorkspaceHint = null,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        var discovered = await DiscoverAsync(tokens, identity, httpClient, cancellationToken);
        return ResolveCurrentForSave(discovered, tokens, selectedWorkspaceHint);
    }

    public static OpenAiWorkspaceDescriptor ResolveCurrentForSave(
        IReadOnlyList<OpenAiWorkspaceDescriptor> discovered,
        OAuthTokens tokens,
        OpenAiWorkspaceDescriptor? selectedWorkspaceHint = null)
    {
        var tokenAccountId = EmptyToNull(tokens.AccountId);
        var hintedId = EmptyToNull(selectedWorkspaceHint?.WorkspaceId);
        var workspace = discovered.FirstOrDefault(item =>
                            hintedId is not null &&
                            TextEquals(item.WorkspaceId, hintedId)) ??
                        discovered.FirstOrDefault(item => item.IsCurrent) ??
                        discovered.FirstOrDefault(item => TextEquals(item.WorkspaceId, tokenAccountId)) ??
                        selectedWorkspaceHint ??
                        discovered.FirstOrDefault();

        if (workspace is not null)
        {
            return MergeSelectedHint(workspace, selectedWorkspaceHint);
        }

        var fallbackId = FirstNonEmpty(tokenAccountId, hintedId, Guid.NewGuid().ToString("N"))!;
        return new OpenAiWorkspaceDescriptor(fallbackId, "OpenAI", null, null, null, true);
    }

    private static IReadOnlyList<OpenAiWorkspaceDescriptor> MergeDescriptors(
        IReadOnlyList<OpenAiWorkspaceDescriptor> descriptors,
        string? currentWorkspaceId)
        => descriptors
            .GroupBy(item => item.WorkspaceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => Merge(group.ToList(), currentWorkspaceId))
            .OrderByDescending(item => item.IsCurrent)
            .ThenBy(item => item.WorkspaceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static OpenAiWorkspaceDescriptor Merge(
        IReadOnlyList<OpenAiWorkspaceDescriptor> descriptors,
        string? currentWorkspaceId)
    {
        var first = descriptors.First();
        return new OpenAiWorkspaceDescriptor(
            first.WorkspaceId,
            FirstNonEmpty(descriptors
                .Select(item => IsGenericWorkspaceName(item.WorkspaceName) ? null : item.WorkspaceName)
                .ToArray()) ??
            FirstNonEmpty(descriptors.Select(item => item.WorkspaceName).ToArray()) ??
            first.WorkspaceId,
            FirstNonEmpty(descriptors.Select(item => item.WorkspaceType).ToArray()),
            FirstNonEmpty(descriptors.Select(item => item.SeatType).ToArray()),
            FirstNonEmpty(descriptors.Select(item => item.QuotaScopeKey).ToArray()),
            descriptors.Any(item => item.IsCurrent) || TextEquals(first.WorkspaceId, currentWorkspaceId));
    }

    private static OpenAiWorkspaceDescriptor MergeSelectedHint(
        OpenAiWorkspaceDescriptor workspace,
        OpenAiWorkspaceDescriptor? selectedWorkspaceHint)
    {
        if (selectedWorkspaceHint is null ||
            !TextEquals(workspace.WorkspaceId, selectedWorkspaceHint.WorkspaceId))
        {
            return workspace;
        }

        return new OpenAiWorkspaceDescriptor(
            workspace.WorkspaceId,
            PreferHintWorkspaceName(workspace.WorkspaceName, selectedWorkspaceHint.WorkspaceName),
            FirstNonEmpty(selectedWorkspaceHint.WorkspaceType, workspace.WorkspaceType),
            FirstNonEmpty(selectedWorkspaceHint.SeatType, workspace.SeatType),
            FirstNonEmpty(workspace.QuotaScopeKey, selectedWorkspaceHint.QuotaScopeKey),
            workspace.IsCurrent || selectedWorkspaceHint.IsCurrent);
    }

    private static string PreferHintWorkspaceName(string workspaceName, string hintName)
        => !string.IsNullOrWhiteSpace(hintName) &&
           !TextEquals(hintName, "Current workspace") &&
           !TextEquals(hintName, "OpenAI")
            ? hintName.Trim()
            : workspaceName;

    private static bool IsGenericWorkspaceName(string? value)
        => TextEquals(value, "Current workspace") ||
           TextEquals(value, "OpenAI");

    private static IReadOnlyList<OpenAiWorkspaceDescriptor> MarkCurrent(
        IReadOnlyList<OpenAiWorkspaceDescriptor> descriptors,
        string? currentWorkspaceId)
        => descriptors
            .Select(item => item with { IsCurrent = TextEquals(item.WorkspaceId, currentWorkspaceId) })
            .ToList();

    private static string? ResolveChatGptCurrentWorkspaceId(
        OAuthTokens tokens,
        IReadOnlyList<OpenAiWorkspaceDescriptor> chatGptAccounts)
    {
        var tokenAccountId = EmptyToNull(tokens.AccountId);
        if (chatGptAccounts.Any(account => TextEquals(account.WorkspaceId, tokenAccountId)))
        {
            return tokenAccountId;
        }

        var claimAccountId = ReadChatGptAccountIdFromJwt(tokens.IdToken);
        if (chatGptAccounts.Any(account => TextEquals(account.WorkspaceId, claimAccountId)))
        {
            return claimAccountId;
        }

        return chatGptAccounts.FirstOrDefault(account => account.IsCurrent)?.WorkspaceId ??
               chatGptAccounts.First().WorkspaceId;
    }

    private static async Task<IReadOnlyList<OpenAiWorkspaceDescriptor>> FetchChatGptAccountsAsync(
        OAuthTokens tokens,
        HttpClient? httpClient,
        CancellationToken cancellationToken)
    {
        try
        {
            using var ownedClient = httpClient is null
                ? new HttpClient { Timeout = TimeSpan.FromSeconds(10) }
                : null;
            var client = httpClient ?? ownedClient!;
            using var request = new HttpRequestMessage(HttpMethod.Get, AccountsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("originator", "Codex Desktop");
            var chatGptAccountId = ReadChatGptAccountIdFromJwt(tokens.IdToken) ?? EmptyToNull(tokens.AccountId);
            if (chatGptAccountId is not null)
            {
                request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", chatGptAccountId);
            }

            request.Headers.UserAgent.ParseAdd("CodexBar.Win/0.1 Codex");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var descriptors = new List<OpenAiWorkspaceDescriptor>();
            foreach (var item in items.EnumerateArray())
            {
                var accountId = ReadString(item, "id", "account_id", "accountId");
                if (accountId is null)
                {
                    continue;
                }

                var structure = NormalizeLower(ReadString(item, "structure", "type", "kind"));
                var name = ReadString(item, "name", "title", "display_name", "displayName");
                var role = NormalizeLower(ReadString(item, "current_user_role", "currentUserRole", "role"));
                descriptors.Add(new OpenAiWorkspaceDescriptor(
                    accountId,
                    name ?? (string.Equals(structure, "personal", StringComparison.OrdinalIgnoreCase) ? "Personal" : accountId),
                    structure,
                    role,
                    ReadString(item, "quota_scope_key", "quotaScopeKey", "billing_scope", "billingScope"),
                    TextEquals(accountId, tokens.AccountId)));
            }

            return descriptors;
        }
        catch
        {
            return [];
        }
    }

    private static void TryCollectFromJwt(
        string idToken,
        string? currentWorkspaceId,
        List<OpenAiWorkspaceDescriptor> descriptors)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return;
            }

            var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var document = JsonDocument.Parse(payload);
            CollectFromElement(document.RootElement, currentWorkspaceId, descriptors);
        }
        catch
        {
            // Workspace discovery is best-effort; OAuth identity still works without it.
        }
    }

    private static string? ReadChatGptAccountIdFromJwt(string? idToken)
        => ReadChatGptWorkspaceFromJwt(idToken, null)?.WorkspaceId;

    private static OpenAiWorkspaceDescriptor? ReadChatGptWorkspaceFromJwt(
        string? idToken,
        string? currentWorkspaceId)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var document = JsonDocument.Parse(payload);
            var authClaim = FindObject(document.RootElement, "https://api.openai.com/auth");
            if (authClaim is null)
            {
                return null;
            }

            var accountId = ReadString(authClaim.Value, "chatgpt_account_id", "chatgptAccountId");
            if (accountId is null)
            {
                return null;
            }

            var planType = NormalizeLower(ReadString(authClaim.Value, "chatgpt_plan_type", "chatgptPlanType"));
            var workspaceName = IsPersonalPlan(planType) ? "Personal" : "Current workspace";
            return new OpenAiWorkspaceDescriptor(
                accountId,
                workspaceName,
                planType,
                null,
                ReadString(authClaim.Value, "quota_scope_key", "quotaScopeKey"),
                currentWorkspaceId is null || TextEquals(accountId, currentWorkspaceId));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPersonalPlan(string? planType)
        => TextEquals(planType, "free") ||
           TextEquals(planType, "plus") ||
           TextEquals(planType, "pro");

    private static void CollectFromElement(
        JsonElement element,
        string? currentWorkspaceId,
        List<OpenAiWorkspaceDescriptor> descriptors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryCreateDescriptor(element, currentWorkspaceId, out var descriptor))
                {
                    descriptors.Add(descriptor);
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        CollectFromElement(property.Value, currentWorkspaceId, descriptors);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectFromElement(item, currentWorkspaceId, descriptors);
                }

                break;
        }
    }

    private static bool TryCreateDescriptor(
        JsonElement element,
        string? currentWorkspaceId,
        out OpenAiWorkspaceDescriptor descriptor)
    {
        descriptor = default!;
        var workspaceId = ReadString(
            element,
            "account_id",
            "accountId",
            "workspace_id",
            "workspaceId",
            "organization_account_id",
            "organizationAccountId",
            "id",
            "organization_id",
            "organizationId",
            "org_id",
            "orgId");
        if (workspaceId is null)
        {
            return false;
        }

        var workspaceName = ReadString(
            element,
            "workspace_name",
            "workspaceName",
            "organization_name",
            "organizationName",
            "org_name",
            "orgName",
            "display_name",
            "displayName",
            "name",
            "title",
            "slug");
        var workspaceType = ReadString(
            element,
            "workspace_type",
            "workspaceType",
            "plan_type",
            "planType",
            "type",
            "kind");
        var seatType = ReadString(
            element,
            "seat_type",
            "seatType",
            "seat",
            "role");
        var quotaScopeKey = ReadString(
            element,
            "quota_scope_key",
            "quotaScopeKey",
            "quota_scope",
            "quotaScope",
            "billing_scope",
            "billingScope");

        if (workspaceName is null &&
            workspaceType is null &&
            seatType is null &&
            quotaScopeKey is null &&
            !TextEquals(workspaceId, currentWorkspaceId))
        {
            return false;
        }

        descriptor = new OpenAiWorkspaceDescriptor(
            workspaceId,
            workspaceName ?? (TextEquals(workspaceId, currentWorkspaceId) ? "Current workspace" : workspaceId),
            NormalizeLower(workspaceType),
            NormalizeLower(seatType),
            quotaScopeKey,
            TextEquals(workspaceId, currentWorkspaceId));
        return true;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return EmptyToNull(value.GetString());
            }

            if (value.ValueKind == JsonValueKind.Number ||
                value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return EmptyToNull(value.ToString());
            }
        }

        return null;
    }

    private static string? FindString(JsonElement element, params string[] names)
    {
        var direct = element.ValueKind == JsonValueKind.Object ? ReadString(element, names) : null;
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .Select(property => FindString(property.Value, names))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            JsonValueKind.Array => element
                .EnumerateArray()
                .Select(item => FindString(item, names))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            _ => null
        };
    }

    private static JsonElement? FindObject(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.Object)
                {
                    return value;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindObject(property.Value, names);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindObject(item, names);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static bool TextEquals(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeLower(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
