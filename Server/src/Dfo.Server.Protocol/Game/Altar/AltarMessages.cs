using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.Altar;

public sealed record AltarPrepareRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameAltarPrepare; }
public sealed record AltarDeathBatchRequest(GameMessageMetadata Metadata, IReadOnlyList<ushort> Actors) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameAltarDeathBatch; }
public sealed record AltarDifficultyRequest(GameMessageMetadata Metadata, byte Difficulty) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameAltarDifficulty; }

internal sealed class AltarPrepareDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.IsEmpty ? new AltarPrepareRequest(metadata) : null;
}
internal sealed class AltarDeathBatchDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty || body.Length != 1 + body[0] * 2) return null;
        var actors = new ushort[body[0]];
        for (var i = 0; i < actors.Length; i++) actors[i] = BinaryPrimitives.ReadUInt16LittleEndian(body[(1 + i * 2)..]);
        return new AltarDeathBatchRequest(metadata, actors);
    }
}
internal sealed class AltarDifficultyDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 1 ? new AltarDifficultyRequest(metadata, body[0]) : null;
}

// ClientStatic: all layouts below were rechecked against the A21 receive handlers.
// They deliberately do not include upstream's malformed 0104 "ExitReady" packet.
internal static class AltarPayloadCodec
{
    public static byte[] Info(int dungeonId, AltarDungeonKind kind)
    {
        var body = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(body, dungeonId);
        if (kind == AltarDungeonKind.Endless) body[6] = 2;
        return body;
    }
    public static byte[] Map(DungeonRunState run) => Map(run.CurrentCell.X, run.CurrentCell.Y, run.Layout.Seed, run.CurrentRoomMapId);
    public static byte[] Map(byte x, byte y, int seed, int mapId)
    {
        var body = new byte[15];
        body[0] = x; body[1] = y;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(2), seed & int.MaxValue);
        body[7] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), mapId);
        return body;
    }
    public static byte[] Wave(IReadOnlyList<AltarSpawn> actors, ushort tail = 0)
    {
        var body = new byte[checked(4 + actors.Count * 17)];
        BinaryPrimitives.WriteUInt16LittleEndian(body, checked((ushort)actors.Count));
        for (var i = 0; i < actors.Count; i++)
        {
            var actor = actors[i]; var row = body.AsSpan(2 + i * 17, 17);
            row[0] = actor.Variant;
            BinaryPrimitives.WriteUInt16LittleEndian(row[1..], actor.Sequence);
            BinaryPrimitives.WriteInt32LittleEndian(row[3..], actor.MonsterCode);
            row[7] = actor.ActorType; row[8] = actor.Level;
            BinaryPrimitives.WriteUInt16LittleEndian(row[9..], actor.Scale);
            BinaryPrimitives.WriteUInt16LittleEndian(row[11..], actor.X);
            BinaryPrimitives.WriteUInt16LittleEndian(row[13..], actor.Y);
            BinaryPrimitives.WriteUInt16LittleEndian(row[15..], actor.Z);
        }
        // A21 altar object +0x194 permits room clear only when this tail is zero.
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(body.Length - 2), tail);
        return body;
    }
    public static byte[] Interval(int round, uint milliseconds)
    {
        var body = new byte[5]; body[0] = checked((byte)round);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), milliseconds); return body;
    }
    public static byte[] DifficultyPrompt(int completed, ushort seconds) =>
        [0, checked((byte)completed), (byte)seconds, (byte)(seconds >> 8)];
    public static byte[] Ranking(uint elapsed, uint completed, uint maximum, uint experience)
    {
        var body = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(body, elapsed);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), completed);
        // No persisted historical record has been established; zero does not invent one.
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(16), maximum);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(20), experience); return body;
    }
    public static byte[] Rewards(int completed, int maximum, IReadOnlyList<AltarParticipantReward> rewards)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)completed)); writer.Write(checked((byte)maximum));
        for (byte slot = 0; slot < 4; slot++)
        {
            var participant = rewards.SingleOrDefault(x => x.Participant.Slot == slot);
            var cards = participant?.Cards ?? [];
            if (cards.Count > 11) throw new InvalidDataException("Too many altar reward cards.");
            writer.Write(checked((byte)cards.Count));
            foreach (var card in cards) { writer.Write(card.ItemId); writer.Write(card.Count); }
        }
        return stream.ToArray();
    }
}
internal sealed record AltarInfoMessage(int DungeonId, AltarDungeonKind Kind) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x00D4; internal override byte[] EncodeBody() => AltarPayloadCodec.Info(DungeonId, Kind); }
internal sealed record AltarMapMessage(DungeonRunState Run) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x00D3; internal override byte[] EncodeBody() => AltarPayloadCodec.Map(Run); }
internal sealed record AltarWaveMessage(IReadOnlyList<AltarSpawn> Actors, ushort Tail = 0) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x00D2; internal override byte[] EncodeBody() => AltarPayloadCodec.Wave(Actors, Tail); }
internal sealed record AltarIntervalMessage(int Round, uint Milliseconds) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x00D7; internal override byte[] EncodeBody() => AltarPayloadCodec.Interval(Round, Milliseconds); }
internal sealed record AltarDifficultyPromptMessage(int Completed, ushort Seconds) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x0129; internal override byte[] EncodeBody() => AltarPayloadCodec.DifficultyPrompt(Completed, Seconds); }
internal sealed record AltarDifficultyResolvedMessage(byte Difficulty) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x0129; internal override byte[] EncodeBody() => [Difficulty]; }
internal sealed record AltarDifficultyResponse(bool Success, byte Difficulty) : GameStateSynchronizationMessage, IGameCmdMessage
{ internal override ushort Type => 0x013D; internal override byte[] EncodeBody() => Success ? [1, Difficulty] : [0, 1]; }
internal sealed record AltarRankingMessage(uint Elapsed, uint Completed, uint Maximum, uint Experience) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x00D0; internal override byte[] EncodeBody() => AltarPayloadCodec.Ranking(Elapsed, Completed, Maximum, Experience); }
internal sealed record AltarRewardMessage(AltarDungeonState State) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x00D1; internal override byte[] EncodeBody() => AltarPayloadCodec.Rewards(State.CompletedRounds, State.Definition.MaxRounds, State.Rewards); }
