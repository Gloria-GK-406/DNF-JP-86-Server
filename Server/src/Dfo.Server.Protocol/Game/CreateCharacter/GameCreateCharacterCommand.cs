using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Protocol.Game.AccountRoster;

namespace Dfo.Server.Protocol.Game.CreateCharacter;

public sealed record GameCreateCharacterCommand(NetworkDispatchContext Dispatch, CreateCharacterRequest Request)
    : IProtocolCommand<CharacterCreationResult>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

public sealed class GameCreateCharacterCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ICharacterRosterDomainService characters,
    IGameCommandOutbox outbox,
    ICommandWorkUnits workUnits,
    ILogger<GameCreateCharacterCommandHandler> logger) : ICommandHandler<GameCreateCharacterCommand, CharacterCreationResult>
{
    public async ValueTask<CharacterCreationResult> HandleAsync(CommandExecutionContext context,
        GameCreateCharacterCommand command, CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{command.Dispatch.Connection.ConnectionId}' is not authenticated.");
        }

        var result = command.Request.RawName is null
            ? new CharacterCreationResult(CharacterCreationStatus.MalformedRequest, null)
            : await workUnits.WriteAsync(context, new CommandWorkUnitDeclaration("create-character", []),
                (_, ct) => characters.CreateCharacterAsync(accountId, command.Request.Job, command.Request.RawName, ct),
                cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Create character {ConnectionId} account={AccountId} job={Job}: {Status} character={CharacterId}",
            command.Dispatch.Connection.ConnectionId,
            accountId,
            command.Request.Job,
            result.Status,
            result.CharacterId?.Value ?? 0);

        await outbox.RegisterCmdAsync(
            command.Dispatch.Connection.ConnectionId,
            new CreateCharacterResponse(result.Status),
            cancellationToken).ConfigureAwait(false);

        if (result.Status != CharacterCreationStatus.Created)
        {
            return result;
        }

        var roster = await characters.GetAccountRosterWithAppearanceAsync(accountId, cancellationToken).ConfigureAwait(false);
        await outbox.RegisterNotiAsync(
            command.Dispatch.Connection.ConnectionId,
            new AccountCharacterRosterResponse(roster, AnswersRosterQuery: false),
            cancellationToken).ConfigureAwait(false);
        return result;
    }
}
