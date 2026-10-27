using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SaveCharacterOption;

public sealed record GameSaveCharacterOptionCommand(NetworkDispatchContext Dispatch, SaveCharacterOptionRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameSaveCharacterOptionCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICharacterOptionsDomainService options, ICommandWorkUnits units, ILogger<GameSaveCharacterOptionCommandHandler> logger)
    : ICommandHandler<GameSaveCharacterOptionCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSaveCharacterOptionCommand command, CancellationToken ct)
    {
        var context = command.Dispatch; var message = command.Input;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user is null) return false;
        var saved = await units.WriteAsync(execution, new("SaveCharacterOption", [new(session, context.Connection, message.Metadata)]),
            (_, token) => options.SaveAsync(session, message.Body, token), ct).ConfigureAwait(false);
        logger.LogInformation("Character options character={CharacterId} bytes={Bytes} saved={Saved}", session.CharacterId.Value, message.Body.Length, saved);
        // No CMD ACK: the client has no response handler for this upload.
        return saved;
    }
}
