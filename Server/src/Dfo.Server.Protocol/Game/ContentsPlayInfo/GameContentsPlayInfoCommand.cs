using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.ContentsPlayInfo;

public sealed record GameContentsPlayInfoCommand(NetworkDispatchContext Dispatch, ContentsPlayInfoRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameContentsPlayInfoCommandHandler(
    IGameCommandOutbox sender,
    ILogger<ContentsPlayInfoHandler> logger) : ICommandHandler<GameContentsPlayInfoCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameContentsPlayInfoCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        logger.LogInformation(
            "Contents play info {ConnectionId}: selector={Selector}, replying with a zeroed record",
            context.Connection.ConnectionId,
            message.Selector);
        await sender.RegisterCmdAsync(
            context.Connection.ConnectionId,
            new ContentsPlayInfoResponse(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
