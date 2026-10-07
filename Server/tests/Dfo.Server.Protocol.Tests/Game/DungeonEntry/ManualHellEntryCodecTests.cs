using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

public sealed class ManualHellEntryCodecTests
{
    private static readonly HellEntryPlan Plan = new(1, new(1, 0), 900,
        [new(2, 10, new(461, 80, 5)), new(1, 11, new(61670, 85, 0)), new(3, 12, new(461, 80, 5))]);
    private static readonly DungeonLayout Layout = new(150, 0, 2, 1, new(0, 0), new(1, 0), 800, 42,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(800), [new(1, 0)] = DungeonCellDefinition.Fixed(801) })
        { EntryKind = DungeonEntryKind.ManualHell, Hell = Plan };
    private static DungeonRunState In(MazeCell cell) => new(Layout, cell, cell == Plan.Cell ? Plan.MapId : 800,
        new HashSet<MazeCell> { cell }, DateTimeOffset.UnixEpoch,
        CurrentCellState: DungeonCellSeed.CompleteEmpty(cell, cell == Plan.Cell ? Plan.MapId : 800).Snapshot());

    [Fact]
    public void Decoder_keeps_manual_mode_and_practice_as_independent_bytes()
    {
        var body = Convert.FromHexString("96000000000101FFFF000000000000");
        var request = Assert.IsType<SelectDungeonRequest>(new SelectDungeonDecoder().Decode(Dfo.Server.Data.Networking.GameMessageMetadata.Empty, body));
        Assert.Equal((byte)1, request.RequestMode);
        Assert.Equal((byte)1, request.PracticeMode);
        Assert.Equal(0, (byte)request.Difficulty);
    }

    [Fact]
    public void Frozen_plan_sets_hell_coordinates_and_difficulty_without_changing_normal_dungeon_difficulty()
    {
        var info = DungeonEntryPayloadCodec.EncodeDungeonInfo(Layout, (Dfo.Server.Data.World.DungeonDifficulty)3);
        Assert.Equal((byte)3, info[4]);
        Assert.Equal(new byte[] { 1, 0, 0 }, info[8..11]);
        var actors = Plan.Actors.Select(x => new RoomActorDescription(x.Actor, x.Actor.ActorType >= 5)
            { IsHellTemplate = true, HellOrder = x.Order }).ToArray();
        var room = DungeonEntryPayloadCodec.EncodeStartMap(In(Plan.Cell), true, actors, 100, []);
        Assert.Equal((byte)1, room[7]);
        Assert.Equal(900, BinaryPrimitives.ReadInt32LittleEndian(room.AsSpan(14)));
        Assert.Equal((byte)3, room[18]);
        for (var i = 0; i < actors.Length; i++)
        {
            var actor = room.AsSpan(19 + i * 21, 21);
            Assert.Equal(actors[i].HellOrder, BinaryPrimitives.ReadUInt16LittleEndian(actor));
            Assert.Equal(100 + i, BinaryPrimitives.ReadUInt16LittleEndian(actor[6..]));
            Assert.Equal(new byte[] { 1, 255 }, actor[14..16].ToArray());
        }
        Assert.Equal((byte)1, room[^3]);
    }

    [Fact]
    public void Resource_table_only_describes_APC_codes_in_hell_room_and_encodes_u32_pairs()
    {
        Assert.Null(HellPartyProjection.For(In(Layout.StartCell)));
        var info = HellPartyProjection.For(In(Plan.Cell));
        Assert.NotNull(info);
        Assert.Equal(new[] { (461, 80) }, info.Actors);
        var frame = new HellPartyMonsterInfoEncoder().Encode(info).Single().ToArray();
        Assert.Equal(0x02A7, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(Convert.FromHexString("01000000CD01000050000000"), frame[15..]);
        var revisit = DungeonEntryPayloadCodec.EncodeStartMap(In(Plan.Cell), false, [], 100, []);
        Assert.Equal(0, revisit[13]);
        Assert.Equal(0, revisit[18]);
    }
}
