using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Info;

public readonly record struct ChannelInfoCompleted;
public sealed record ChannelInfoCommand(NetworkDispatchContext Dispatch, ChannelInfoRequest Request)
    : IProtocolCommand<ChannelInfoCompleted>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Channel(Request.Metadata);
}

public sealed class ChannelInfoCommandHandler(IChannelDirectoryDomainService channels, ICommandMessageOutbox outbox)
    : ICommandHandler<ChannelInfoCommand, ChannelInfoCompleted>
{
    public async ValueTask<ChannelInfoCompleted> HandleAsync(CommandExecutionContext context,
        ChannelInfoCommand command, CancellationToken cancellationToken)
    {
        await outbox.RegisterAsync(command.Dispatch.Connection.ConnectionId,
            new ChannelInfoResponse(await channels.ChannelsAsync(cancellationToken).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false);
        return default;
    }
}
