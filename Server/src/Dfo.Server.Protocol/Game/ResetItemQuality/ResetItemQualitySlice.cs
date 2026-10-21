using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.ResetItemQuality;

public sealed record ResetItemQualityRequest(GameMessageMetadata Metadata, ushort TargetSlot, int ExpectedItemId,
    ushort MaterialSlot, bool Valid = true) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameResetItemQuality;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
}
internal sealed class ResetItemQualityDecoder(ILogger<ResetItemQualityDecoder> logger) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        // Request layout is 70Reference/upstream; A21 builder is not yet recovered.
        var valid = body.Length == 8 || body.Length == 12
            && GameBodyChecksum.IsValid(body[..8], body[8..]);
        if (!valid) logger.LogWarning("RESET_ITEM_ATTR invalid body length={Length} hex={Body}", body.Length,
            Convert.ToHexString(body[..Math.Min(body.Length, 32)]));
        return valid ? new ResetItemQualityRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body),
            BinaryPrimitives.ReadInt32LittleEndian(body[2..]), BinaryPrimitives.ReadUInt16LittleEndian(body[6..]))
            : new ResetItemQualityRequest(metadata, 0, 0, 0, false);
    }
}
public sealed record ResetItemQualityResponse(bool Success, ushort MaterialSlot = 0, int Remaining = 0,
    byte Error = 0x13, ushort Operation = 2) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameResetItemQuality;
}
internal sealed class ResetItemQualityEncoder : GameCmdMessageEncoder<ResetItemQualityResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ResetItemQualityResponse message)
    {
        if (!message.Success) return OnePayload(0x0051, GameCmdEnvelope.Refuse(message.Error));
        var body = new byte[9]; body[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), message.MaterialSlot);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), message.Remaining);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(7), message.Operation);
        return OnePayload(0x0051, body);
    }
}
public sealed record ResetItemQualityCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessSlot Target, int ExpectedItemId, BusinessSlot Material)
    : ICommand<EquipmentQualityResult>, IRequestSettlementCommand, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class ResetItemQualityCommandHandler(IEquipmentQualityDomainService quality, IEquipmentResealDomainService reseal)
    : ICommandHandler<ResetItemQualityCommand, EquipmentQualityResult>
{
    public async ValueTask<EquipmentQualityResult> HandleAsync(CommandExecutionContext context,
        ResetItemQualityCommand command, CancellationToken ct) =>
        await reseal.TryResealAsync(command.Session, command.Target, command.ExpectedItemId, command.Material, ct).ConfigureAwait(false)
        ?? await quality.RerollAsync(command.Session, command.Target, command.ExpectedItemId, command.Material, ct).ConfigureAwait(false);
}
public sealed class ResetItemQualityHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IInventorySlotMappingRuleService mappings, ICommandBus commands, IEventService events,
    IGameMessageSender sender, ILogger<ResetItemQualityHandler> logger) : ClientMessageHandler<ResetItemQualityRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ResetItemQualityRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !request.Valid || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null) { await Reject(0x13); return; }
        BusinessSlot target;
        BusinessSlot material;
        try
        {
            target = mappings.ToBusinessSlot(new(InventoryContainerType.Main, request.TargetSlot));
            material = mappings.ToBusinessSlot(new(InventoryContainerType.Main, request.MaterialSlot));
        }
        catch (InventorySlotMappingException) { await Reject(0x13); return; }
        EquipmentQualityResult result;
        try
        {
            result = await commands.SendAsync(new ResetItemQualityCommand(online.OnlineUserId, session,
                target, request.ExpectedItemId, material), ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogError(failure, "Equipment attribute reset transaction failed character={Character}", session.CharacterId);
            await Reject(0x13); return;
        }
        if (result.Receipt is not { } receipt)
        {
            logger.LogInformation("Equipment attribute reset rejected character={Character} reason={Reason}", session.CharacterId, result.Rejection);
            await Reject(result.Rejection switch
            {
                EquipmentQualityRejection.MaterialUnavailable => 0x11,
                EquipmentQualityRejection.AlreadySealed => 0x12,
                EquipmentQualityRejection.ResealLimitReached => 0x0D,
                _ => (byte)0x13,
            });
            return;
        }
        logger.LogInformation("Equipment attribute reset committed character={Character} target={Slot} item={Item} operation={Operation} resealCount={ResealCount} seed={Seed} materialRemaining={Count}",
            session.CharacterId, request.TargetSlot, request.ExpectedItemId, receipt.Operation, receipt.Equipment.ResealCount,
            receipt.Equipment.InstanceValue, receipt.MaterialRemaining);
        await events.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new EquipmentQualityCompleted(receipt), ct).ConfigureAwait(false);
        ValueTask Reject(byte error) => sender.SendCmdAsync(context.Connection.ConnectionId, new ResetItemQualityResponse(false, Error: error), ct);
    }
}
public sealed record EquipmentQualityCompleted(EquipmentQualityReceipt Receipt) : INotification<EquipmentQualityCompleted>;
public sealed class EquipmentQualityCompletedHandler(ISelectedSessionDomainService sessions, IInventorySlotProjectionDomainService slots)
    : INotificationHandler<EquipmentQualityCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        EquipmentQualityCompleted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var material = slots.Project(receipt.MaterialSlot);
        var target = slots.Project(receipt.TargetSlot);
        // CMD0051 updates/removes the material itself. Only the target needs NOTI000E.
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([
            new ResetItemQualityResponse(true, checked((ushort)material.Index), receipt.MaterialRemaining, Operation: receipt.Operation),
            GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(target.Space,
                [new InventoryItemState(receipt.Equipment.ItemUid, target.Index, receipt.Equipment.ToCore())], receipt.At))]);
    }
}
