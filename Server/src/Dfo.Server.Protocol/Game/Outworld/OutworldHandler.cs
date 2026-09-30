using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Outworld;

public sealed class OutworldHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService mappings,
    ICommandBus commands,
    INotificationBus notifications,
    IGameMessageSender sender,
    OutworldReplayGuard replays,
    ILogger<OutworldHandler> logger) : ClientMessageHandler<OutworldRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        OutworldRequest request, CancellationToken ct)
    {
        using var replay = await replays.EnterAsync(context.Connection,
            request.Metadata.Sequence, ct).ConfigureAwait(false);
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogDebug("OUTWORLD no selected session purify={Purify}", request.Purify);
            await Reject(context, request, ct).ConfigureAwait(false);
            return;
        }
        replay.BindSession(session);
        if (replay.CachedFor(request, out var conflict) is { } cached)
        {
            if (cached.Receipt is { } committed)
                await notifications.PublishAsync(
                    new NotificationAudience.TheseCharacters([committed.Session.CharacterId]),
                    new OutworldCompleted(committed, request.Purify, request.WireAction), ct)
                    .ConfigureAwait(false);
            else
                await Reject(context, request, ct, cached.ErrorCode).ConfigureAwait(false);
            return;
        }
        if (conflict || !replay.Admit(request) || request.TargetSlot < 0
            || request.MaterialSlot < 0 || request.TargetSlot == request.MaterialSlot
            || request.TargetItemId <= 0 || request.MaterialItemId <= 0
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
        {
            await Reject(context, request, ct).ConfigureAwait(false);
            return;
        }
        // Action 2 appends a polymorphic string in A21. Its material and result semantics
        // are not closed, so never route it to a transaction.
        if (!request.Purify && request.WireAction is not (0 or 1))
        {
            replay.RememberFailure(request, 0x08);
            await Reject(context, request, ct, 0x08).ConfigureAwait(false);
            return;
        }
        BusinessSlot target;
        BusinessSlot material;
        try
        {
            target = mappings.ToBusinessSlot(new(InventoryContainerType.Main, request.TargetSlot));
            material = mappings.ToBusinessSlot(new(InventoryContainerType.Main, request.MaterialSlot));
        }
        catch (InventorySlotMappingException)
        {
            await Reject(context, request, ct).ConfigureAwait(false);
            return;
        }
        var action = request.Purify ? EquipmentOutworldAction.Purify
            : request.WireAction == 0 ? EquipmentOutworldAction.Invest
            : EquipmentOutworldAction.Twist;
        EquipmentOutworldResult result;
        try
        {
            result = await commands.SendAsync(new OutworldCommand(online.OnlineUserId,
                session, action, target, request.TargetItemId,
                material, request.MaterialItemId, request.SelectedOption), ct)
                .ConfigureAwait(false);
        }
        catch
        {
            replay.MarkCommitFailed(request);
            throw;
        }
        if (result.Receipt is not { } receipt)
        {
            var errorCode = ErrorCode(result.Rejection);
            logger.LogInformation("OUTWORLD rejected character={Character} action={Action} targetSlot={TargetSlot} targetItem={TargetItem} material={Material} selectedOption={SelectedOption} reason={Reason} errorCode={ErrorCode}",
                session.CharacterId.Value, action, request.TargetSlot, request.TargetItemId,
                request.MaterialItemId, request.SelectedOption, result.Rejection, errorCode);
            replay.RememberFailure(request, errorCode);
            await Reject(context, request, ct, errorCode).ConfigureAwait(false);
            return;
        }
        replay.Remember(request, receipt);
        logger.LogInformation("OUTWORLD character={Character} action={Action} target={Target} material={Material} seed={Seed}",
            receipt.Session.CharacterId.Value, action, request.TargetSlot, request.MaterialSlot, receipt.Seed);
        await notifications.PublishAsync(
            new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
            new OutworldCompleted(receipt, request.Purify, request.WireAction), ct)
            .ConfigureAwait(false);
    }

    private static byte ErrorCode(EquipmentOutworldRejection rejection) => rejection switch
    {
        EquipmentOutworldRejection.UnsupportedContent => 0x08,
        EquipmentOutworldRejection.SameOption => 0x17,
        EquipmentOutworldRejection.AlreadyUpgraded => 0x12,
        EquipmentOutworldRejection.AlreadyHasAmplifyOption => 0x14,
        EquipmentOutworldRejection.NoAmplifyOption => 0x15,
        _ => 0x11,
    };

    private ValueTask Reject(NetworkDispatchContext context, OutworldRequest request,
        CancellationToken ct, byte errorCode = 0x11) => request.Purify
        ? sender.SendCmdAsync(context.Connection.ConnectionId,
            new PurifyItemResponse(false), ct)
        : sender.SendCmdAsync(context.Connection.ConnectionId,
            new InvestAmplifyOptionResponse(false, request.WireAction, ErrorCode: errorCode), ct);
}
