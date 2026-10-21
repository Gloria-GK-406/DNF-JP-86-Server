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

namespace Dfo.Server.Protocol.Game.ResetRandomOption;

public sealed record ResetRandomOptionRequest(GameMessageMetadata Metadata, ushort TargetSlot,
    ushort MaterialSlot, ushort OptionIndex, bool Valid = true) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameResetRandomOption;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
}

internal sealed class ResetRandomOptionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        // A21 writes three u16s; the third field must not be discarded or truncated to a byte.
        var valid = body.Length == 6 || body.Length == 10 && GameBodyChecksum.IsValid(body[..6], body[6..]);
        return valid ? new ResetRandomOptionRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body),
            BinaryPrimitives.ReadUInt16LittleEndian(body[2..]), BinaryPrimitives.ReadUInt16LittleEndian(body[4..]))
            : new ResetRandomOptionRequest(metadata, 0, 0, 0, false);
    }
}

public sealed record ResetRandomOptionResponse(bool Success, byte Error = 0x13) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameResetRandomOption;
}

internal sealed class ResetRandomOptionEncoder : GameCmdMessageEncoder<ResetRandomOptionResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ResetRandomOptionResponse message)
        => OnePayload(0x01C8, message.Success ? [1] : GameCmdEnvelope.Refuse(message.Error));
}

public sealed record ResetRandomOptionCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessSlot Target, BusinessSlot Material, ushort OptionIndex)
    : ICommand<EquipmentQualityResult>, IRequestSettlementCommand, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class ResetRandomOptionCommandHandler(IEquipmentQualityDomainService quality)
    : ICommandHandler<ResetRandomOptionCommand, EquipmentQualityResult>
{
    public ValueTask<EquipmentQualityResult> HandleAsync(CommandExecutionContext context,
        ResetRandomOptionCommand command, CancellationToken ct)
        => quality.RerollMagicSealGradeAsync(command.Session, command.Target, command.Material, command.OptionIndex, ct);
}

public sealed class ResetRandomOptionHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IInventorySlotMappingRuleService mappings, ICommandBus commands, IEventService events,
    IGameMessageSender sender, ILogger<ResetRandomOptionHandler> logger) : ClientMessageHandler<ResetRandomOptionRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ResetRandomOptionRequest request, CancellationToken ct)
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
            result = await commands.SendAsync(new ResetRandomOptionCommand(online.OnlineUserId, session,
                target, material, request.OptionIndex), ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogError(failure, "Magic-seal grade transaction failed character={Character}", session.CharacterId);
            await Reject(0x13); return;
        }
        if (result.Receipt is not { } receipt)
        {
            logger.LogInformation("Magic-seal grade refused character={Character} target={Target} material={Material} option={Option} reason={Reason}",
                session.CharacterId, request.TargetSlot, request.MaterialSlot, request.OptionIndex, result.Rejection);
            await Reject(result.Rejection switch
            {
                EquipmentQualityRejection.Locked => 0xD5,
                EquipmentQualityRejection.MaterialUnavailable => 0x15,
                EquipmentQualityRejection.UnmodifiableOption => 0x17,
                EquipmentQualityRejection.ItemUnavailable => 0x11,
                _ => 0x13,
            });
            return;
        }
        logger.LogInformation("Magic-seal grade committed character={Character} target={Target} material={Material} option={Option} remaining={Count}",
            session.CharacterId, request.TargetSlot, request.MaterialSlot, request.OptionIndex, receipt.MaterialRemaining);
        await events.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new MagicSealGradeCompleted(receipt), ct).ConfigureAwait(false);
        ValueTask Reject(byte error) => sender.SendCmdAsync(context.Connection.ConnectionId, new ResetRandomOptionResponse(false, error), ct);
    }
}

public sealed record MagicSealGradeCompleted(EquipmentQualityReceipt Receipt) : INotification<MagicSealGradeCompleted>;

public sealed class MagicSealGradeCompletedHandler(ISelectedSessionDomainService sessions, IInventorySlotProjectionDomainService slots)
    : INotificationHandler<MagicSealGradeCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        MagicSealGradeCompleted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var target = slots.Project(receipt.TargetSlot);
        var material = slots.Project(receipt.MaterialSlot);
        // Unlike 0051, this ACK carries no material count. Publish both committed slots first.
        var targetUpdate = GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(target.Space,
            [new InventoryItemState(receipt.Equipment.ItemUid, target.Index, receipt.Equipment.ToCore())], receipt.At));
        var materialUpdate = receipt.MaterialRemaining == 0
            ? GameInventoryDelivery.ClearedSlotNotification(material.Space, material.Index, receipt.At)
            : GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(material.Space,
                [new InventoryItemState(null, material.Index, (receipt.Material ?? throw new InvalidOperationException("Missing material receipt.")).ToCore())], receipt.At));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([targetUpdate, materialUpdate, new ResetRandomOptionResponse(true)]);
    }
}
