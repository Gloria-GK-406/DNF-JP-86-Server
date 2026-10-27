using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.StoryPause;

public sealed record GameStoryPauseCommand(NetworkDispatchContext Dispatch, StoryPauseRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameStoryPauseCommandHandler(
    ISelectedSessionDomainService selectedSessions,
    IGameCommandOutbox sender,
    ILogger<StoryPauseHandler> logger) : ICommandHandler<GameStoryPauseCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameStoryPauseCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Story pause {ConnectionId}: no published session, ignoring pause={Pause} type={Type}",
                context.Connection.ConnectionId,
                message.PauseFlag,
                message.RequestType);
            return false;
        }

        logger.LogInformation(
            "Story pause {ConnectionId} user={WireUserId}: pause={Pause} type={Type}",
            context.Connection.ConnectionId,
            session.WireUserId,
            message.PauseFlag,
            message.RequestType);

        await sender.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new StoryPauseNotification(session.WireUserId, message.PauseFlag, message.RequestType)],
                []),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
