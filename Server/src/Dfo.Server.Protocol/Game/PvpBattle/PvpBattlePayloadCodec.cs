using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PvpBattle;

/// <summary>The battle reports' bodies and the battle's notices.</summary>
/// <remarks>
/// A malformed report is never thrown on (a throwing decoder drops the connection): the command logs it and leaves
/// the battle as it is. Layouts: upstream <c>Parsers/Pvp/*</c>, <c>PvpRoomNotificationBuilder</c> and
/// <c>PvpRecordBodyBuilder</c> (design-r1 §5, research.md §2); the client's static reader gives only lower bounds.
/// </remarks>
internal static class PvpBattlePayloadCodec
{
    public const int DeathLength = 5;
    public const int TimeOutLength = 32;
    public const int RecordLength = 51;
    public const byte Nobody = 0xFF;

    public static ConnectP2pPvpRequest DecodeConnectP2p(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < 1 || body[0] > PvpRoomSnapshot.SeatCount || body.Length != 1 + body[0] * 2)
            return new(metadata, [], false, body.Length);
        var peers = ImmutableArray.CreateBuilder<(byte, byte)>(body[0]);
        for (var index = 0; index < body[0]; index++)
        {
            var seat = body[1 + index * 2];
            if (seat >= PvpRoomSnapshot.SeatCount) return new(metadata, [], false, body.Length);
            peers.Add((seat, body[2 + index * 2]));
        }
        return new(metadata, peers.MoveToImmutable(), true, body.Length);
    }

    public static DiePvpCharacterRequest DecodeDeath(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == DeathLength
            ? new(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body), BinaryPrimitives.ReadUInt16LittleEndian(body[2..]),
                body[4], true, body.Length)
            : new(metadata, 0, 0, 0, false, body.Length);

    public static PvpTimeOutRequest DecodeTimeOut(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new(metadata, [.. body], body.Length == TimeOutLength);

    public static byte[] Encode(PvpDeathMessage message) =>
        [checked((byte)message.VictimSeat), message.KillerSeat is { } killer ? checked((byte)killer) : Nobody];

    /// <summary>
    /// <c>u8 winner seat, i32 0, u8 own grade, i32 own experience change, u8 n</c>, then per fighter
    /// <c>u16 user, i32 kills, u8 grade, i32 experience, i32 rank point, i32 peak</c>, then <c>u16 0xFFFF, u16 0xFFFF, i32 0,
    /// u8 draw, u8 0xFF, i32 0, u8 0</c> (upstream <c>BuildEndPvpBody</c>).
    /// </summary>
    public static byte[] Encode(EndPvpMessage message)
    {
        if (message.Fighters.Count is < 1 or > PvpRoomSnapshot.SeatCount)
            throw new InvalidDataException("A battle result lists one to eight fighters.");
        var body = new byte[26 + 19 * message.Fighters.Count];
        var offset = 0;
        body[offset++] = message.WinnerSeat;
        // The free duel's win point.
        offset += Int(body, offset, 0);
        body[offset++] = message.Grade;
        offset += Int(body, offset, Clamp(message.ExperienceChange));
        body[offset++] = (byte)message.Fighters.Count;
        foreach (var fighter in message.Fighters)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), fighter.WireUserId);
            offset += sizeof(ushort);
            offset += Int(body, offset, fighter.Kills);
            body[offset++] = fighter.Grade;
            offset += Int(body, offset, Clamp(fighter.Experience));
            offset += Int(body, offset, fighter.Rating.OfficialPoints);
            offset += Int(body, offset, fighter.Rating.PeakPoints);
        }
        // No ace and no all-kill recipient.
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), ushort.MaxValue);
        offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), ushort.MaxValue);
        offset += sizeof(ushort);
        // No reward experience.
        offset += Int(body, offset, 0);
        body[offset++] = message.Draw ? (byte)1 : (byte)0;
        // No mission result.
        body[offset++] = Nobody;
        // The unnamed result-event value upstream leaves at zero.
        offset += Int(body, offset, 0);
        // No reward item.
        body[offset++] = 0;
        return offset == body.Length ? body : throw new InvalidOperationException("The battle result was not filled exactly.");
    }

    /// <summary>
    /// 51 bytes: <c>i32 wins, i32 losses, i32 experience, i32 grade lower bound, i32 grade upper bound, u8 grade,
    /// u8 rating grade, i32 -1, i32 rank point, i32 peak, i32 placement games, u8 0, i32 0, i32 0, i32 0</c>
    /// (upstream <c>PvpRecordBodyBuilder.BuildBody</c>, the ordinary room's form without the total-match tail).
    /// </summary>
    public static byte[] Encode(PvpRecordMessage message)
    {
        var record = message.Record;
        const int candidateLength = 26;
        if (message.TotalMatchExperience.Count is not (0 or Dfo.Server.Abstractions.Pvp.PvpTotalMatchTeam.MemberCount))
            throw new InvalidDataException("A total-match record reports all three candidates in lineup order.");
        var body = new byte[RecordLength + candidateLength * message.TotalMatchExperience.Count];
        var offset = 0;
        offset += Int(body, offset, record.Wins);
        offset += Int(body, offset, record.Losses);
        offset += Int(body, offset, Clamp(record.Experience));
        offset += Int(body, offset, Clamp(record.LowerBound));
        offset += Int(body, offset, Clamp(record.UpperBound));
        body[offset++] = record.Grade;
        body[offset++] = record.RatingGrade;
        // Upstream: brings the bar up to date without a separate grade-change animation.
        offset += Int(body, offset, -1);
        offset += Int(body, offset, record.Rating.OfficialPoints);
        offset += Int(body, offset, record.Rating.PeakPoints);
        offset += Int(body, offset, record.Rating.PlacementGames);
        // Grade-change status.
        body[offset++] = 0;
        offset += Int(body, offset, 0);
        // Total-match point and placement games.
        offset += Int(body, offset, record.TotalMatchRating.OfficialPoints);
        offset += Int(body, offset, record.TotalMatchRating.PlacementGames);
        foreach (var candidate in message.TotalMatchExperience)
        {
            offset += Int(body, offset, Clamp(candidate.Before));
            offset += Int(body, offset, Clamp(candidate.After));
            body[offset++] = candidate.PreviousGrade;
            body[offset++] = candidate.Grade;
            offset += Int(body, offset, Clamp(candidate.PreviousBand.LowerBound));
            offset += Int(body, offset, Clamp(candidate.PreviousBand.UpperBound));
            offset += Int(body, offset, Clamp(candidate.Band.LowerBound));
            offset += Int(body, offset, Clamp(candidate.Band.UpperBound));
        }
        return offset == body.Length ? body : throw new InvalidOperationException("The duel record was not filled exactly.");
    }

    /// <summary>The wire's <c>i32</c>; the archive's top-grade sentinel can lie beyond it.</summary>
    private static int Clamp(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    private static int Int(byte[] body, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset), value);
        return sizeof(int);
    }
}

internal sealed class CompleteLoadPvpDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new CompleteLoadPvpRequest(metadata, body.Length);
}

internal sealed class ConnectP2pPvpDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        PvpBattlePayloadCodec.DecodeConnectP2p(metadata, body);
}

internal sealed class PvpHeartbeatDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new PvpHeartbeatRequest(metadata, body.Length);
}

internal sealed class PvpRequestFightDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new PvpRequestFightRequest(metadata, body.Length);
}

internal sealed class DiePvpCharacterDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        PvpBattlePayloadCodec.DecodeDeath(metadata, body);
}

internal sealed class PvpTimeOutDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        PvpBattlePayloadCodec.DecodeTimeOut(metadata, body);
}

internal sealed class PvpRankResponseDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new PvpRankResponseRequest(metadata, body.Length);
}

internal sealed class EndPvpResultDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new EndPvpResultRequest(metadata, body.Length);
}

internal sealed class PvpDeathEncoder : GameNotiMessageEncoder<PvpDeathMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpDeathMessage message) =>
        OnePayload(message, PvpBattlePayloadCodec.Encode(message));
}

internal sealed class PvpTurnPlayerEncoder : GameNotiMessageEncoder<PvpTurnPlayerMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpTurnPlayerMessage message) =>
        OnePayload(message, new byte[] { 2, checked((byte)message.FirstSeat), 0, checked((byte)message.SecondSeat), 0 });
}

internal sealed class PvpRequestFightEncoder : GameNotiMessageEncoder<PvpRequestFightMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpRequestFightMessage message) =>
        OnePayload(message, new byte[] { checked((byte)message.Seat) });
}

internal sealed class PvpRankRequestEncoder : GameNotiMessageEncoder<PvpRankRequestMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpRankRequestMessage message) =>
        OnePayload(message, Array.Empty<byte>());
}

internal sealed class EndPvpEncoder : GameNotiMessageEncoder<EndPvpMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(EndPvpMessage message) =>
        OnePayload(message, PvpBattlePayloadCodec.Encode(message));
}

internal sealed class PvpRecordEncoder : GameNotiMessageEncoder<PvpRecordMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpRecordMessage message) =>
        OnePayload(message, PvpBattlePayloadCodec.Encode(message));
}
