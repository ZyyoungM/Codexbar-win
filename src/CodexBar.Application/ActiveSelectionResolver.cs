using CodexBar.Core;

namespace CodexBar.Application;

public enum ActiveSelectionResolutionStatus
{
    Ready,
    MissingSelection,
    MissingAccount
}

public sealed record ActiveSelectionResolution(
    ActiveSelectionResolutionStatus Status,
    CodexSelection? Selection,
    AccountRecord? Account);

public static class ActiveSelectionResolver
{
    public static ActiveSelectionResolution Resolve(AppConfig config)
    {
        if (config.ActiveSelection is null)
        {
            return new ActiveSelectionResolution(
                ActiveSelectionResolutionStatus.MissingSelection,
                null,
                null);
        }

        var selection = config.ActiveSelection;
        var account = config.Accounts.FirstOrDefault(account =>
            string.Equals(account.ProviderId, selection.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(account.AccountId, selection.AccountId, StringComparison.OrdinalIgnoreCase));

        return account is null
            ? new ActiveSelectionResolution(ActiveSelectionResolutionStatus.MissingAccount, selection, null)
            : new ActiveSelectionResolution(ActiveSelectionResolutionStatus.Ready, selection, account);
    }
}
