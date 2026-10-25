using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MapEntryReports;

public sealed record GameSecurityStatusCommand(NetworkDispatchContext Dispatch, SecurityStatusReport Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameSecurityStatusCommandHandler(
    IGameCommandOutbox sender,
    ILogger<SecurityStatusHandler> logger) : ICommandHandler<GameSecurityStatusCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameSecurityStatusCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        logger.LogInformation(
            "Security status {ConnectionId}: {Body}",
            context.Connection.ConnectionId,
            Convert.ToHexString(message.Body));
        await sender.RegisterCmdAsync(
            context.Connection.ConnectionId,
            new SecurityStatusAcknowledgement(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
