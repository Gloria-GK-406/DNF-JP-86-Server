using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.ScriptVersion;

public readonly record struct ChannelScriptVersionCompleted;
public sealed record ChannelScriptVersionCommand(NetworkDispatchContext Dispatch, ChannelScriptVersionRequest Request)
    : IProtocolCommand<ChannelScriptVersionCompleted>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Channel(Request.Metadata);
}

public sealed class ChannelScriptVersionCommandHandler(IChannelDirectoryDomainService channels, ICommandMessageOutbox outbox)
    : ICommandHandler<ChannelScriptVersionCommand, ChannelScriptVersionCompleted>
{
    public async ValueTask<ChannelScriptVersionCompleted> HandleAsync(CommandExecutionContext context,
        ChannelScriptVersionCommand command, CancellationToken cancellationToken)
    {
        await outbox.RegisterAsync(command.Dispatch.Connection.ConnectionId,
            new ChannelScriptVersionResponse(channels.ScriptVersion), cancellationToken).ConfigureAwait(false);
        return default;
    }
}
