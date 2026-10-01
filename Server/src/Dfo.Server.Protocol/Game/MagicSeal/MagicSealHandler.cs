using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MagicSeal;

public sealed class MagicSealHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService mappings,
    ICommandBus commands,
    INotificationBus notifications,
    IGameMessageSender sender,
    ILogger<MagicSealHandler> logger) : ClientMessageHandler<MagicSealRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        MagicSealRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogDebug("MAGIC_SEAL no selected session change={Change}", request.Change);
            await Reject(context, request, ct).ConfigureAwait(false);
            return;
        }
        if (request.TargetSlot < 0
            || request.MaterialSlot != -1
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
        {
            await Reject(context, request, ct).ConfigureAwait(false);
            return;
        }
        BusinessSlot target;
        try
        {
            target = mappings.ToBusinessSlot(
                new(InventoryContainerType.Main, request.TargetSlot));
        }
        catch (InventorySlotMappingException)
        {
            await Reject(context, request, ct).ConfigureAwait(false);
            return;
        }
        var result = await commands.SendAsync(new MagicSealCommand(online.OnlineUserId,
            session, request.Change, target, null, request.OptionIndex), ct)
            .ConfigureAwait(false);
        if (result.Receipt is not { } receipt)
        {
            logger.LogDebug("MAGIC_SEAL rejected: {Reason}", result.Rejection);
            await Reject(context, request, ct, ErrorCode(result.Rejection))
                .ConfigureAwait(false);
            return;
        }
        logger.LogInformation("MAGIC_SEAL character={Character} change={Change} slot={Slot} seed={Seed}",
            receipt.Session.CharacterId.Value, request.Change, request.TargetSlot, receipt.Seed);
        await notifications.PublishAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
            new MagicSealCompleted(receipt, request.Change), ct).ConfigureAwait(false);
    }

    private static byte ErrorCode(EquipmentMagicSealRejection rejection) => rejection switch
    {
        EquipmentMagicSealRejection.InsufficientGold => 0x0A,
        EquipmentMagicSealRejection.MaterialUnavailable => 0x11,
        EquipmentMagicSealRejection.Locked => 0xD6,
        EquipmentMagicSealRejection.UnsupportedContent => 0x17,
        _ => 0x13,
    };

    private ValueTask Reject(NetworkDispatchContext context, MagicSealRequest request,
        CancellationToken ct, byte errorCode = 0x13) => request.Change
        ? sender.SendCmdAsync(context.Connection.ConnectionId,
            new ChangeRandomOptionResponse(false, errorCode), ct)
        : ValueTask.CompletedTask;
}
