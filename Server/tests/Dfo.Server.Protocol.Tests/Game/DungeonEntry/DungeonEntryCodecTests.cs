using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DungeonEntry;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

/// <summary>
/// The dungeon-entry request and the three frames answering it.
/// </summary>
/// <remarks>
/// Every field of the three outbound frames is transcribed from the reference server; the client's
/// own handler yields only "at least one byte after the envelope", so nothing here can be checked
/// against the client until a live session does it. These tests therefore pin the bytes rather
/// than assert meanings: they exist so that when the live answer arrives, a change made in
/// response to it is a visible change and not a silent one.
/// </remarks>
public sealed class DungeonEntryCodecTests
{
    /// <summary>The tutorial's layout, as the rule resolves it from the archive.</summary>
    private static readonly DungeonLayout Tutorial = new(
        DungeonId: 10000,
        MazeIndex: 0,
        Width: 5,
        Height: 3,
        StartCell: new MazeCell(0, 1),
        BossCell: new MazeCell(3, 0),
        StartRoomMapId: 61000,
        Seed: 0x0BADF00D);

    /// <remarks>
    /// The captured body, byte for byte: <c>102700000000000000</c>, identical across all eight
    /// sessions that sent it. 10000 is Tutorial/Tutorial.dgn.
    /// </remarks>
    [Fact]
    public void The_captured_request_decodes_as_the_tutorial_dungeon_at_difficulty_zero()
    {
        byte[] body = [0x10, 0x27, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        var request = (SelectDungeonRequest)new SelectDungeonDecoder()
            .Decode(GameMessageMetadata.Empty, body)!;

        Assert.Equal(10000, request.DungeonId);
        Assert.Equal(0, request.Difficulty);
        Assert.Equal(0, request.HellPartyRequested);
        Assert.Equal(0, request.HellPartyDifficulty);
    }

    [Fact]
    public void A_body_shorter_than_its_evidenced_fields_is_refused()
    {
        Assert.Throws<InvalidDataException>(() =>
            new SelectDungeonDecoder().Decode(GameMessageMetadata.Empty, new byte[4]));
    }

    /// <remarks>
    /// Thirty bytes, not the six the generated field table records. That table's count is the
    /// lower bound static analysis reached before the client's handler entered an indirect call;
    /// the reference builder writes eleven more fields after it.
    /// </remarks>
    [Fact]
    public void Dungeon_info_carries_the_run_shape_in_the_reference_builders_order()
    {
        var body = DungeonEntryPayloadCodec.EncodeDungeonInfo(Tutorial, difficulty: 0);

        Assert.Equal(30, body.Length);
        Assert.Equal(10000, BinaryPrimitives.ReadInt16LittleEndian(body));
        Assert.Equal(0, body[2]);
        Assert.Equal(0, body[3]);
        Assert.Equal(3, body[4]);
        Assert.Equal(0, body[5]);
        Assert.Equal(0, body[8]);
        Assert.Equal(0, body[9]);
        Assert.Equal(
            unchecked((int)0xFFFFFFFF),
            BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(16)));
    }

    /// <remarks>
    /// The absent abyss room is marked rather than pointed at cell (0,0), which an ordinary maze
    /// can occupy -- the tutorial's own start cell is (0,1) and its grid includes (0,0).
    /// </remarks>
    [Fact]
    public void A_run_with_no_abyss_room_marks_its_absence_rather_than_naming_a_real_cell()
    {
        var body = DungeonEntryPayloadCodec.EncodeDungeonInfo(Tutorial, difficulty: 0);

        Assert.Equal(0xFF, body[6]);
        Assert.Equal(0xFF, body[7]);
    }

    [Fact]
    public void Start_map_opens_the_run_in_the_layouts_start_cell()
    {
        var body = DungeonEntryPayloadCodec.EncodeStartMap(Tutorial);

        Assert.Equal(21, body.Length);
        Assert.Equal(0, body[0]);
        Assert.Equal(1, body[1]);
        Assert.Equal(0, body[2]);
        Assert.Equal(0, body[7]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(14)));
    }

    /// <remarks>
    /// The seed is on the wire because the client draws on it. A frame that carried a different
    /// seed than the layout was derived from would leave the two sides disagreeing about
    /// everything downstream of it.
    /// </remarks>
    [Fact]
    public void Start_map_carries_the_layouts_own_seed()
    {
        var body = DungeonEntryPayloadCodec.EncodeStartMap(Tutorial);

        Assert.Equal(0x0BADF00D, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(3)));
    }

    /// <remarks>
    /// A first entry, and a run of one. The reference builder distinguishes a first entry from a
    /// revisit by this flag and defaults the party index to 0xFF.
    /// </remarks>
    [Fact]
    public void Start_map_marks_a_first_entry_rather_than_a_revisit()
    {
        var body = DungeonEntryPayloadCodec.EncodeStartMap(Tutorial);

        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9)));
        Assert.Equal(1, body[13]);
        Assert.Equal(0xFF, body[20]);
    }

    /// <remarks>
    /// The empty room is this slice's deliberate limit, not an oversight: it is the cheapest frame
    /// that can answer whether the client accepts this shape at all.
    /// </remarks>
    [Fact]
    public void Start_map_declares_an_empty_room()
    {
        var body = DungeonEntryPayloadCodec.EncodeStartMap(Tutorial);

        Assert.Equal(0, body[16]);
        Assert.Equal(0, body[17]);
        Assert.Equal(0, body[18]);
        Assert.Equal(0, body[19]);
    }

    [Fact]
    public void The_support_roster_is_a_count_of_zero()
    {
        Assert.Equal([0x00, 0x00], DungeonEntryPayloadCodec.EncodeSupportTag());
    }
}
