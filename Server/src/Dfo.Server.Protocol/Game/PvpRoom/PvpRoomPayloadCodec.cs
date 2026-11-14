using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.PvpRoom;

/// <summary>The room commands' bodies, read whole or not at all.</summary>
/// <remarks>
/// A malformed body is never thrown on: a throwing decoder drops the connection, and a room request is not
/// worth a session. The decoders hand the command a malformed marker instead, and the command answers it as a
/// refusal and logs it. Layouts: upstream <c>Parsers/Pvp/*</c> (design-r1 §5, research.md §2).
/// </remarks>
internal static class PvpRoomPayloadCodec
{
    /// <summary>Upstream <c>MakePvpRoomRequest.MaximumRoomNameBytes</c>.</summary>
    public const int MaximumNameBytes = 29;

    /// <summary>Upstream <c>MakePvpRoomRequest.MaximumPasswordBytes</c>.</summary>
    public const int MaximumPasswordBytes = 8;

    public static MakePvpRoomInput? DecodeMakeRoom(ReadOnlySpan<byte> body)
    {
        var offset = 0;
        if (!TryByte(body, ref offset, out var nameType)
            // Upstream reads the name type as a signed char and refuses a negative one.
            || nameType > 0x7F)
            return null;
        ImmutableArray<byte> name = [];
        if (nameType == 0 && !TryCounted(body, ref offset, MaximumNameBytes, out name))
            return null;
        if (!TryInt16(body, ref offset, out var map)) return null;
        if (!TryPassword(body, ref offset, out var password)) return null;
        if (!TryByte(body, ref offset, out var special)) return null;
        return offset == body.Length ? new MakePvpRoomInput(nameType, name, map, password, special) : null;
    }

    public static EnterPvpRoomRequest DecodeEnterRoom(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < sizeof(ushort)) return new(metadata, 0, null, false);
        var number = BinaryPrimitives.ReadUInt16LittleEndian(body);
        var offset = sizeof(ushort);
        return TryPassword(body, ref offset, out var password) && offset == body.Length
            ? new(metadata, number, password, true)
            : new(metadata, number, null, false);
    }

    public static SetPvpSeatStateRequest DecodeSeatState(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 2 ? new(metadata, body[0], body[1], true) : new(metadata, 0, 0, false);

    public static SetPvpReadyStateRequest DecodeReadyState(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 1 ? new(metadata, body[0] != 0, true) : new(metadata, false, false);

    /// <summary>Spec RQ-075: exactly one byte; the client's builders disagree on the shape, so any other length is refused.</summary>
    public static SetPvpTeamModeRequest DecodeTeamMode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 1 ? new(metadata, body[0], true) : new(metadata, 0, false);

    public static SetPvpMapIndexRequest DecodeMapIndex(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == sizeof(short)
            ? new(metadata, BinaryPrimitives.ReadInt16LittleEndian(body), true)
            : new(metadata, 0, false);

    public static byte[] Encode(PvpRoomCommandRefusal refusal) => GameCmdEnvelope.Refuse(refusal.Reason);

    public static byte[] Encode(EnterPvpRoomAccepted accepted)
    {
        if (accepted.Ready.Length != PvpRoomSnapshot.SeatCount)
            throw new InvalidDataException("An entered room reports exactly eight ready flags.");
        return GameCmdEnvelope.Accept([.. accepted.Ready.Select(ready => ready ? (byte)1 : (byte)0)]);
    }

    public static byte[] Encode(PvpRoomInfoMessage message) =>
        SupplementalSynchronizationCodec.EncodePvpRoomInfoBody(message.Rooms);

    /// <summary>11 bytes: <c>u16 number, u8 state, u8 owner seat, i16 map, u8 mode, i32 matching type</c>.</summary>
    public static byte[] Encode(PvpRoomStateMessage message)
    {
        var body = new byte[11];
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.Number);
        body[2] = message.State;
        body[3] = message.OwnerSeat;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(4), message.MapIndex);
        body[6] = message.Mode;
        // The matching type: 0 for a free room, the only kind the first phase opens.
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), 0);
        return body;
    }

    /// <summary><c>u16 number, u8 mode, u8 n</c> then <c>n × (u8 seat, u8 state, u16 user)</c>.</summary>
    public static byte[] Encode(PvpSeatStateMessage message)
    {
        if (message.Seats.Count is < 1 or > PvpRoomSnapshot.SeatCount)
            throw new InvalidDataException("A seat-state notice names one to eight seats.");
        var body = new byte[4 + message.Seats.Count * 4];
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.Number);
        body[2] = message.Mode;
        body[3] = (byte)message.Seats.Count;
        for (var index = 0; index < message.Seats.Count; index++)
        {
            var (seat, state) = message.Seats[index];
            var offset = 4 + index * 4;
            body[offset] = checked((byte)seat);
            body[offset + 1] = SupplementalSynchronizationCodec.PvpSeatStateByte(state);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset + 2), SupplementalSynchronizationCodec.PvpSeatUserId(state));
        }
        return body;
    }

    public static byte[] Encode(PvpReadyStateMessage message) =>
        [checked((byte)message.Seat), message.Ready ? (byte)1 : (byte)0];

    public static byte[] Encode(StartPvpMessage message) => [message.Map, message.Mode];

    /// <summary>
    /// The domain side a seat-state byte asks for: 0 an individual fighter, 1 and 2 the teams. Null for any other
    /// byte, the domain's <see cref="PvpTeam"/> values never being read off the wire as they stand.
    /// </summary>
    public static PvpTeam? TeamOf(byte state) => state switch
    {
        0 => PvpTeam.Individual,
        1 => PvpTeam.First,
        2 => PvpTeam.Second,
        _ => null,
    };

    private static bool TryPassword(ReadOnlySpan<byte> body, ref int offset, out ImmutableArray<byte>? password)
    {
        password = null;
        if (!TryByte(body, ref offset, out var flag) || flag > 1) return false;
        if (flag == 0) return true;
        if (!TryCounted(body, ref offset, MaximumPasswordBytes, out var value)) return false;
        password = value;
        return true;
    }

    private static bool TryCounted(ReadOnlySpan<byte> body, ref int offset, int maximum, out ImmutableArray<byte> value)
    {
        value = [];
        if (body.Length - offset < sizeof(int)) return false;
        var length = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
        offset += sizeof(int);
        if (length < 0 || length > maximum || body.Length - offset < length) return false;
        value = [.. body.Slice(offset, length)];
        offset += length;
        return true;
    }

    private static bool TryByte(ReadOnlySpan<byte> body, ref int offset, out byte value)
    {
        value = 0;
        if (offset >= body.Length) return false;
        value = body[offset++];
        return true;
    }

    private static bool TryInt16(ReadOnlySpan<byte> body, ref int offset, out short value)
    {
        value = 0;
        if (body.Length - offset < sizeof(short)) return false;
        value = BinaryPrimitives.ReadInt16LittleEndian(body[offset..]);
        offset += sizeof(short);
        return true;
    }
}

/// <summary>The one place a room refusal becomes a wire reason.</summary>
/// <remarks>
/// Codes from upstream <c>FreeDuelRoomRegistry</c> and <c>PvpRoomHandler</c>: 19 the generic "not now" (wrong
/// state, not a member, not allowed in), 22 room gone or the start conditions unmet, 6 wrong password, 4 room
/// full, 8 no authority or a malformed request, 14 unequal teams. A refusal upstream has no counterpart for takes
/// the nearest of these, noted on its arm. Invitation refusals answer on REQUEST_PEER / RESPONSE_PEER with the
/// party codes those handlers already show (<see cref="PartyRefusalCode"/>).
/// </remarks>
internal static class PvpRoomRefusalCode
{
    public const byte NotNow = 19;
    public const byte RoomGoneOrNotReady = 22;
    public const byte WrongPassword = 6;
    public const byte Full = 4;
    public const byte NoAuthority = 8;
    public const byte UnequalTeams = 14;

    public static byte Of(PvpRoomRefusal refusal) => refusal switch
    {
        PvpRoomRefusal.SessionNotCurrent or PvpRoomRefusal.NotDuelChannel or PvpRoomRefusal.InParty
            or PvpRoomRefusal.AlreadySeated or PvpRoomRefusal.NotSeated or PvpRoomRefusal.NotWaiting => NotNow,
        PvpRoomRefusal.RoomMissing => RoomGoneOrNotReady,
        PvpRoomRefusal.WrongPassword => WrongPassword,
        // Upstream refuses a password longer than eight bytes in its parser, with 19.
        PvpRoomRefusal.InvalidPassword => NotNow,
        PvpRoomRefusal.Full => Full,
        PvpRoomRefusal.NotOwner => NoAuthority,
        // Upstream accepts modes 1-6 in its parser and fails the unsupported ones later with 19.
        PvpRoomRefusal.UnsupportedMode => NotNow,
        // Upstream has no map command; an index outside the archive's order is a request it could not make.
        PvpRoomRefusal.InvalidMap => NotNow,
        // Upstream: a seat or side the actor may not set is 8.
        PvpRoomRefusal.InvalidSeat or PvpRoomRefusal.InvalidTeam => NoAuthority,
        // Upstream is_closeable_seat_state: 19.
        PvpRoomRefusal.LastOpenSeat => NotNow,
        // Upstream lets a lone owner's start fall through silently; 22 is its "start conditions unmet".
        PvpRoomRefusal.TooFewMembers or PvpRoomRefusal.NotAllReady => RoomGoneOrNotReady,
        PvpRoomRefusal.UnbalancedTeams => UnequalTeams,
        // Invitation refusals reach the room commands only through a room the actor is not in; 19 as upstream's
        // invite error.
        PvpRoomRefusal.TargetUnavailable or PvpRoomRefusal.TargetNotInLobby or PvpRoomRefusal.TargetSeated
            or PvpRoomRefusal.TargetInParty or PvpRoomRefusal.Blacklisted or PvpRoomRefusal.InviteMissing
            or PvpRoomRefusal.InviteExpired => NotNow,
        // Battle reports are not room commands; should one reach here it is the generic "not now".
        PvpRoomRefusal.WrongPhase or PvpRoomRefusal.NotFighter or PvpRoomRefusal.NotAlive or PvpRoomRefusal.TooEarly
            or PvpRoomRefusal.AlreadyReported => NotNow,
        // The owner's start while the server stops: upstream has no such state; 19, the room is not startable now.
        PvpRoomRefusal.ShuttingDown => NotNow,
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Not a duel room refusal."),
    };

    /// <summary>REQUEST_PEER type 2 refused: <c>00 reason 02</c> with the party handler's codes.</summary>
    public static byte ForInvite(PvpRoomRefusal refusal) => refusal switch
    {
        PvpRoomRefusal.TargetUnavailable or PvpRoomRefusal.SessionNotCurrent => PartyRefusalCode.PeerLeftGame,
        PvpRoomRefusal.Full => PartyRefusalCode.PartyFullOrEmptySlot,
        PvpRoomRefusal.TargetInParty => PartyRefusalCode.AlreadyInParty,
        _ => PartyRefusalCode.Refused,
    };

    /// <summary>RESPONSE_PEER type 2 refused: <c>00 reason 02</c>; upstream answers every one with 19.</summary>
    public static byte ForAcceptance(PvpRoomRefusal refusal) => refusal switch
    {
        PvpRoomRefusal.Full => PartyRefusalCode.PartyFullOrEmptySlot,
        _ => PartyRefusalCode.Refused,
    };
}

internal sealed class MakePvpRoomDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new MakePvpRoomRequest(metadata, PvpRoomPayloadCodec.DecodeMakeRoom(body));
}

internal sealed class EnterPvpRoomDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        PvpRoomPayloadCodec.DecodeEnterRoom(metadata, body);
}

internal sealed class SetPvpSeatStateDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        PvpRoomPayloadCodec.DecodeSeatState(metadata, body);
}

internal sealed class SetPvpReadyStateDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        PvpRoomPayloadCodec.DecodeReadyState(metadata, body);
}

internal sealed class SetPvpTeamModeDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        PvpRoomPayloadCodec.DecodeTeamMode(metadata, body);
}

internal sealed class SetPvpMapIndexDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        PvpRoomPayloadCodec.DecodeMapIndex(metadata, body);
}

internal sealed class PvpRoomCommandRefusalEncoder : GameCmdMessageEncoder<PvpRoomCommandRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpRoomCommandRefusal message) =>
        OnePayload(message, PvpRoomPayloadCodec.Encode(message));
}

internal sealed class EnterPvpRoomAcceptedEncoder : GameCmdMessageEncoder<EnterPvpRoomAccepted>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(EnterPvpRoomAccepted message) =>
        OnePayload(message, PvpRoomPayloadCodec.Encode(message));
}

internal sealed class PvpRoomInfoEncoder : GameNotiMessageEncoder<PvpRoomInfoMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpRoomInfoMessage message) =>
        OnePayload(message, PvpRoomPayloadCodec.Encode(message));
}

internal sealed class PvpRoomStateEncoder : GameNotiMessageEncoder<PvpRoomStateMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpRoomStateMessage message) =>
        OnePayload(message, PvpRoomPayloadCodec.Encode(message));
}

internal sealed class PvpSeatStateEncoder : GameNotiMessageEncoder<PvpSeatStateMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpSeatStateMessage message) =>
        OnePayload(message, PvpRoomPayloadCodec.Encode(message));
}

internal sealed class PvpReadyStateEncoder : GameNotiMessageEncoder<PvpReadyStateMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpReadyStateMessage message) =>
        OnePayload(message, PvpRoomPayloadCodec.Encode(message));
}

internal sealed class StartPvpEncoder : GameNotiMessageEncoder<StartPvpMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(StartPvpMessage message) =>
        OnePayload(message, PvpRoomPayloadCodec.Encode(message));
}
