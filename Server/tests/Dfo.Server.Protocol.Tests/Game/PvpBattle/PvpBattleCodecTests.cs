using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.PvpBattle;

namespace Dfo.Server.Protocol.Tests.Game.PvpBattle;

/// <summary>
/// The battle reports' and notices' bytes (design-r1 §5, research.md §2, upstream <c>Parsers/Pvp/*</c>,
/// <c>BuildEndPvpBody</c>, <c>PvpRecordBodyBuilder</c>); Spec RQ-075 / AC-075 for the report lengths.
/// </summary>
public sealed class PvpBattleCodecTests
{
    private static readonly GameMessageMetadata Meta = GameMessageMetadata.Empty;

    [Fact]
    public void A_death_is_exactly_victim_killer_and_flag()
    {
        var death = PvpBattlePayloadCodec.DecodeDeath(Meta, [0x02, 0x01, 0x04, 0x03, 0x01]);
        Assert.True(death.WellFormed);
        Assert.Equal((0x0102, 0x0304, 1), (death.Victim, death.Killer, death.Flag));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    public void A_death_of_any_other_length_is_malformed(int length) =>
        Assert.False(PvpBattlePayloadCodec.DecodeDeath(Meta, new byte[length]).WellFormed);

    [Theory]
    [InlineData(32, true)]
    [InlineData(31, false)]
    [InlineData(33, false)]
    [InlineData(0, false)]
    public void A_time_out_is_exactly_thirty_two_bytes(int length, bool wellFormed)
    {
        var body = Enumerable.Range(0, length).Select(x => (byte)x).ToArray();
        var timeOut = PvpBattlePayloadCodec.DecodeTimeOut(Meta, body);
        Assert.Equal(wellFormed, timeOut.WellFormed);
        Assert.Equal(body, timeOut.Values.ToArray());
    }

    [Fact]
    public void A_peer_link_report_lists_seat_and_status_pairs()
    {
        var linked = PvpBattlePayloadCodec.DecodeConnectP2p(Meta, [2, 0, 1, 3, 0]);
        Assert.True(linked.WellFormed);
        Assert.Equal([((byte)0, (byte)1), ((byte)3, (byte)0)], linked.Peers.ToArray());
        Assert.True(PvpBattlePayloadCodec.DecodeConnectP2p(Meta, [0]).WellFormed);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 2, 0, 1, 3 })] // one pair short
    [InlineData(new byte[] { 1, 0, 1, 0 })] // trailing byte
    [InlineData(new byte[] { 1, 8, 1 })] // seat out of range
    [InlineData(new byte[] { 9, 0, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 0, 0 })] // more than eight seats
    public void A_malformed_peer_link_report_is_marked(byte[] body) =>
        Assert.False(PvpBattlePayloadCodec.DecodeConnectP2p(Meta, body).WellFormed);

    [Fact]
    public void A_death_notice_is_two_seats_and_nobody_is_0xFF()
    {
        Assert.Equal(new byte[] { 3, 1 }, PvpBattlePayloadCodec.Encode(new PvpDeathMessage(3, 1)));
        Assert.Equal(new byte[] { 3, 0xFF }, PvpBattlePayloadCodec.Encode(new PvpDeathMessage(3, null)));
    }

    [Fact]
    public void A_two_fighter_result_is_its_byte_baseline()
    {
        var body = PvpBattlePayloadCodec.Encode(new EndPvpMessage(0, 1, 100,
            [new EndPvpFighter(0x0102, 1, 1, 1100), new EndPvpFighter(0x0304, 0, 0, 950)], false));

        Assert.Equal(26 + 19 * 2, body.Length);
        Assert.Equal(Convert.FromHexString(
            "00" + "00000000" + "01" + "64000000" + "02"
            + "0201" + "01000000" + "01" + "4C040000" + "00000000" + "00000000"
            + "0403" + "00000000" + "00" + "B6030000" + "00000000" + "00000000"
            + "FFFF" + "FFFF" + "00000000" + "00" + "FF" + "00000000" + "00"), body);
    }

    [Fact]
    public void A_four_fighter_draw_is_twenty_six_plus_nineteen_each()
    {
        EndPvpFighter[] fighters = [.. Enumerable.Range(0, 4).Select(x => new EndPvpFighter((ushort)(x + 1), x, (byte)x, -5))];
        var body = PvpBattlePayloadCodec.Encode(new EndPvpMessage(0xFF, 2, -50, fighters, true));

        Assert.Equal(26 + 19 * 4, body.Length);
        Assert.Equal(0xFF, body[0]);
        Assert.Equal(4, body[10]);
        // The fourth fighter's entry, then the tail with the draw flag set.
        Assert.Equal(Convert.FromHexString("0400" + "03000000" + "03" + "FBFFFFFF" + "00000000" + "00000000"), body[(11 + 19 * 3)..(11 + 19 * 4)]);
        Assert.Equal(Convert.FromHexString("FFFF" + "FFFF" + "00000000" + "01" + "FF" + "00000000" + "00"), body[^15..]);
    }

    [Fact]
    public void A_record_is_its_fifty_one_byte_baseline()
    {
        var body = PvpBattlePayloadCodec.Encode(new PvpRecordMessage(new PvpRecordView(3, 2, 1500, 1000, 2000, 7, 4)));

        Assert.Equal(51, body.Length);
        Assert.Equal(Convert.FromHexString(
            "07000000" + "04000000" + "DC050000" + "E8030000" + "D0070000" + "03" + "02"
            + "FFFFFFFF" + "00000000" + "00000000" + "00000000" + "00" + "00000000" + "00000000" + "00000000"), body);
    }

    [Fact]
    public void A_top_grade_sentinel_beyond_the_wire_is_clamped()
    {
        var body = PvpBattlePayloadCodec.Encode(new PvpRecordMessage(new PvpRecordView(9, 0, 5000, 4000, long.MaxValue, 0, 0)));
        Assert.Equal(Convert.FromHexString("FFFFFF7F"), body[16..20]);
    }
}
