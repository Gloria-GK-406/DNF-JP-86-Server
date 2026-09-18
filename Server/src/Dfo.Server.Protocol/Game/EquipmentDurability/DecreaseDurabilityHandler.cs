using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EquipmentDurability;

public sealed class DecreaseDurabilityHandler(IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions, ICommandBus commands, INotificationBus notifications,
    IGameMessageSender sender) : ClientMessageHandler<DecreaseDurabilityRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        DecreaseDurabilityRequest message, CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        EquipmentDurabilityResult result;
        if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
            result = new(EquipmentDurabilityRejection.NoSession);
        else
            result = await commands.SendAsync(new DecreaseDurabilityCommand(online.OnlineUserId, session,
                message.SlotIndex), cancellationToken).ConfigureAwait(false);

        if (result.Receipt is { } receipt)
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new EquipmentDurabilityChanged(receipt), cancellationToken).ConfigureAwait(false);
        else
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new DecreaseDurabilityResponse(null, 17), cancellationToken).ConfigureAwait(false);
    }
}
