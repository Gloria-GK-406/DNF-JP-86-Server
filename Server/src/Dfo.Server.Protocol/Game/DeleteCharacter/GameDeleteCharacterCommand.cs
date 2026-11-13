using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DeleteCharacter;

public sealed record GameDeleteCharacterCommand(NetworkDispatchContext Dispatch, DeleteCharacterRequest Request)
    : IProtocolCommand<CharacterDeletionResult>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

public sealed class GameDeleteCharacterCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    ICharacterRosterDomainService characters,
    IGameCommandOutbox outbox,
    ICommandWorkUnits workUnits,
    ICharacterRelationDomainService relations,
    Dfo.Server.Abstractions.Auction.IAuctionQueryDomainService auctions,
    ICommandProjections projections,
    TimeProvider clock,
    ILogger<GameDeleteCharacterCommandHandler> logger) : ICommandHandler<GameDeleteCharacterCommand, CharacterDeletionResult>
{
    public async ValueTask<CharacterDeletionResult> HandleAsync(CommandExecutionContext context,
        GameDeleteCharacterCommand command, CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{command.Dispatch.Connection.ConnectionId}' is not authenticated.");
        }

        // Any selection on the account refuses, not only one on this connection. A login that
        // displaced another connection leaves that connection's selection to its disconnect
        // teardown, so a deletion arriving first must wait for it rather than slip past.
        var deletion = command.Request.RawName is null
            ? new Deletion(new CharacterDeletionResult(CharacterDeletionStatus.MalformedRequest, null), null)
            : sessions.Find(accountId) is not null
                ? new Deletion(new CharacterDeletionResult(CharacterDeletionStatus.CharacterSelected, null), null)
            : await workUnits.WriteAsync(context, new CommandWorkUnitDeclaration("delete-character", []),
                (unit, ct) => DeleteAsync(unit, accountId, command.Request.RosterIndex, command.Request.RawName, ct),
                cancellationToken).ConfigureAwait(false);
        var result = deletion.Result;

        // RequestIndex against ResolvedSlot is the open question in one line: they agree on every
        // gap-free roster, and the first disagreement proves the index is a position in the roster
        // the server sent rather than a slot. Logged on every deletion so the answer arrives on its
        // own rather than needing a staged experiment.
        logger.LogInformation(
            "Delete character {ConnectionId} account={AccountId} requestIndex={RequestIndex}: "
                + "{Status} character={CharacterId} resolvedSlot={ResolvedSlot}",
            command.Dispatch.Connection.ConnectionId,
            accountId,
            command.Request.RosterIndex,
            result.Status,
            result.CharacterId?.Value ?? 0,
            result.SlotIndex);

        // The acknowledgement is the whole reply. Deletion does not refresh the roster, and
        // creation does, because their acknowledgements differ: creation's carries no character,
        // so only a roster tells the client what was made and where, while deletion's carries the
        // id and the client removes that entry from its own view on receipt.
        //
        // Sending a roster anyway crashed the client. It had already dropped the entry, and the
        // full roster arriving behind that left it indexing a list it had just mutated. The
        // reference server sends the acknowledgement and stops; so does this.
        await outbox.RegisterCmdAsync(
            command.Dispatch.Connection.ConnectionId,
            new DeleteCharacterResponse(result.Status, command.Request.RosterIndex),
            cancellationToken).ConfigureAwait(false);

        // RQ-053: whoever kept the deleted character as a friend and is in the world now sees it go
        // from the list -- the removal line first, then the list and blacklist as they stand. The
        // name is the request's: it is the one the deletion matched against the stored name.
        if (deletion.Purge is { FriendObservers.Count: > 0 } purge && command.Request.RawName is { } name)
        {
            var observers = FriendObservers.InWorld(purge.FriendObservers, result.CharacterId!.Value, sessions);
            if (observers.Count > 0)
            {
                var audience = new NotificationAudience.TheseCharacters(observers);
                _ = await projections.ProjectAsync(audience, new FriendRemoved(name), cancellationToken).ConfigureAwait(false);
                _ = await projections.ProjectAsync(audience, new FriendListChanged(clock.LocalTimeZone), cancellationToken).ConfigureAwait(false);
            }
        }
        return result;
    }

    /// <remarks>
    /// One unit: a character that is gone keeps no relation and is kept in none, so the rows go with
    /// it or not at all. A refused deletion leaves them untouched.
    /// </remarks>
    private async ValueTask<Deletion> DeleteAsync(CommandWorkUnit unit, AccountId accountId, ushort rosterIndex, byte[] rawName,
        CancellationToken ct)
    {
        var result = await characters.DeleteCharacterAsync(accountId, rosterIndex, rawName, ct).ConfigureAwait(false);
        if (result is not { Status: CharacterDeletionStatus.Deleted, CharacterId: { } deleted }) return new(result, null);
        // Whatever the auction house holds for or from this character must keep somewhere to go back to.
        if (await auctions.HasOpenObligationsAsync(deleted, ct).ConfigureAwait(false))
        {
            unit.RequireRollback();
            return new(new CharacterDeletionResult(CharacterDeletionStatus.AuctionObligations, null), null);
        }
        return new(result, await relations.PurgeAsync(deleted, ct).ConfigureAwait(false));
    }

    /// <param name="Purge">The relations cleared with a deleted character, or nothing when none was deleted.</param>
    private sealed record Deletion(CharacterDeletionResult Result, CharacterRelationPurge? Purge);
}
