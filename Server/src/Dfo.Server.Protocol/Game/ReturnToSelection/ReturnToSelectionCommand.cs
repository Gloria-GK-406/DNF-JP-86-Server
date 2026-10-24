using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.ReturnToSelection;

public sealed record PrepareReturnToSelectionCommand(
    AccountId AccountId, NetworkConnectionId ConnectionId, long Generation)
    : ICommand<PreparedReturnToSelection>, IAccountCommand;

public sealed record PreparedReturnToSelection(
    AccountId AccountId, NetworkConnectionId ConnectionId, long Generation,
    SelectedSessionIdentity? Session, AccountCharacterRoster Roster, bool ResetKnightDeck);

// Inventory writes settle before teardown. A failed save leaves the selected session intact.
public sealed class PrepareReturnToSelectionCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    ICharacterDomainService characters)
    : ICommandHandler<PrepareReturnToSelectionCommand, PreparedReturnToSelection>
{
    public async ValueTask<PreparedReturnToSelection> HandleAsync(
        CommandExecutionContext context, PrepareReturnToSelectionCommand command, CancellationToken cancellationToken)
    {
        ReturnToSelectionOwnership.Require(onlineUsers, sessions,
            command.AccountId, command.ConnectionId, command.Generation);
        var session = sessions.Find(command.AccountId);
        var roster = await characters.GetAccountRosterWithAppearanceAsync(command.AccountId, cancellationToken)
            .ConfigureAwait(false);
        var resetKnight = session is not null
            && roster.Characters.Any(character => character.CharacterId == session.CharacterId && character.Job == 12);
        return new(command.AccountId, command.ConnectionId, command.Generation, session, roster, resetKnight);
    }
}

public sealed record CompleteReturnToSelectionCommand(PreparedReturnToSelection Prepared)
    : ICommand<CompletedReturnToSelection>, IAccountCommand
{
    public AccountId AccountId => Prepared.AccountId;
}

public sealed record CompletedReturnToSelection(SelectedSessionCleanupNotificationPlan? Notifications);

// Cleanup persists position independently and attempts every exact removal even on failure.
// An outer transaction would undo its successful writes when it reports an aggregate failure.
public sealed class CompleteReturnToSelectionCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    ISelectedSessionCleanupDomainService cleanup)
    : ICommandHandler<CompleteReturnToSelectionCommand, CompletedReturnToSelection>
{
    public async ValueTask<CompletedReturnToSelection> HandleAsync(
        CommandExecutionContext context, CompleteReturnToSelectionCommand command, CancellationToken cancellationToken)
    {
        var prepared = command.Prepared;
        ReturnToSelectionOwnership.Require(onlineUsers, sessions,
            prepared.AccountId, prepared.ConnectionId, prepared.Generation);
        // Teardown asks whether the record is still there to release, revoked or not; with nothing
        // prepared, that there is still nothing.
        if (prepared.Session is { } held
                ? sessions.StandingOf(held) is null
                : sessions.Find(prepared.AccountId) is not null)
            throw new InvalidOperationException("Return-to-selection session changed after preparation.");
        if (prepared.Session is not { } session)
            return new(null);
        return new(await cleanup.CleanupAsync(session, includeOwner: false, context.LegacyFootprint, cancellationToken)
            .ConfigureAwait(false));
    }
}

internal static class ReturnToSelectionOwnership
{
    public static void Require(IOnlineUserQueryDomainService users, ISelectedSessionDomainService sessions,
        AccountId accountId, NetworkConnectionId connectionId, long generation)
    {
        if (!users.TryGetByConnection(connectionId, out var online)
            || online?.AccountId != accountId || online.Generation != generation
            || (sessions.Find(accountId) is { } selected && selected.ConnectionId != connectionId))
            throw new InvalidOperationException("Return-to-selection connection is stale or unauthenticated.");
    }
}
