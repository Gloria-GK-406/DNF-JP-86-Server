using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.ScriptVersion;

public sealed class ChannelScriptVersionHandler(ICommandBus commandBus)
    : ClientMessageHandler<ChannelScriptVersionRequest>, IProtocolCommandMessageHandler<ChannelScriptVersionCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ChannelScriptVersionRequest message, CancellationToken cancellationToken) =>
        _ = await commandBus.SendAsync(new ChannelScriptVersionCommand(context, message), cancellationToken).ConfigureAwait(false);
}
