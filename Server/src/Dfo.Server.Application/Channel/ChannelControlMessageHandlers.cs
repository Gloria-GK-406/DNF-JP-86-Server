using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Channel;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Channel;

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

public sealed class ChannelInfoHandler(
    INetworkMessageSender sender,
    ICommandBus commandBus) : ClientMessageHandler<ChannelInfoRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ChannelInfoRequest message,
        CancellationToken cancellationToken)
    {
        var result = await commandBus.SendAsync(
            new GetChannelInfoCommand(), cancellationToken).ConfigureAwait(false);
        await sender.SendAsync(
            context.Connection.ConnectionId,
            new ChannelInfoResponse(result.Channels),
            cancellationToken).ConfigureAwait(false);
    }
}
