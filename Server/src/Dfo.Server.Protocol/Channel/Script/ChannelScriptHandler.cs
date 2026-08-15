using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Script;

public sealed class ChannelScriptHandler(
    INetworkMessageSender sender,
    ICommandBus commandBus) : ClientMessageHandler<ChannelScriptRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ChannelScriptRequest message,
        CancellationToken cancellationToken)
    {
        var result = await commandBus.SendAsync(
            new GetChannelScriptCommand(), cancellationToken).ConfigureAwait(false);
        await sender.SendAsync(
            context.Connection.ConnectionId,
            new ChannelScriptResponse(result.Content),
            cancellationToken).ConfigureAwait(false);
    }
}
