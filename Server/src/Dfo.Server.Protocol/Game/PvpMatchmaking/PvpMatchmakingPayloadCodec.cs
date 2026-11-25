using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PvpMatchmaking;

internal static class PvpMatchmakingPayloadCodec
{
    private const byte Refused = 0x13;
    private const byte DuelGroup = 1;

    public static JoinPvpMatchmakingRequest DecodeJoin(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 14 || DecodeQueueKey(body[..3]) is not { } mode || body[3] != 0 || body[4] > 1 || body[13] > 1
            || (mode != PvpBattleMode.Individual && body[4] != 0)
            || (body[13] == 1 && mode == PvpBattleMode.Relay))
            return new(metadata, null, []);

        var excluded = ImmutableArray.CreateBuilder<CharacterCareer>(4);
        for (var offset = 5; offset < 13; offset += 2)
        {
            var job = body[offset];
            var growType = body[offset + 1];
            if (job == byte.MaxValue && growType == byte.MaxValue) continue;
            if (body[4] == 0 || job == byte.MaxValue || growType > 15) return new(metadata, null, []);
            excluded.Add(new(job, growType));
        }
        return new(metadata, mode, excluded.ToImmutable()) { Colosseum = body[13] == 1 };
    }

    public static PvpBattleMode? DecodeQueueKey(ReadOnlySpan<byte> body)
    {
        if (body.Length != 3 || body[0] != DuelGroup) return null;
        var category = BinaryPrimitives.ReadUInt16LittleEndian(body[1..]);
        if (category == 0) return PvpBattleMode.Individual;
        if (category == 1) return PvpBattleMode.Relay;
        return category == 7 ? PvpBattleMode.TotalMatch : null;
    }

    public static byte[] Encode(JoinPvpMatchmakingResponse response) => response.AcceptedMode is { } mode
        ? GameCmdEnvelope.Accept([DuelGroup, mode switch
        {
            PvpBattleMode.Individual => 0,
            PvpBattleMode.Relay => 1,
            PvpBattleMode.TotalMatch => 7,
            PvpBattleMode.Team or PvpBattleMode.DeathMatchIndividual or PvpBattleMode.DeathMatchTeam
                or PvpBattleMode.Practice => throw new InvalidDataException("This duel mode has no matchmaking category."),
        }, 0, response.Colosseum ? (byte)1 : (byte)0])
        : GameCmdEnvelope.Refuse(Refused);

    public static byte[] Encode(CancelPvpMatchmakingResponse response) => response.Accepted
        ? GameCmdEnvelope.Accept([])
        : GameCmdEnvelope.Refuse(Refused);

    public static byte[] Encode(PvpMatchmakingCountNotice message)
    {
        var body = new byte[message.Counts.TotalMatch is null ? 21 : 29];
        body[4] = message.Counts.TotalMatch is null ? (byte)2 : (byte)3;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9), message.Counts.Individual);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(13), 1);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(17), message.Counts.Relay);
        if (message.Counts.TotalMatch is { } total)
        {
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(21), 7);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(25), total);
        }
        return body;
    }

    public static byte[] Encode(PvpMatchmakingRoster message)
    {
        const int memberLength = 13;
        var body = new byte[6 + memberLength * message.Opponents.Count];
        body[0] = DuelGroup;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), message.RoomNumber);
        body[5] = checked((byte)message.Opponents.Count);
        for (var index = 0; index < message.Opponents.Count; index++)
        {
            var opponent = message.Opponents[index];
            var member = body.AsSpan(6 + memberLength * index, memberLength);
            BinaryPrimitives.WriteUInt16LittleEndian(member, opponent.UserId);
            member[2] = opponent.Side;
            BinaryPrimitives.WriteInt32LittleEndian(member[3..], opponent.OfficialPoints);
            member[7] = opponent.Grade;
            BinaryPrimitives.WriteInt32LittleEndian(member[8..], opponent.OfficialPoints);
        }
        return body;
    }
}

internal sealed class JoinPvpMatchmakingDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        PvpMatchmakingPayloadCodec.DecodeJoin(metadata, body);
}

internal sealed class CancelPvpMatchmakingDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new CancelPvpMatchmakingRequest(metadata, PvpMatchmakingPayloadCodec.DecodeQueueKey(body));
}

internal sealed class ConfirmPvpMatchmakingDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new ConfirmPvpMatchmakingRequest(metadata, body.Length == sizeof(uint) ? BinaryPrimitives.ReadUInt32LittleEndian(body) : null);
}

internal sealed class PvpMatchmakingCountDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new PvpMatchmakingCountRequest(metadata, body.IsEmpty);
}

internal sealed class JoinPvpMatchmakingResponseEncoder : GameCmdMessageEncoder<JoinPvpMatchmakingResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(JoinPvpMatchmakingResponse message) =>
        OnePayload(message, PvpMatchmakingPayloadCodec.Encode(message));
}

internal sealed class CancelPvpMatchmakingResponseEncoder : GameCmdMessageEncoder<CancelPvpMatchmakingResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CancelPvpMatchmakingResponse message) =>
        OnePayload(message, PvpMatchmakingPayloadCodec.Encode(message));
}

internal sealed class PvpMatchmakingCountEncoder : GameNotiMessageEncoder<PvpMatchmakingCountNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpMatchmakingCountNotice message) =>
        OnePayload(message, PvpMatchmakingPayloadCodec.Encode(message));
}

internal sealed class PvpMatchmakingRosterEncoder : GameNotiMessageEncoder<PvpMatchmakingRoster>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpMatchmakingRoster message) =>
        OnePayload(message, PvpMatchmakingPayloadCodec.Encode(message));
}

internal sealed class PvpMatchmakingStateEncoder : GameNotiMessageEncoder<PvpMatchmakingStateNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpMatchmakingStateNotice message) =>
        OnePayload(message, new byte[] { message.State });
}

internal sealed class PvpActorLoadedEncoder : GameNotiMessageEncoder<PvpActorLoaded>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpActorLoaded message)
    {
        var body = new byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.UserId);
        return OnePayload(message, body);
    }
}

internal sealed class PvpActorsConnectedEncoder : GameNotiMessageEncoder<PvpActorsConnected>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpActorsConnected message) =>
        OnePayload(message, ReadOnlyMemory<byte>.Empty);
}

internal sealed class PvpMatchmakingCompleteEncoder : GameNotiMessageEncoder<PvpMatchmakingComplete>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpMatchmakingComplete message) =>
        OnePayload(message, ReadOnlyMemory<byte>.Empty);
}
