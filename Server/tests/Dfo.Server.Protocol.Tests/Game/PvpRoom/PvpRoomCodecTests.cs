using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.PvpChannelInfo;
using Dfo.Server.Protocol.Game.PvpRoom;

namespace Dfo.Server.Protocol.Tests.Game.PvpRoom;

/// <summary>The room commands' and notices' bytes (design-r1 §5, research.md §2, upstream parsers and builders).</summary>
public sealed class PvpRoomCodecTests
{
    private static readonly GameMessageMetadata Meta = GameMessageMetadata.Empty;

    [Fact]
    public void Make_room_reads_name_map_password_and_special()
    {
        byte[] body = [0, 2, 0, 0, 0, 0x41, 0x42, 3, 0, 1, 3, 0, 0, 0, 1, 2, 3, 0];
        var input = Assert.IsType<MakePvpRoomInput>(PvpRoomPayloadCodec.DecodeMakeRoom(body));
        Assert.Equal(0, input.NameType);
        Assert.Equal(new byte[] { 0x41, 0x42 }, input.Name.ToArray());
        Assert.Equal(3, input.MapIndex);
        Assert.Equal(new byte[] { 1, 2, 3 }, input.Password!.Value.ToArray());
        Assert.Equal(0, input.Special);

        var unnamed = Assert.IsType<MakePvpRoomInput>(PvpRoomPayloadCodec.DecodeMakeRoom([5, 0, 0, 0, 1]));
        Assert.Equal(5, unnamed.NameType);
        Assert.Empty(unnamed.Name);
        Assert.Null(unnamed.Password);
        Assert.Equal(1, unnamed.Special);
    }

    [Theory]
    [InlineData(new byte[] { 0, 30, 0, 0, 0 })] // name longer than 29 bytes
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 9, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0 })] // password of 9 bytes
    [InlineData(new byte[] { 5, 0, 0, 2, 0 })] // password flag 2
    [InlineData(new byte[] { 5, 0, 0, 0, 0, 0 })] // trailing byte
    [InlineData(new byte[] { 5, 0, 0, 0 })] // no special byte
    [InlineData(new byte[] { 0x80, 0, 0, 0, 0 })] // negative name type
    [InlineData(new byte[] { })]
    public void Make_room_refuses_anything_but_one_whole_request(byte[] body) =>
        Assert.Null(PvpRoomPayloadCodec.DecodeMakeRoom(body));

    [Fact]
    public void Enter_room_reads_number_and_optional_password()
    {
        var open = PvpRoomPayloadCodec.DecodeEnterRoom(Meta, [7, 0, 0]);
        Assert.True(open.WellFormed);
        Assert.Equal(7, open.Number);
        Assert.Null(open.Password);

        var locked = PvpRoomPayloadCodec.DecodeEnterRoom(Meta, [7, 1, 1, 2, 0, 0, 0, 0x61, 0x62]);
        Assert.True(locked.WellFormed);
        Assert.Equal(0x0107, locked.Number);
        Assert.Equal(new byte[] { 0x61, 0x62 }, locked.Password!.Value.ToArray());
    }

    [Theory]
    [InlineData(new byte[] { 7, 0, 0, 0 })]
    [InlineData(new byte[] { 7, 0, 1, 9, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 })]
    [InlineData(new byte[] { 7, 0, 1, 2, 0, 0, 0, 1 })]
    [InlineData(new byte[] { 7, 0 })]
    [InlineData(new byte[] { 7 })]
    public void Enter_room_marks_a_malformed_body(byte[] body) =>
        Assert.False(PvpRoomPayloadCodec.DecodeEnterRoom(Meta, body).WellFormed);

    [Fact]
    public void Seat_ready_mode_and_map_take_exactly_their_fixed_lengths()
    {
        Assert.Equal((3, 0xFE, true), Seat([3, 0xFE]));
        Assert.False(PvpRoomPayloadCodec.DecodeSeatState(Meta, [3]).WellFormed);
        Assert.False(PvpRoomPayloadCodec.DecodeSeatState(Meta, [3, 1, 0]).WellFormed);

        Assert.True(PvpRoomPayloadCodec.DecodeReadyState(Meta, [5]).Ready);
        Assert.False(PvpRoomPayloadCodec.DecodeReadyState(Meta, [0]).Ready);
        Assert.False(PvpRoomPayloadCodec.DecodeReadyState(Meta, [1, 0]).WellFormed);

        // Spec RQ-075 / AC-075: 0x0036 is one byte and nothing else.
        Assert.Equal((byte)2, PvpRoomPayloadCodec.DecodeTeamMode(Meta, [2]).Mode);
        Assert.False(PvpRoomPayloadCodec.DecodeTeamMode(Meta, [2, 0]).WellFormed);
        Assert.False(PvpRoomPayloadCodec.DecodeTeamMode(Meta, []).WellFormed);

        Assert.Equal((short)-1, PvpRoomPayloadCodec.DecodeMapIndex(Meta, [0xFF, 0xFF]).Index);
        Assert.False(PvpRoomPayloadCodec.DecodeMapIndex(Meta, [1, 0, 0]).WellFormed);
    }

    [Fact]
    public void Seat_state_bytes_map_to_sides_without_reading_the_domain_enum_off_the_wire()
    {
        Assert.Equal(PvpTeam.Individual, PvpRoomPayloadCodec.TeamOf(0));
        Assert.Equal(PvpTeam.First, PvpRoomPayloadCodec.TeamOf(1));
        Assert.Equal(PvpTeam.Second, PvpRoomPayloadCodec.TeamOf(2));
        Assert.Null(PvpRoomPayloadCodec.TeamOf(3));
        Assert.Null(PvpRoomPayloadCodec.TeamOf(0xFE));
    }

    [Fact]
    public void Command_replies_are_the_envelope_and_eight_ready_flags()
    {
        Assert.Equal([0, 22], PvpRoomPayloadCodec.Encode(PvpRoomCommandRefusal.EnterRoom(22)));
        Assert.Equal(ServerMessageType.GameSetPvpTeamMode, PvpRoomCommandRefusal.TeamMode(8).MessageType);
        Assert.Equal([1, 1, 0, 1, 0, 0, 0, 0, 0],
            PvpRoomPayloadCodec.Encode(new EnterPvpRoomAccepted([true, false, true, false, false, false, false, false])));
    }

    [Fact]
    public void Room_notices_have_the_upstream_layouts()
    {
        Assert.Equal([0x34, 0x12, 1, 2, 3, 0, 1, 0, 0, 0, 0],
            PvpRoomPayloadCodec.Encode(new PvpRoomStateMessage(0x1234, 1, 2, 3, 1)));
        Assert.Equal([9, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0], PvpRoomPayloadCodec.Encode(PvpRoomStateMessage.Destroyed(9)));
        Assert.Equal([9, 0, 2, 2, 0, 1, 0x64, 0, 3, 0xFF, 0, 0],
            PvpRoomPayloadCodec.Encode(new PvpSeatStateMessage(9, 2, [
                (0, new PvpRoomSeatState(PvpRoomSeatOccupancy.Taken, 1, 100, true)),
                (3, new PvpRoomSeatState(PvpRoomSeatOccupancy.Open, 0, 0, false))])));
        Assert.Equal([5, 1], PvpRoomPayloadCodec.Encode(new PvpReadyStateMessage(5, true)));
        Assert.Equal([3, 2], PvpRoomPayloadCodec.Encode(new StartPvpMessage(3, 2)));
    }

    [Fact]
    public void A_room_notice_body_is_a_count_then_each_room_with_three_bytes_a_seat()
    {
        var room = PvpRoomLobbyView.Of(PvpRooms.Room(4, PvpBattleMode.Individual, 0,
            seats: [(0, PvpRooms.Taken(PvpRooms.Session(7), PvpTeam.Individual)), (2, PvpSeat.ClosedSeat)]));
        var body = PvpRoomPayloadCodec.Encode(new PvpRoomInfoMessage([room]));
        Assert.Equal([1, 0, 4, 0, 0, 2, 0, 0, 0, 0x41, 0x42, 1, 0, 0, 0, 1], body[..16]);
        Assert.Equal([0, 7, 0, 0xFF, 0, 0, 0xFE, 0, 0, 0xFF, 0, 0], body[16..28]);
        Assert.Equal(2 + 2 + 1 + 4 + 2 + 1 + 1 + 2 + 1 + 24 + 1 + 4, body.Length);
    }

    [Fact]
    public void A_room_invitation_notice_is_seven_bytes_with_request_type_two()
    {
        Assert.Equal([0x10, 0, 2, 0x78, 0x56, 0x34, 0x12],
            PvpRoomInviteRequestEncoder.EncodeBody(new PvpRoomInviteRequestMessage(0x10, 0x12345678)));
        Assert.Equal(ServerMessageType.GamePartyInviteRequest, new PvpRoomInviteRequestMessage(1, 0).MessageType);
    }

    [Fact]
    public void An_entry_roster_is_one_userinfo_zero_frame_counting_every_member()
    {
        var members = new[] { PvpRooms.Session(3), PvpRooms.Session(5) }
            .Select(x => Dfo.Server.Protocol.Game.PvpRoom.PvpRoomProjector.Identity(PvpRoomCommandTests.EntryFor(x))).ToArray();
        var payload = Assert.Single(new Dfo.Server.Protocol.Game.StateSynchronization.PvpParticipantRosterMessage(members).EncodePayloads());
        Assert.Equal((ushort)0x0002, payload.Type);
        Assert.Equal([0, 2, 0], payload.Body.Span[..3].ToArray());
    }

    [Fact]
    public void Channel_info_answers_the_upstream_success_or_ordinary_failure()
    {
        Assert.Equal([1, 0, 0, 0, 0, 0], PvpChannelInfoResponseEncoder.EncodeBody(new PvpChannelInfoResponse(true)));
        Assert.Equal([0, 0], PvpChannelInfoResponseEncoder.EncodeBody(new PvpChannelInfoResponse(false)));
    }

    [Fact]
    public void Every_room_refusal_has_a_wire_reason()
    {
        foreach (var refusal in Enum.GetValues<PvpRoomRefusal>())
        {
            _ = PvpRoomRefusalCode.Of(refusal);
            _ = PvpRoomRefusalCode.ForInvite(refusal);
            _ = PvpRoomRefusalCode.ForAcceptance(refusal);
        }
        Assert.Equal(19, PvpRoomRefusalCode.Of(PvpRoomRefusal.NotWaiting));
        Assert.Equal(22, PvpRoomRefusalCode.Of(PvpRoomRefusal.RoomMissing));
        Assert.Equal(22, PvpRoomRefusalCode.Of(PvpRoomRefusal.NotAllReady));
        Assert.Equal(6, PvpRoomRefusalCode.Of(PvpRoomRefusal.WrongPassword));
        Assert.Equal(4, PvpRoomRefusalCode.Of(PvpRoomRefusal.Full));
        Assert.Equal(8, PvpRoomRefusalCode.Of(PvpRoomRefusal.NotOwner));
        Assert.Equal(14, PvpRoomRefusalCode.Of(PvpRoomRefusal.UnbalancedTeams));
        Assert.Equal(19, PvpRoomRefusalCode.Of(PvpRoomRefusal.ShuttingDown));
        Assert.Equal(PartyRefusalCode.AlreadyInParty, PvpRoomRefusalCode.ForInvite(PvpRoomRefusal.TargetInParty));
        Assert.Equal(PartyRefusalCode.Refused, PvpRoomRefusalCode.ForInvite(PvpRoomRefusal.Blacklisted));
        Assert.Equal(PartyRefusalCode.Refused, PvpRoomRefusalCode.ForAcceptance(PvpRoomRefusal.InviteExpired));
        Assert.Equal(PartyRefusalCode.PartyFullOrEmptySlot, PvpRoomRefusalCode.ForAcceptance(PvpRoomRefusal.Full));
    }

    private static (int Seat, int State, bool WellFormed) Seat(byte[] body)
    {
        var request = PvpRoomPayloadCodec.DecodeSeatState(Meta, body);
        return (request.Seat, request.State, request.WellFormed);
    }
}
