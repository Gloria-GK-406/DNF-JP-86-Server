using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Connect;

public readonly record struct ChannelConnectCompleted;
public sealed record ChannelConnectCommand(NetworkDispatchContext Dispatch, ChannelConnectRequest Request)
    : IProtocolCommand<ChannelConnectCompleted>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Channel(Request.Metadata);
}

public sealed class ChannelConnectCommandHandler(TimeProvider timeProvider, ICommandMessageOutbox outbox)
    : ICommandHandler<ChannelConnectCommand, ChannelConnectCompleted>
{
    public async ValueTask<ChannelConnectCompleted> HandleAsync(CommandExecutionContext context,
        ChannelConnectCommand command, CancellationToken cancellationToken)
    {
        await outbox.RegisterAsync(command.Dispatch.Connection.ConnectionId,
            new ChannelConnectResponse(ChannelProtocolKey.ForLocalTime(timeProvider.GetLocalNow())), cancellationToken).ConfigureAwait(false);
        return default;
    }
}
