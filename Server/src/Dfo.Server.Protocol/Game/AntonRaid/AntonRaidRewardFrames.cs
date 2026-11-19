using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed record AntonRaidRewardRequest(GameMessageMetadata Metadata, ClientMessageType MessageType,
    byte Value, byte Slot = 0) : IClientNetworkMessage;

internal sealed class AntonRaidRewardRequestDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var kind = (ClientMessageType)((metadata.Selector << 16) | metadata.Type);
        return kind switch
        {
            ClientMessageType.GameFinishAntonRaidMovie when body.Length == 1 => new AntonRaidRewardRequest(metadata, kind, body[0]),
            ClientMessageType.GameSelectAntonRaidReward when body.Length == 2 => new AntonRaidRewardRequest(metadata, kind, body[0], body[1]),
            _ => null,
        };
    }
}

internal sealed record AntonRaidRewardNotice(ServerMessageType MessageType, byte[] Payload) : IGameNotiMessage
{
    internal static AntonRaidRewardNotice Result(AntonRaidRewardSnapshot snapshot, bool eligible)
    {
        var seed = snapshot.Seed;
        var body = new byte[10];
        body[0] = seed.Succeeded ? (byte)0 : (byte)1;
        body[1] = seed.Phase;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(2),
            (uint)Math.Clamp((seed.EndedAt - seed.StartedAt).TotalSeconds, 0, uint.MaxValue));
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6), (ushort)Math.Min(seed.DeathCount, ushort.MaxValue));
        body[8] = seed.Succeeded ? checked((byte)(snapshot.Rank + 2)) : (byte)0;
        body[9] = eligible ? (byte)0 : (byte)1;
        return new(ServerMessageType.GameAntonRaidResult, body);
    }

    internal static AntonRaidRewardNotice State(byte state, byte argument) =>
        new(ServerMessageType.GameAntonRaidState, [state, argument]);

    internal static AntonRaidRewardNotice MoviePrompt() => new(ServerMessageType.GameAntonRaidMoviePrompt, new byte[8]);

    internal static AntonRaidRewardNotice Cards(AntonRaidRewardKind kind,
        IReadOnlyList<(ushort User, AntonRaidRewardCard Card)> cards)
    {
        var body = new byte[2 + cards.Count * 10];
        body[0] = (byte)kind;
        body[1] = checked((byte)cards.Count);
        for (var i = 0; i < cards.Count; i++)
        {
            var (user, card) = cards[i];
            var row = body.AsSpan(2 + 10 * i, 10);
            BinaryPrimitives.WriteUInt16LittleEndian(row, user);
            row[2] = card.Slot;
            row[3] = card.Flags;
            BinaryPrimitives.WriteUInt32LittleEndian(row[4..], checked((uint)card.Item.ItemId));
            BinaryPrimitives.WriteUInt16LittleEndian(row[8..], (ushort)Math.Min(card.Item.Quantity, ushort.MaxValue));
        }
        return new(ServerMessageType.GameAntonRaidRewardList, body);
    }
}

internal sealed class AntonRaidRewardNoticeEncoder : GameNotiMessageEncoder<AntonRaidRewardNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AntonRaidRewardNotice message) =>
        OnePayload(message, message.Payload);
}
