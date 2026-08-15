using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.ScriptVersion;

public sealed class ChannelScriptVersionHandler(
    INetworkMessageSender sender,
    ICommandBus commandBus) : ClientMessageHandler<ChannelScriptVersionRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ChannelScriptVersionRequest message,
        CancellationToken cancellationToken)
    {
        var result = await commandBus.SendAsync(
            new GetChannelScriptVersionCommand(), cancellationToken).ConfigureAwait(false);
        await sender.SendAsync(
            context.Connection.ConnectionId,
            new ChannelScriptVersionResponse(result.Version),
            cancellationToken).ConfigureAwait(false);
    }
}
