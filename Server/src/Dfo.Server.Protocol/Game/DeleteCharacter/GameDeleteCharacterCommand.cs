using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
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
    ICharacterDomainService characters,
    IGameCommandOutbox outbox,
    ICommandWorkUnits workUnits,
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

        var result = command.Request.RawName is null
            ? new CharacterDeletionResult(CharacterDeletionStatus.MalformedRequest, null)
            : await workUnits.WriteAsync(context, new CommandWorkUnitDeclaration("delete-character", []),
                (_, ct) => characters.DeleteCharacterAsync(accountId, command.Request.RosterIndex, command.Request.RawName, ct),
                cancellationToken).ConfigureAwait(false);

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
        return result;
    }
}
