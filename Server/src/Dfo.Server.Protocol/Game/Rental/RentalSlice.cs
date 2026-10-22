using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Rental;

public sealed record RentEquipmentRequest(GameMessageMetadata Metadata, int ItemId, uint Context) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameRentEquipment;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Rental | ReplayProjectionNeeds.CharacterAppearance;
}
public sealed record ChargeRentPointRequest(GameMessageMetadata Metadata, int Mode, int Count) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameChargeRentPoint;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Rental;
}
internal sealed class RentEquipmentDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 22) throw new InvalidDataException("RENT_EQUIPMENT_ITEM requires a 22-byte body.");
        return new RentEquipmentRequest(metadata, BinaryPrimitives.ReadInt32LittleEndian(body[14..]), BinaryPrimitives.ReadUInt32LittleEndian(body[18..]));
    }
}
internal sealed class ChargeRentPointDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 22) throw new InvalidDataException("CHARGE_RENTPOINT requires a 22-byte body.");
        return new ChargeRentPointRequest(metadata, BinaryPrimitives.ReadInt32LittleEndian(body[14..]), BinaryPrimitives.ReadInt32LittleEndian(body[18..]));
    }
}
public sealed record RentEquipmentResponse(uint Result = 0, byte? Error = null) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameRentEquipment; }
public sealed record ChargeRentPointResponse(uint Mode, uint ResultOrQuantity, uint Balance, byte? Error = null) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameChargeRentPoint; }
internal sealed class RentEquipmentEncoder : GameCmdMessageEncoder<RentEquipmentResponse>
{
    internal static byte[] Body(RentEquipmentResponse message)
    {
        if (message.Error is { } error) return [0,error];
        if (message.Result > 2) throw new InvalidDataException("Unverified rental result code.");
        var body = new byte[5]; body[0]=1; BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1),message.Result); return body;
    }
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(RentEquipmentResponse message) => OnePayload(0x03CF,Body(message));
}
internal sealed class ChargeRentPointEncoder : GameCmdMessageEncoder<ChargeRentPointResponse>
{
    internal static byte[] Body(ChargeRentPointResponse message)
    {
        if (message.Error is { } error) return [0,error];
        var body = new byte[13]; body[0]=1;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1),message.Mode);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5),message.ResultOrQuantity);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(9),message.Balance); return body;
    }
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ChargeRentPointResponse message) => OnePayload(0x03D0,Body(message));
}
public sealed record RentEquipmentCommand(SelectedSessionIdentity Actor, int ItemId) : ICommand<EquipmentRentalReceipt>, IAccountCommand, IRequestSettlementCommand
{ public AccountId AccountId => Actor.AccountId; public SelectedSessionIdentity? Session => Actor; }
public sealed class RentEquipmentCommandHandler(IEquipmentRentalDomainService rentals,IRequestReplayProjectionDomainService replay) : ICommandHandler<RentEquipmentCommand,EquipmentRentalReceipt>
{
    public async ValueTask<EquipmentRentalReceipt> HandleAsync(CommandExecutionContext context,RentEquipmentCommand command,CancellationToken ct)
    {
        var receipt=await rentals.RentAsync(command.Actor.AccountId,command.Actor.CharacterId,command.ItemId,ct);
        if(receipt.Succeeded) replay.RecordInventorySlots(command.Actor,receipt.TouchedSlots);
        return receipt;
    }
}
public sealed record ChargeRentPointCommand(SelectedSessionIdentity Actor,int Count) : ICommand<LuckyStarMutation>, IAccountCommand, IRequestSettlementCommand
{ public AccountId AccountId => Actor.AccountId; public SelectedSessionIdentity? Session => Actor; }
public sealed class ChargeRentPointCommandHandler(IAccountLuckyStarDomainService stars) : ICommandHandler<ChargeRentPointCommand,LuckyStarMutation>
{
    public ValueTask<LuckyStarMutation> HandleAsync(CommandExecutionContext context,ChargeRentPointCommand command,CancellationToken ct) =>
        stars.PurchaseAsync(command.Actor.AccountId,command.Actor.CharacterId,command.Count,ct);
}
public sealed record RentalRefreshSnapshot(EquipmentRentalPanel Panel,CharacterInventoryEntryState Character,AccountInventoryEntryState Account,DateTimeOffset At);
public sealed record ReadRentalRefreshCommand(SelectedSessionIdentity Actor) : ICommand<RentalRefreshSnapshot>, IAccountCommand, ISelectedSessionCommand
{ public AccountId AccountId => Actor.AccountId; public SelectedSessionIdentity? Session => Actor; }
public sealed class ReadRentalRefreshCommandHandler(IEquipmentRentalDomainService rentals,ICharacterInventoryEntryDomainService inventory,
    IAccountInventoryEntryDomainService accounts,TimeProvider clock) : ICommandHandler<ReadRentalRefreshCommand,RentalRefreshSnapshot>
{
    public async ValueTask<RentalRefreshSnapshot> HandleAsync(CommandExecutionContext context,ReadRentalRefreshCommand command,CancellationToken ct) =>
        new(await rentals.PanelAsync(command.Actor.AccountId,command.Actor.CharacterId,ct),
            await inventory.ReadCurrentAsync(command.Actor.AccountId,command.Actor.CharacterId,ct),
            await accounts.LoadAsync(command.Actor.AccountId,command.Actor.CharacterId,ct),clock.GetUtcNow());
}
internal static class RentalPanelProjection
{
    internal static RentalStateMessage Full(EquipmentRentalPanel panel,DateTimeOffset at) => new(
        new CharacterRentalEntryState(panel.Items.Select(x=>new CharacterRentalItemState(checked((uint)x.Uid.Value),checked((uint)x.ItemId),checked((uint)x.ExpireTime))).ToArray()),
        checked((ushort)panel.LuckyStar),at);
}
internal sealed class RentEquipmentHandler(ISelectedSessionDomainService sessions,ICommandBus commands,IGameMessageSender sender,
    SelectedSessionSafety safety,IEventService events,ICharacterVisibleSessionDomainService visible,
    ILogger<RentEquipmentHandler> logger) : ClientMessageHandler<RentEquipmentRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,RentEquipmentRequest message,CancellationToken ct)
    {
        var actor=sessions.FindByConnection(context.Connection.ConnectionId); if(actor is null){await sender.SendCmdAsync(context.Connection.ConnectionId,new RentEquipmentResponse(Error:1),ct);return;}
        var recipient=safety.Capture(actor);safety.DemandCurrent(recipient);
        var result=await commands.SendAsync(new RentEquipmentCommand(actor,message.ItemId),ct);
        safety.DemandCurrent(recipient);
        logger.LogInformation("Rental character={Character} item={Item} result={Result} renewed={Renewed}",actor.CharacterId,message.ItemId,result.Rejection,result.Renewed);
        if(!result.Succeeded)
        {
            var reply=result.Rejection switch
            {
                EquipmentRentalRejection.TooManyRentals=>new RentEquipmentResponse(1),
                EquipmentRentalRejection.InventoryFull=>new RentEquipmentResponse(2),
                _=>new RentEquipmentResponse(Error:1),
            };
            await sender.SendCmdAsync(context.Connection.ConnectionId,reply,ct);return;
        }
        var current=await commands.SendAsync(new ReadRentalRefreshCommand(actor),ct);safety.DemandCurrent(recipient);
        var frames=new List<IGameServerMessage>();
        foreach(var type in result.ChangedContainers.Distinct().Where(x=>x!=InventoryContainerType.Equipment))
            frames.Add(new InventoryFullSnapshotMessage(current.Character.Containers.Single(x=>x.ContainerType==type),current.Character,current.Account,current.At));
        frames.Add(new RentEquipmentResponse());frames.Add(RentalPanelProjection.Full(current.Panel,current.At));
        await sender.SendBatchAsync(context.Connection,frames,ct);
        if(result.ChangedContainers.Contains(InventoryContainerType.Equipment))
            await events.ProjectAsync(new NotificationAudience.TheseSessions(visible.Resolve(actor)),new CharacterUserStateRefresh(actor),ct);
    }
}
internal sealed class ChargeRentPointHandler(ISelectedSessionDomainService sessions,ICommandBus commands,IGameMessageSender sender,
    SelectedSessionSafety safety,ILogger<ChargeRentPointHandler> logger) : ClientMessageHandler<ChargeRentPointRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,ChargeRentPointRequest message,CancellationToken ct)
    {
        var actor=sessions.FindByConnection(context.Connection.ConnectionId);
        if(actor is null||message.Mode!=1){await sender.SendCmdAsync(context.Connection.ConnectionId,new ChargeRentPointResponse(1,0,0,1),ct);return;}
        var recipient=safety.Capture(actor);safety.DemandCurrent(recipient);
        var result=await commands.SendAsync(new ChargeRentPointCommand(actor,message.Count),ct);safety.DemandCurrent(recipient);
        logger.LogInformation("Lucky star purchase character={Character} count={Count} result={Result} balance={Balance}",actor.CharacterId,message.Count,result.Rejection,result.Balance);
        if(!result.Succeeded){await sender.SendCmdAsync(context.Connection.ConnectionId,new ChargeRentPointResponse(1,0,checked((uint)result.Balance),1),ct);return;}
        var current=await commands.SendAsync(new ReadRentalRefreshCommand(actor),ct);safety.DemandCurrent(recipient);
        // Native CHARGE_RENTPOINT computes the gain from its old balance. ACK must precede 03C1.
        await sender.SendBatchAsync(context.Connection,[new ChargeRentPointResponse(1,checked((uint)message.Count),checked((uint)result.Balance)),
            new InventoryFullSnapshotMessage(current.Character.Containers.Single(x=>x.ContainerType==InventoryContainerType.Main),current.Character,current.Account,current.At),
            RentalPanelProjection.Full(current.Panel,current.At)],ct);
    }
}
internal sealed record LuckyStarPanelRefresh(SelectedSessionIdentity Owner,DateTimeOffset At) : INotification<LuckyStarPanelRefresh>;
internal sealed class LuckyStarChangedSubscriber(ISelectedSessionDomainService sessions,IEventService events) : IDomainEventSubscriber<LuckyStarChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<LuckyStarChanged> message,CancellationToken ct)
    {
        if(!message.Payload.NotifyGain||sessions.Find(message.Payload.AccountId) is not { } owner||!sessions.IsInWorld(owner))return;
        await events.ProjectAsync(new NotificationAudience.TheseSessions([owner]),new LuckyStarPanelRefresh(owner,message.OccurredAt),ct);
    }
}
internal sealed class LuckyStarPanelRefreshHandler(ISelectedSessionDomainService sessions,IEquipmentRentalDomainService rentals)
    : INotificationHandler<LuckyStarPanelRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,LuckyStarPanelRefresh notification,CancellationToken ct)
    {
        var owner=notification.Owner;
        if(recipient.CharacterId!=owner.CharacterId||recipient.ConnectionId!=owner.ConnectionId||!sessions.IsInWorld(owner))return [];
        var panel=await rentals.PanelAsync(owner.AccountId,owner.CharacterId,ct);
        if(!sessions.IsInWorld(owner))return [];
        return [new ChargeRentPointResponse(2,0,checked((uint)panel.LuckyStar)),RentalPanelProjection.Full(panel,notification.At)];
    }
}
