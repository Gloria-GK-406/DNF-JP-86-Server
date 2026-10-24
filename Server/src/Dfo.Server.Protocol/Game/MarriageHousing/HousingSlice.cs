using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.MarriageHousing;

public sealed record HousingNetworkRequest(GameMessageMetadata Metadata, ushort Opcode, HousingRequest Request) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameMarriageHousing;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.MarriageHousing | ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.MarriageLovePoints;
}
internal abstract class HousingDecoder(ushort opcode, HousingAction action, int length) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != length) throw new InvalidDataException($"Housing {opcode:X4} requires {length} body bytes.");
        HousingRequest request;
        if (action == HousingAction.Storage)
        {
            if (body[14] > 1) throw new InvalidDataException("Invalid housing storage flag.");
            request = new(action, Count: body[14]);
        }
        else if (action == HousingAction.Move)
        {
            var a = Endpoint(body[14..]); var b = Endpoint(body[21..]);
            request = new(action, Source: a, Destination: b);
        }
        else if (action == HousingAction.Delete)
            request = new(action, BinaryPrimitives.ReadUInt16LittleEndian(body[14..]),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(body[16..])), checked((int)BinaryPrimitives.ReadUInt32LittleEndian(body[20..])));
        else if (action is HousingAction.Plant or HousingAction.Water)
            request = new(action, BinaryPrimitives.ReadUInt16LittleEndian(body[14..]), checked((int)BinaryPrimitives.ReadUInt32LittleEndian(body[16..])));
        else if (action == HousingAction.UpgradeRing)
            request = new(action, checked((int)BinaryPrimitives.ReadUInt32LittleEndian(body[14..])), Count: body[18]);
        else request = new(action);
        return new HousingNetworkRequest(metadata, opcode, request);
    }
    private static HousingEndpoint Endpoint(ReadOnlySpan<byte> b)
    {
        if (b[0] > 1) throw new InvalidDataException("Invalid furniture location kind.");
        return new(b[0], BinaryPrimitives.ReadUInt16LittleEndian(b[1..]), checked((int)BinaryPrimitives.ReadUInt32LittleEndian(b[3..])));
    }
}
internal sealed class HousingStorageDecoder() : HousingDecoder(0x03FD, HousingAction.Storage, 15);
internal sealed class HousingMoveDecoder() : HousingDecoder(0x03FE, HousingAction.Move, 28);
internal sealed class HousingDeleteDecoder() : HousingDecoder(0x03FF, HousingAction.Delete, 24);
internal sealed class HousingPlantDecoder() : HousingDecoder(0x0400, HousingAction.Plant, 20);
internal sealed class HousingWaterDecoder() : HousingDecoder(0x0401, HousingAction.Water, 20);
internal sealed class HousingHarvestDecoder() : HousingDecoder(0x0402, HousingAction.Harvest, 0);
internal sealed class HousingClearDecoder() : HousingDecoder(0x0403, HousingAction.Clear, 0);
internal sealed class HousingRingDecoder() : HousingDecoder(0x03F4, HousingAction.UpgradeRing, 19);

internal sealed record HousingAck(ushort Opcode, int Status, byte Value = 0) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMarriageHousing; }
internal sealed class HousingAckEncoder : GameCmdMessageEncoder<HousingAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(HousingAck m)
    {
        // Native dispatch consumes the accepted envelope before invoking the opcode handler.
        if (m.Opcode == 0x03FF) return OnePayload(m.Opcode, GameCmdEnvelope.Accept());
        var b = new byte[m.Opcode is 0x03FD or 0x03F4 ? 5 : 4];
        BinaryPrimitives.WriteInt32LittleEndian(b, m.Status); if (b.Length == 5) b[4] = m.Value;
        return OnePayload(m.Opcode, GameCmdEnvelope.Accept(b));
    }
}
public sealed record HousingCommand(SelectedSessionIdentity Actor, HousingRequest Request, IReadOnlyList<AccountId> AccountIds)
    : ICommand<HousingResult>, IRequestSettlementCommand
{ public SelectedSessionIdentity? Session => Actor; }
public sealed class HousingCommandHandler(IMarriageHousingDomainService housing) : ICommandHandler<HousingCommand, HousingResult>
{
    public ValueTask<HousingResult> HandleAsync(CommandExecutionContext context, HousingCommand command, CancellationToken ct) =>
        housing.ExecuteAsync(command.Actor, command.Request, command.AccountIds, context.LegacyFootprint, ct);
}
internal sealed class HousingHandler(ISelectedSessionDomainService sessions, IMarriageQueryDomainService marriage,
    ICommandBus commands, IGameMessageSender sender, IEventService events, TimeProvider clock) : ClientMessageHandler<HousingNetworkRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, HousingNetworkRequest message, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(context.Connection.ConnectionId);
        HousingResult result;
        try
        {
            result = actor is null ? new HousingResult(-4) : await commands.SendAsync(new HousingCommand(actor, message.Request,
                await marriage.CoupleAccountsAsync(actor, ct).ConfigureAwait(false)), ct).ConfigureAwait(false);
        }
        catch (HousingRejectedException error) { result = new(error.Status); }
        await sender.SendCmdAsync(context.Connection.ConnectionId, new HousingAck(message.Opcode, result.Status,
            message.Request.Action == HousingAction.UpgradeRing ? result.RingLevel : message.Request.Action == HousingAction.Storage ? checked((byte)message.Request.Count) : (byte)0), ct).ConfigureAwait(false);
        if (result.Status != 0 || actor is null) return;
        if (result.ConsumedSlot is { } slot)
        {
            var wire = new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService().ToGameSlot(slot);
            await sender.SendExplicitAsync(context.Connection.ConnectionId, new([result.Remaining is { } item
                ? GameInventoryDelivery.Notification(new(wire.Space, [new(item.ItemUid, wire.Index, item.ToCore(), item.AvatarDetail)], clock.GetUtcNow()))
                : GameInventoryDelivery.ClearedSlotNotification(wire.Space, wire.Index, clock.GetUtcNow())], []), ct).ConfigureAwait(false);
        }
        await events.ProjectAsync(new NotificationAudience.TheseCharacters(result.Audience), new HousingChanged(actor, message.Request, result), ct).ConfigureAwait(false);
    }
}
public sealed record HousingChanged(SelectedSessionIdentity Actor, HousingRequest Request, HousingResult Result) : INotification<HousingChanged>;
internal sealed class HousingChangedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<HousingChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, HousingChanged change, CancellationToken ct)
    {
        var own = recipient.CharacterId == change.Actor.CharacterId;
        if (own && !sessions.IsInWorld(change.Actor)) return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var messages = new List<IServerNetworkMessage>();
        if (change.Request.Action == HousingAction.UpgradeRing)
        {
            // The purchaser's successful CMD03F4 already updates the ring and announces it.
            // Only the spouse receives the one-shot NOTI03D1 event.
            if (!own) messages.Add(new HousingRingMessage(change.Result.RingLevel));
        }
        else if (change.Result.Snapshot is { } snapshot)
        {
            if (change.Request.Action == HousingAction.Storage)
            { if (own && change.Request.Count == 0) messages.Add(new HousingStorageMessage(snapshot)); }
            else if (change.Request.Action == HousingAction.Move)
            {
                messages.Add(new HousingMoveMessage(change.Request, snapshot));
                // The native dual-storage branch swaps locally. An absolute snapshot also settles pile merges.
                if (change.Request.Source?.Kind == 1 && change.Request.Destination?.Kind == 1) messages.Add(new HousingStorageMessage(snapshot));
            }
            else if (change.Request.Action == HousingAction.Delete) messages.Add(new HousingDeleteMessage(change.Request, snapshot));
            else messages.Add(new HousingTreeMessage(snapshot.Tree));
        }
        var rewards = change.Result.Rewards.Where(x => x.Character == recipient.CharacterId).ToArray();
        if (rewards.Length > 0) messages.Add(new HousingRewardsMessage(rewards));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}

public sealed record InitializeHousingCommand(SelectedSessionIdentity Session) : ICommand<bool>, IAccountCommand
{ public AccountId AccountId => Session.AccountId; }
public sealed class InitializeHousingCommandHandler(IMarriageHousingDomainService housing, ISelectedSessionDomainService sessions)
    : ICommandHandler<InitializeHousingCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, InitializeHousingCommand command, CancellationToken ct)
    {
        if (!sessions.IsSelected(command.Session)) return false;
        await housing.InitializeAsync(command.AccountId, command.Session.CharacterId, ct).ConfigureAwait(false); return true;
    }
}
