using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game;

internal sealed record HousingRoomMessage(HousingSnapshot Snapshot) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x03D8;
    internal override byte[] EncodeBody() => HousingWire.Room(Snapshot);
}
internal sealed record HousingStorageMessage(HousingSnapshot Snapshot) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMarriageHousingStorage; }
internal sealed record HousingTreeMessage(HousingTree? Tree) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMarriageHousingTree; }
internal sealed record HousingRingMessage(byte Level) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x03D1;
    internal override byte[] EncodeBody() => [Level];
}
internal sealed record HousingRewardsMessage(IReadOnlyList<HousingReward> Rewards) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMarriageHousingRewards; }
internal sealed record HousingMoveMessage(HousingRequest Request, HousingSnapshot Snapshot) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMarriageHousingMove; }
internal sealed record HousingDeleteMessage(HousingRequest Request, HousingSnapshot Snapshot) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMarriageHousingDelete; }
internal static class HousingWire
{
    private static byte[] Write(Action<BinaryWriter> action) { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); action(writer); return stream.ToArray(); }
    public static byte[] Room(HousingSnapshot room) => Write(w =>
    { w.Write(room.Placements.Count); foreach (var p in room.Placements.OrderBy(x => x.Slot)) { w.Write(checked((ushort)p.Slot)); w.Write(p.ItemId); } Tree(w, room.Tree); });
    public static byte[] Storage(HousingSnapshot room) => Write(w =>
    { w.Write(room.Storage.Count); foreach (var s in room.Storage.OrderBy(x => x.Slot)) { w.Write(checked((short)s.Slot)); w.Write(s.ItemId); w.Write(s.Count); } w.Write(0); });
    public static byte[] Tree(HousingTree? tree) => Write(w => Tree(w, tree));
    private static void Tree(BinaryWriter w, HousingTree? tree)
    { w.Write(tree?.Kind ?? 0); w.Write(tree is null ? 0 : checked((uint)tree.ExpiresAt.ToUnixTimeSeconds())); w.Write(tree?.Growth ?? 0); w.Write(checked((sbyte)(tree?.Waters ?? 0))); }
    public static byte[] Rewards(IReadOnlyList<HousingReward> rewards) => Write(w =>
    { w.Write(checked((ushort)rewards.Count)); foreach (var r in rewards) { w.Write(r.ItemId); w.Write(r.Count); } });
    private static void Endpoint(BinaryWriter w, HousingEndpoint e, HousingSnapshot room)
    {
        var itemId = e.Kind == 1 ? room.Storage.SingleOrDefault(x => x.Slot == e.Slot)?.ItemId ?? 0 : room.Placements.SingleOrDefault(x => x.Slot == e.Slot)?.ItemId ?? 0;
        var count = e.Kind == 1 ? room.Storage.SingleOrDefault(x => x.Slot == e.Slot)?.Count ?? 0 : itemId == 0 ? 0 : 1;
        w.Write(e.Kind); w.Write(itemId); w.Write(checked((ushort)e.Slot)); w.Write(count);
    }
    public static byte[] Move(HousingMoveMessage message) => Write(w =>
    { w.Write((byte)0); Endpoint(w, message.Request.Source!, message.Snapshot); Endpoint(w, message.Request.Destination!, message.Snapshot); });
    public static byte[] Delete(HousingDeleteMessage message) => Write(w =>
    { w.Write(1); Endpoint(w, new(1, message.Request.Slot, message.Request.ItemId), message.Snapshot); });
}
internal sealed class HousingRoomEncoder : GameNotiMessageEncoder<HousingRoomMessage>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(HousingRoomMessage m) => OnePayload(m.Type, m.EncodeBody()); }
internal sealed class HousingStorageEncoder : GameNotiMessageEncoder<HousingStorageMessage>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(HousingStorageMessage m) => OnePayload(m, HousingWire.Storage(m.Snapshot)); }
internal sealed class HousingTreeEncoder : GameNotiMessageEncoder<HousingTreeMessage>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(HousingTreeMessage m) => OnePayload(m, HousingWire.Tree(m.Tree)); }
internal sealed class HousingRingEncoder : GameNotiMessageEncoder<HousingRingMessage>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(HousingRingMessage m) => OnePayload(m.Type, m.EncodeBody()); }
internal sealed class HousingRewardsEncoder : GameNotiMessageEncoder<HousingRewardsMessage>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(HousingRewardsMessage m) => OnePayload(m, HousingWire.Rewards(m.Rewards)); }
internal sealed class HousingMoveEncoder : GameNotiMessageEncoder<HousingMoveMessage>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(HousingMoveMessage m) => OnePayload(m, HousingWire.Move(m)); }
internal sealed class HousingDeleteEncoder : GameNotiMessageEncoder<HousingDeleteMessage>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(HousingDeleteMessage m) => OnePayload(m, HousingWire.Delete(m)); }

/// <summary>Receiver-owned marriage facts after a native USERINFO rebuild. Never use the observed peer's room.</summary>
internal static class HousingCurrentProjection
{
    internal static async ValueTask<IReadOnlyList<IGameServerMessage>> ReadAsync(
        Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? query,
        Dfo.Server.Data.World.SelectedSessionIdentity receiver, CancellationToken ct)
    {
        if (query is null || await query.ReadAsync(receiver.AccountId, receiver.CharacterId, ct).ConfigureAwait(false) is not { } room) return [];
        return [new Dfo.Server.Protocol.Game.MarriageProposal.WeddingInfoMessage(
            await query.InfoAsync(receiver.AccountId, receiver.CharacterId, ct).ConfigureAwait(false)), new HousingRoomMessage(room)];
    }
}
