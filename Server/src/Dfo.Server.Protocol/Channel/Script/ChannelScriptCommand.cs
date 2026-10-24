using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Script;

public readonly record struct ChannelScriptCompleted;
public sealed record ChannelScriptCommand(NetworkDispatchContext Dispatch, ChannelScriptRequest Request)
    : IProtocolCommand<ChannelScriptCompleted>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Channel(Request.Metadata);
}

public sealed class ChannelScriptCommandHandler(IChannelDirectoryDomainService channels, ICommandMessageOutbox outbox)
    : ICommandHandler<ChannelScriptCommand, ChannelScriptCompleted>
{
    public async ValueTask<ChannelScriptCompleted> HandleAsync(CommandExecutionContext context,
        ChannelScriptCommand command, CancellationToken cancellationToken)
    {
        await outbox.RegisterAsync(command.Dispatch.Connection.ConnectionId,
            new ChannelScriptResponse(await channels.ReadScriptAsync(cancellationToken).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false);
        return default;
    }
}
