using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.MercenaryInfo;

public sealed record GameMercenaryInfoCommand(NetworkDispatchContext Dispatch, MercenaryInfoRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameMercenaryInfoCommandHandler(
    IGameCommandOutbox sender,
    ILogger<MercenaryInfoHandler> logger) : ICommandHandler<GameMercenaryInfoCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameMercenaryInfoCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        logger.LogInformation(
            "Mercenary info {ConnectionId}: replying with an empty expedition roster",
            context.Connection.ConnectionId);
        await sender.RegisterCmdAsync(
            context.Connection.ConnectionId,
            new MercenaryInfoResponse(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
