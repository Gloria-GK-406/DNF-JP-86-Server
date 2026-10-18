using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game.MarriageProposal;
public sealed record WeddingLovePointRequest(GameMessageMetadata Metadata, uint Slot) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameWeddingLovePoint;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.MarriageLovePoints;
}
internal sealed class WeddingLovePointDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> b)
    {
        if (b.Length != 18) throw new InvalidDataException("Love coupon requires 18 body bytes.");
        return new WeddingLovePointRequest(m, BinaryPrimitives.ReadUInt32LittleEndian(b[14..]));
    }
}
internal sealed record WeddingLovePointAck(int Status) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingLovePoint; }
internal sealed class WeddingLovePointAckEncoder : GameCmdMessageEncoder<WeddingLovePointAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingLovePointAck m)
    {
        var b = new byte[5]; b[0] = 1; BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), m.Status);
        return OnePayload(0x03F5, b);
    }
}
internal sealed record WeddingLovePointNotification(int Character, int Points) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingLovePointNotification; }
internal sealed class WeddingLovePointNotificationEncoder : GameNotiMessageEncoder<WeddingLovePointNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingLovePointNotification m)
    {
        var b = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(b, m.Character); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), m.Points);
        return OnePayload(0x03D3, b);
    }
}
public sealed record WeddingLovePointCommand(SelectedSessionIdentity Actor, BusinessSlot Slot, IReadOnlyList<AccountId> AccountIds)
    : ICommand<MarriageLovePointResult>, IMultiAccountCommand, IRequestSettlementCommand
{ public SelectedSessionIdentity? Session => Actor; }
public sealed class WeddingLovePointCommandHandler(IMarriageCoupleDomainService couple) : ICommandHandler<WeddingLovePointCommand, MarriageLovePointResult>
{
    public ValueTask<MarriageLovePointResult> HandleAsync(CommandExecutionContext c, WeddingLovePointCommand q, CancellationToken ct) => couple.UseLovePointAsync(q.Actor, q.Slot, q.AccountIds, ct);
}
internal sealed class WeddingLovePointHandler(ISelectedSessionDomainService sessions,
    IMarriageQueryDomainService marriage, IInventorySlotMappingRuleService mappings, ICommandBus commands,
    IGameMessageSender sender, SelectedSessionTransitionGate gate) : ClientMessageHandler<WeddingLovePointRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c, WeddingLovePointRequest m, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(c.Connection.ConnectionId);
        var profile = actor is null ? null : (await marriage.SnapshotAsync(actor.AccountId, actor.CharacterId, ct).ConfigureAwait(false)).Partner;
        var peer = profile is null ? null : sessions.FindByCharacter(new(profile.CharacterId));
        await WeddingProposalExecution.RunAsync(gate, actor, peer ?? actor, async () =>
        {
            var result = new MarriageLovePointResult(-1);
            if (actor is not null && m.Slot <= ushort.MaxValue)
            {
                BusinessSlot? slot = null;
                try { slot = mappings.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, checked((int)m.Slot))); }
                catch (InventorySlotMappingException) { }
                if (slot is { } source)
                    result = await commands.SendAsync(new WeddingLovePointCommand(actor, source, await marriage.CoupleAccountsAsync(actor, ct).ConfigureAwait(false)), ct).ConfigureAwait(false);
            }
            var own = new List<IGameServerMessage> { new WeddingLovePointAck(result.Status) };
            var targets = new List<GameExplicitMessageTarget>();
            if (result.Status == 0 && actor is not null && result.State is { } state)
            {
                own.Add(state.Item is { } item
                    ? GameInventoryDelivery.Notification(new(InventoryContainerType.Main, [new(item.ItemUid, checked((int)m.Slot), item.ToCore(), item.AvatarDetail)], state.PreparedAt))
                    : GameInventoryDelivery.ClearedSlotNotification(InventoryContainerType.Main, checked((int)m.Slot), state.PreparedAt));
                own.Add(new WeddingLovePointNotification(actor.CharacterId.Value, result.Points));
            }
            await sender.SendExplicitAsync(c.Connection.ConnectionId, new(own, targets), ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
