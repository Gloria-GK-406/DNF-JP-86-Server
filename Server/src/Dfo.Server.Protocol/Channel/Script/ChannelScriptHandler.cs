using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Script;

public sealed class ChannelScriptHandler(ICommandBus commandBus)
    : ClientMessageHandler<ChannelScriptRequest>, IProtocolCommandMessageHandler<ChannelScriptCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ChannelScriptRequest message, CancellationToken cancellationToken) =>
        _ = await commandBus.SendAsync(new ChannelScriptCommand(context, message), cancellationToken).ConfigureAwait(false);
}
