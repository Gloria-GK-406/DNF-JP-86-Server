using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.RepairItem;

public sealed class RepairItemHandler(IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions, ICommandBus commands, INotificationBus notifications,
    IGameMessageSender sender) : ClientMessageHandler<RepairItemRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        RepairItemRequest message, CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        EquipmentRepairResult result;
        // The observed NPC commands use only the ordinary repair mode. Do not silently charge a
        // quick/auto/material request as an ordinary repair until those variants are verified.
        if (message.MaterialSlot != -1 || message.AutoRepair != 0 || message.Reserved != 0
            || message.QuickRepair != 0)
            result = new(EquipmentRepairRejection.InvalidRequest);
        else if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
            result = new(EquipmentRepairRejection.NoSession);
        else
            result = await commands.SendAsync(new RepairItemCommand(online.OnlineUserId, session,
                message.Container, message.SlotIndex), cancellationToken).ConfigureAwait(false);

        if (result.Receipt is { } receipt)
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new EquipmentRepairCompleted(receipt), cancellationToken).ConfigureAwait(false);
        else
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new RepairItemResponse(null, ErrorCode(result.Rejection)), cancellationToken).ConfigureAwait(false);
    }

    // The upstream ordinary-repair path answers its failed commit with 0x0A. Individual A21
    // error branches still need client-EXE verification; avoid inventing separate meanings here.
    internal static byte ErrorCode(EquipmentRepairRejection reason) => reason switch
    {
        EquipmentRepairRejection.NoSession or EquipmentRepairRejection.InvalidRequest
            or EquipmentRepairRejection.ItemUnavailable or EquipmentRepairRejection.CannotRepair
            or EquipmentRepairRejection.InsufficientGold => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
