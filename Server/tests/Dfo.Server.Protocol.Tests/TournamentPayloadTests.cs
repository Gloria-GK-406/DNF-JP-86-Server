using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Tournament;

namespace Dfo.Server.Protocol.Tests;

public sealed class TournamentPayloadTests
{
    // A21 D56690 reads four bracket groups, then four actor groups. These offsets come
    // from the client's nested counts (16/8/4/2, partyLimit=1), not the codec's loops.
    [Fact]
    public void Bracket_has_u32_dungeon_header_and_four_separate_path_actor_records()
    {
        var body = TournamentPayloadCodec.Info(Plan());

        Assert.Equal(260, body.Length);
        Assert.Equal(Convert.FromHexString("780000000201011000000000000000"), body[..15]);
        Assert.Equal(new byte[] { 2, 8 }, body[120..122]);
        Assert.Equal(new byte[] { 3, 4 }, body[178..180]);
        Assert.Equal(new byte[] { 4, 2 }, body[208..210]);
        Assert.Equal(Convert.FromHexString(
            "012211040302015505024433443322115405036655887766555305048877010101015208"),
            body[224..]);
    }

    // D5AB20 consumes the full map body only for visitKind 1/2. Empty actors/P2P
    // still need their count bytes and the final flag: the solo packet is 15 bytes.
    [Fact]
    public void First_map_contains_map_id_and_three_distinct_empty_tail_fields()
    {
        var body = TournamentPayloadCodec.Map(Plan());

        Assert.Equal(Convert.FromHexString("0000674523710001CC420000000000"), body);
        Assert.Equal(17100, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(8)));
    }

    [Fact]
    public void Elimination_rewards_keep_empty_actor_prefix_and_disabled_champion_row()
    {
        var state = State(2, TournamentStatus.Eliminated) with
        {
            Rewards = [[new(3329, 3), new(0, 3000)], []],
        };

        // rounds=2, exp=50000, firstCount=0, two upper cards, lowerCount=0.
        Assert.Equal(Convert.FromHexString(
            "0250C300000002010D000003000000000000000000B80B0000000000"),
            TournamentPayloadCodec.Reward(state));
    }

    [Fact]
    public void Champion_rewards_preserve_signed_empty_card_and_two_card_rows()
    {
        var state = State(4, TournamentStatus.Champion) with
        {
            Rewards = [[new(-1, 0), new(3329, 1)], [new(0, 1200), new(3329, 2)]],
        };

        Assert.Equal(Convert.FromHexString(
            "04400D03000002FFFFFFFF000000000000010D00000100000000000200000000B00400000000010D0000020000000000"),
            TournamentPayloadCodec.Reward(state));
    }

    [Fact]
    public void Reward_status_reserves_eight_party_slots_after_success_byte()
    {
        Assert.Equal(Convert.FromHexString("0101FFFFFF01FFFFFF"),
            TournamentPayloadCodec.Status(new(true, 0)));
        Assert.Equal(9, TournamentPayloadCodec.Status(new(false, 0)).Length);
        Assert.Equal(Convert.FromHexString("0101FFFFFFFFFFFFFF"),
            TournamentPayloadCodec.Status(new(true, 0, SecondRow: false)));
    }

    [Fact]
    public void Selection_returns_winners_by_card_and_not_selected_as_ff()
    {
        var state = State(4, TournamentStatus.Champion) with
        {
            Rewards = [[new(3329, 1), new(3329, 2)], [new(0, 10), new(0, 20)]],
            Selections = [1, 0],
        };

        Assert.Equal(Convert.FromHexString("0102FF000200FF"),
            TournamentPayloadCodec.Selection(new(state, 0)));
        Assert.Equal(new byte[] { 0, 1 }, TournamentPayloadCodec.Selection(new(null, 0)));
    }

    [Fact]
    public void Decoders_accept_only_native_request_shapes()
    {
        var meta = GameMessageMetadata.Empty;
        Assert.IsType<TournamentRewardRequest>(new TournamentStatusDecoder().Decode(meta, []));
        Assert.Null(new TournamentStatusDecoder().Decode(meta, [0]));
        var request = Assert.IsType<TournamentRewardRequest>(new TournamentSelectionDecoder().Decode(meta, [1, 0]));
        Assert.Equal((byte?)1, request.Row);
        Assert.Equal((byte?)0, request.Index);
        Assert.Null(new TournamentSelectionDecoder().Decode(meta, []));
        Assert.Null(new TournamentSelectionDecoder().Decode(meta, [0]));
        Assert.Null(new TournamentSelectionDecoder().Decode(meta, [0, 0, 0]));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    [InlineData(255, 255)]
    public void Selection_decoder_preserves_invalid_coordinates_for_explicit_domain_refusal(byte row, byte index)
    {
        var request = Assert.IsType<TournamentRewardRequest>(
            new TournamentSelectionDecoder().Decode(GameMessageMetadata.Empty, [row, index]));
        Assert.Equal((byte?)row, request.Row);
        Assert.Equal((byte?)index, request.Index);
    }

    private static TournamentDungeonState State(int rounds, TournamentStatus status) =>
        new(DungeonRunId.New(), new(2), Plan(), rounds, status);

    private static TournamentPlan Plan()
    {
        var definition = new TournamentDefinition(120, 17100, 69, 70, 2, 2,
            [], [], [0, 30000, 50000, 100000, 200000], [], [], 1);
        TournamentBracketRound Round(byte number, int count) => new(number,
            Enumerable.Range(0, count).Select(position => new TournamentTeam((byte)position,
                position == 0 ? 0 : 1000 + position, position == 0 ? 0 : 300)).ToArray());
        return new(definition, [Round(1, 16), Round(2, 8), Round(3, 4), Round(4, 2)],
            [new(0x1122, 0x01020304, 85, 5, true), new(0x3344, 0x11223344, 84, 5, true),
             new(0x5566, 0x55667788, 83, 5, true), new(0x7788, 0x01010101, 82, 8, true)],
            0xF1234567);
    }
}
