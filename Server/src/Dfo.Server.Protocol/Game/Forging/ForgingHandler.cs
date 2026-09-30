using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Forging;

public sealed class ForgingHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService mappings,
    ICommandBus commands,
    INotificationBus notifications,
    IGameMessageSender sender,
    ForgingReplayGuard replays,
    ILogger<ForgingHandler> logger) : ClientMessageHandler<ForgingRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ForgingRequest request, CancellationToken ct)
    {
        using var replay = await replays.EnterAsync(context.Connection,
            request.Metadata.Sequence, ct).ConfigureAwait(false);
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogDebug("FORGING no selected session");
            await Reject(context, ct, 0x04).ConfigureAwait(false);
            return;
        }
        replay.BindSession(session);
        if (replay.CachedFor(request, out var conflict) is { } cached)
        {
            if (cached.Receipt is { } prior)
                await notifications.PublishAsync(
                    new NotificationAudience.TheseCharacters([prior.Session.CharacterId]),
                    new ForgingCompleted(prior), ct).ConfigureAwait(false);
            else await Reject(context, ct, cached.ErrorCode).ConfigureAwait(false);
            return;
        }
        if (conflict || !replay.Admit(request))
        {
            await Reject(context, ct, 0x04).ConfigureAwait(false);
            return;
        }
        // Variant 1/2 requires a separate ticket rule; never assume the ordinary PVF material path.
        if (request.Variant != 0)
        {
            replay.RememberFailure(request, 0x0D);
            await Reject(context, ct, 0x0D).ConfigureAwait(false);
            return;
        }
        if (request.TargetListType is not ((byte)InventoryContainerType.Main)
                and not ((byte)InventoryContainerType.Equipment)
            || request.TargetSlot < 0 || request.MaterialSlot < 0
            || request.TargetItemId <= 0 || request.ReportedNameBytes.Length is < 1 or > 128
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
        {
            replay.RememberFailure(request, 0x04);
            await Reject(context, ct, 0x04).ConfigureAwait(false);
            return;
        }
        BusinessSlot target;
        BusinessSlot material;
        try
        {
            target = mappings.ToBusinessSlot(new(
                (InventoryContainerType)request.TargetListType, request.TargetSlot));
            material = mappings.ToBusinessSlot(new(InventoryContainerType.Main,
                request.MaterialSlot));
        }
        catch (InventorySlotMappingException)
        {
            replay.RememberFailure(request, 0x04);
            await Reject(context, ct, 0x04).ConfigureAwait(false);
            return;
        }
        EquipmentForgingResult result;
        try
        {
            result = await commands.SendAsync(new ForgingCommand(online.OnlineUserId,
                session, target, request.TargetItemId, material), ct).ConfigureAwait(false);
        }
        catch
        {
            replay.MarkCommitFailed(request);
            throw;
        }
        if (result.Receipt is not { } receipt)
        {
            var errorCode = ErrorCode(result.Rejection);
            replay.RememberFailure(request, errorCode);
            await Reject(context, ct, errorCode).ConfigureAwait(false);
            return;
        }
        replay.Remember(request, receipt);
        logger.LogInformation("FORGING character={Character} target={Target} old={Old} new={New} roll={Roll}",
            receipt.Session.CharacterId.Value, request.TargetSlot, receipt.OldLevel,
            receipt.NewLevel, receipt.Roll);
        await notifications.PublishAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
            new ForgingCompleted(receipt), ct).ConfigureAwait(false);
    }

    internal static byte ErrorCode(EquipmentForgingRejection rejection) => rejection switch
    {
        EquipmentForgingRejection.Durability => 0x07,
        EquipmentForgingRejection.UnsupportedContent => 0x0D,
        EquipmentForgingRejection.NotWeapon => 0x13,
        EquipmentForgingRejection.MaterialUnavailable => 0x16,
        EquipmentForgingRejection.MaxLevel => 0x5F,
        EquipmentForgingRejection.Locked => 0xD5,
        _ => 0x04,
    };

    private ValueTask Reject(NetworkDispatchContext context, CancellationToken ct,
        byte errorCode) => sender.SendCmdAsync(context.Connection.ConnectionId,
            new ForgingResponse(false, errorCode), ct);
}
