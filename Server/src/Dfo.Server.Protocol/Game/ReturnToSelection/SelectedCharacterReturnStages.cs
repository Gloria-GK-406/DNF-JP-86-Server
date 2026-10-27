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

public sealed record PreparedReturnToSelection(
    AccountId AccountId, NetworkConnectionId ConnectionId, long Generation,
    SelectedSessionIdentity? Session, AccountCharacterRoster Roster, bool ResetKnightDeck);

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
