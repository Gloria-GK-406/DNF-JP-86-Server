using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

public sealed class SelectCharacterHandler(
    IOnlineUserQueryService onlineUsers,
    IChannelEntryPolicy channels,
    ICommandBus commandBus) : ClientMessageHandler<SelectCharacterRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SelectCharacterRequest message,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{context.Connection.ConnectionId}' is not authenticated.");
        }

        // Resolved here so the listener stays a transport detail and the command states the
        // channel as a fact it was given rather than one it goes looking for.
        var channel = await channels
            .ResolveAsync(context.Connection.ListenerPort, cancellationToken)
            .ConfigureAwait(false);

        var result = await commandBus.SendAsync(
            new SelectCharacterCommand(
                accountId, message.SlotIndex, channel,
                context.Connection.ConnectionId, onlineUser.Generation),
            cancellationToken).ConfigureAwait(false);
        _ = result; // The lifecycle command owns ordered delivery and publication.
    }
}
