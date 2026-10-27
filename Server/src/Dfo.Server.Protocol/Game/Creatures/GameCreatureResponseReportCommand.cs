using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.Creatures;

internal sealed record GameCreatureResponseReportCommand(NetworkDispatchContext Dispatch, CreatureResponseReport Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameCreatureResponseReportCommandHandler(
    ISelectedSessionDomainService sessions, ILogger<CreatureResponseReportHandler> logger) : ICommandHandler<GameCreatureResponseReportCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameCreatureResponseReportCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var selected = sessions.FindByConnection(context.Connection.ConnectionId);
        logger.LogDebug("Creature response report actor={ActorKey} selected={Selected}",
            message.ActorKey, selected is not null);
        return ValueTask.FromResult(true);
    }
}
