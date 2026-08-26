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
        Seed: 0x0BADF00D,
        Rooms: new Dictionary<MazeCell, int>
        {
            [new MazeCell(0, 1)] = 61000,
            [new MazeCell(1, 1)] = 61001,
            [new MazeCell(3, 0)] = 61006,
        });

    /// <summary>The run as it stands on entry: in the start room, having seen only it.</summary>
    private static readonly DungeonRunState AtStart = new(
        Tutorial,
        Tutorial.StartCell,
        Tutorial.StartRoomMapId,
        new HashSet<MazeCell> { Tutorial.StartCell },
        new HashSet<GroundDropId>());

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
    /// Thirty-two bytes on A21, not thirty and not the six the generated field table records --
    /// that count is where static analysis stopped, inside an indirect call in the client's
    /// handler. The dungeon id is an <c>i32</c>, which is the whole of the two-byte difference and
    /// is what puts the trailing sentinel at 18 instead of 16.
    /// </remarks>
    [Fact]
    public void Dungeon_info_carries_the_run_shape_in_the_A21_builders_order()
    {
        var body = DungeonEntryPayloadCodec.EncodeDungeonInfo(Tutorial, difficulty: 0);

        Assert.Equal(32, body.Length);
        Assert.Equal(10000, BinaryPrimitives.ReadInt32LittleEndian(body));
        Assert.Equal(0, body[4]);           // difficulty
        Assert.Equal(0, body[5]);           // maze index
        Assert.Equal(3, body[6]);           // boss cell x
        Assert.Equal(0, body[7]);           // boss cell y
        Assert.Equal(0, body[11]);          // minimap group count
        Assert.Equal(1, body[12]);          // the constant after the group list
        Assert.Equal(
            unchecked((int)0xFFFFFFFF),
            BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(18)));
    }

    /// <remarks>
    /// The two bytes that used to carry an absent-abyss sentinel of 0xFF/0xFF are a hell-party
    /// marker and a hell-party enabled flag on A21, and both are zero when neither applies. The
    /// older reading was the A12 builder's; A21's writes zeros, and its reference session's frame
    /// has them zero.
    /// </remarks>
    [Fact]
    public void A_run_with_no_hell_party_leaves_both_of_its_bytes_clear()
    {
        var body = DungeonEntryPayloadCodec.EncodeDungeonInfo(Tutorial, difficulty: 0);

        Assert.Equal(0, body[8]);
        Assert.Equal(0, body[9]);
    }

    /// <summary>
    /// A room's actors reach the client, twenty-one bytes each, numbered from the run's base.
    /// </summary>
    /// <remarks>
    /// This frame carried a count of zero until 2026-08-26 whatever the room declared. The session
    /// it had been checked against ran a maze whose first rooms hold no monsters, so an empty list
    /// matched it byte for byte and the gap stayed invisible. A client told a room holds nothing
    /// draws the monsters from its own copy of the map and then treats them as scenery.
    /// </remarks>
    [Fact]
    public void Start_map_carries_the_rooms_actors_numbered_from_the_runs_base()
    {
        DungeonRoomActor[] actors =
        [
            new DungeonRoomActor(61670, 1, 0),
            new DungeonRoomActor(461, 80, 5),
        ];

        var body = DungeonEntryPayloadCodec.EncodeStartMap(
            AtStart, firstEntry: true, actors, FirstSequence, NoDrops);

        Assert.Equal(23 + (21 * actors.Length), body.Length);
        Assert.Equal(actors.Length, body[18]);
        for (var index = 0; index < actors.Length; index++)
        {
            var entry = body.AsSpan(19 + (index * 21));
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(entry)); // template order
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(entry[2..])); // packet index
            Assert.Equal(FirstSequence + index, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));
            Assert.Equal(actors[index].Code, BinaryPrimitives.ReadInt32LittleEndian(entry[8..]));
            Assert.Equal(actors[index].Level, entry[12]);
            Assert.Equal(actors[index].ActorType, entry[13]);
        }

        // The four bytes the frame has always ended with sit after the actors, not before them.
        Assert.Equal([0, 0, 0, 0xFF], body[^4..]);
    }

    /// <summary>
    /// A room's breakable objects reach the client, nineteen bytes each, after the actors.
    /// </summary>
    /// <remarks>
    /// The tutorial's second room holds one, and a client told the room yields nothing has nothing
    /// to break open: the run stops there with the room already cleared and the scripted pause it
    /// opened never resumed. Nineteen and not the forty-eight a kill's drop takes -- the two
    /// frames describe the same thing in different shapes.
    /// </remarks>
    [Fact]
    public void Start_map_carries_what_the_rooms_breakable_objects_hold()
    {
        var drops = AtStart.RemainingGroundDrops([new DungeonRoomDrop(0, 14400, 25)]);

        var body = DungeonEntryPayloadCodec.EncodeStartMap(
            AtStart, firstEntry: true, NoActors, FirstSequence, drops);

        Assert.Equal(23 + (19 * drops.Count), body.Length);
        Assert.Equal(drops.Count, body[19]);

        var entry = body.AsSpan(20);
        Assert.Equal(0, entry[0]); // object index
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(entry[1..])); // ground number
        Assert.Equal(14400u, BinaryPrimitives.ReadUInt32LittleEndian(entry[3..]));
        Assert.NotEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(entry[7..])); // instance
        Assert.Equal(25, BinaryPrimitives.ReadUInt16LittleEndian(entry[11..]));
        Assert.Equal([0, 0, 0, 0, 0, 0], entry[13..19].ToArray());
        Assert.Equal([0, 0, 0, 0xFF], body[^4..]);
    }

    /// <summary>
    /// Rooms next to each other in a dungeon do not get sequence bases next to each other.
    /// </summary>
    /// <remarks>
    /// A dungeon's rooms carry consecutive map identifiers. A base that only added the map to the
    /// seed put the tutorial's first room at 30946 and its second at 30945, so the second room's
    /// monsters arrived under numbers the player had just killed in the first -- and the client
    /// names an actor by that number. The bands have to be far enough apart that one room's
    /// actors cannot reach into the next.
    /// </remarks>
    [Fact]
    public void Neighbouring_rooms_do_not_get_neighbouring_actor_sequence_bases()
    {
        var bases = new List<int>();
        foreach (var mapId in new[] { 61000, 61001, 61002, 61005 })
        {
            bases.Add(DungeonEntryPayloadCodec.FirstActorSequence(
                AtStart with { CurrentRoomMapId = mapId }));
        }

        // Wider apart than any room's actor count could ever be. The tutorial's largest room
        // declares five.
        for (var left = 0; left < bases.Count; left++)
        {
            for (var right = left + 1; right < bases.Count; right++)
            {
                Assert.True(
                    Math.Abs(bases[left] - bases[right]) > 256,
                    $"rooms {left} and {right} share a band: {bases[left]} vs {bases[right]}");
            }
        }
    }

    /// <summary>The sequence base is the run's, and a re-send of the same room repeats it.</summary>
    /// <remarks>
    /// A21 draws this at random per room so that a re-entered room cannot collide with actor
    /// identities the client still holds. Deriving it from the run's seed keeps that separation
    /// without a counter to store, and makes the frame reproducible, which is what lets this be
    /// asserted at all.
    /// </remarks>
    [Fact]
    public void The_actor_sequence_base_is_stable_for_a_run_and_inside_the_clients_range()
    {
        var first = DungeonEntryPayloadCodec.FirstActorSequence(AtStart);

        Assert.Equal(first, DungeonEntryPayloadCodec.FirstActorSequence(AtStart));
        Assert.InRange(first, 1, 60000);
    }

    /// <summary>
    /// A piece already picked up is gone from the room without renumbering what is left.
    /// </summary>
    /// <remarks>
    /// What a room has on its floor is read back out of the archive every time the room is
    /// described, so the run has to remember what has been taken or a player could walk out of a
    /// room and back in and be handed the box again. The numbering is derived from the room's own
    /// declaration and not from what survives: a client that took the second of three has to keep
    /// naming the third by three.
    /// </remarks>
    [Fact]
    public void A_taken_piece_leaves_the_floor_without_renumbering_what_is_left()
    {
        DungeonRoomDrop[] declared =
        [
            new DungeonRoomDrop(0, 14400, 25),
            new DungeonRoomDrop(1, 3020, 0),
            new DungeonRoomDrop(2, 14401, 20),
        ];

        Assert.Equal([1, 2, 3], AtStart.RemainingGroundDrops(declared).Select(drop => drop.Number));

        var afterTaking = AtStart with
        {
            TakenGroundDrops = new HashSet<GroundDropId>
            {
                new GroundDropId(AtStart.CurrentCell, 2),
            },
        };

        Assert.Equal(
            [1, 3], afterTaking.RemainingGroundDrops(declared).Select(drop => drop.Number));
        Assert.Equal(14401, afterTaking.FindGroundDrop(declared, 3)!.Drop.ItemId);
        Assert.Null(afterTaking.FindGroundDrop(declared, 2));
    }

    /// <summary>
    /// The floor of one room is not the floor of another, even when both number from one.
    /// </summary>
    [Fact]
    public void Clearing_one_rooms_floor_leaves_the_next_rooms_alone()
    {
        DungeonRoomDrop[] declared = [new DungeonRoomDrop(0, 14400, 25)];
        var afterTaking = AtStart with
        {
            TakenGroundDrops = new HashSet<GroundDropId>
            {
                new GroundDropId(AtStart.CurrentCell, 1),
            },
        };

        Assert.Empty(afterTaking.RemainingGroundDrops(declared));

        var nextRoom = afterTaking with
        {
            CurrentCell = new MazeCell(1, 1),
            CurrentRoomMapId = 61001,
        };

        Assert.Single(nextRoom.RemainingGroundDrops(declared));
    }

    /// <summary>
    /// The value a dropped piece carries fits the signed column it is stored in.
    /// </summary>
    /// <remarks>
    /// The piece keeps this value once it is picked up -- a working A21 session's dropped shoulder
    /// and the one the player ends up wearing carry the same one -- so a value that only fits
    /// unsigned would reach the wire as one number and the database as another.
    /// </remarks>
    [Fact]
    public void The_drop_instance_value_fits_a_signed_column_and_is_never_zero()
    {
        foreach (var mapId in new[] { 61000, 61001, 61002, 61005, 61032 })
        {
            for (byte objectIndex = 0; objectIndex < 4; objectIndex++)
            {
                var instance = DungeonEntryPayloadCodec.DropInstance(
                    AtStart with { CurrentRoomMapId = mapId }, objectIndex);

                Assert.InRange(instance, 1u, (uint)int.MaxValue);
            }
        }
    }

    /// <summary>A room the archive declares nothing in.</summary>
    private static readonly DungeonRoomActor[] NoActors = [];

    /// <summary>A room with nothing breakable in it.</summary>
    private static readonly GroundDrop[] NoDrops = [];

    /// <summary>An arbitrary sequence base; these tests are about the fields around it.</summary>
    private const ushort FirstSequence = 75;

    [Fact]
    public void Start_map_opens_the_run_in_the_layouts_start_cell()
    {
        var body = DungeonEntryPayloadCodec.EncodeStartMap(AtStart, firstEntry: true, NoActors, FirstSequence, NoDrops);

        Assert.Equal(23, body.Length);
        Assert.Equal(0, body[0]);
        Assert.Equal(1, body[1]);
        Assert.Equal(0, body[2]);
        // A21 defaults this to two. It was read as an abyss-mode flag and written as zero; the
        // reference session carries two on both of its START_MAP frames.
        Assert.Equal(2, body[7]);
    }

    /// <remarks>
    /// The map, not the maze. The reference server names this field Index on a per-room record,
    /// which reads like a maze number; it is the room's map identifier, and sending the maze
    /// index instead made the client load map 0 -- a black screen, then a crash (2026-08-18).
    /// </remarks>
    [Fact]
    public void Start_map_names_the_room_to_load_and_not_the_maze_it_belongs_to()
    {
        var body = DungeonEntryPayloadCodec.EncodeStartMap(AtStart, firstEntry: true, NoActors, FirstSequence, NoDrops);

        // An i32 on A21, which is the other half of this frame's two-byte growth.
        Assert.Equal(61000, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(14)));
        Assert.NotEqual(Tutorial.MazeIndex, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(14)));
    }

    /// <remarks>
    /// The seed is on the wire because the client draws on it. A frame that carried a different
    /// seed than the layout was derived from would leave the two sides disagreeing about
    /// everything downstream of it.
    /// </remarks>
    [Fact]
    public void Start_map_carries_the_layouts_own_seed()
    {
        var body = DungeonEntryPayloadCodec.EncodeStartMap(AtStart, firstEntry: true, NoActors, FirstSequence, NoDrops);

        Assert.Equal(0x0BADF00D, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(3)));
    }

    /// <remarks>
    /// A first entry, and a run of one. The reference builder distinguishes a first entry from a
    /// revisit by this flag and defaults the party index to 0xFF.
    /// </remarks>
    [Fact]
    public void Start_map_marks_a_first_entry_rather_than_a_revisit()
    {
        var body = DungeonEntryPayloadCodec.EncodeStartMap(AtStart, firstEntry: true, NoActors, FirstSequence, NoDrops);

        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9)));
        Assert.Equal(1, body[13]);
        Assert.Equal(0xFF, body[22]);
    }

    /// <remarks>
    /// The empty room is this slice's deliberate limit, not an oversight: it is the cheapest frame
    /// that can answer whether the client accepts this shape at all.
    /// </remarks>
    [Fact]
    public void Start_map_declares_an_empty_room()
    {
        var body = DungeonEntryPayloadCodec.EncodeStartMap(AtStart, firstEntry: true, NoActors, FirstSequence, NoDrops);

        Assert.Equal(0, body[16]);
        Assert.Equal(0, body[17]);
        Assert.Equal(0, body[18]);
        Assert.Equal(0, body[19]);
    }

    /// <remarks>
    /// A room change is the same frame as an entry, differing in this flag alone. The reference
    /// server has a separate revisit builder whose only difference for an ordinary room is that it
    /// writes zero here.
    /// </remarks>
    [Fact]
    public void A_revisited_room_is_marked_as_one()
    {
        var revisit = AtStart with
        {
            CurrentCell = new MazeCell(1, 1),
            CurrentRoomMapId = 61001,
        };

        var body = DungeonEntryPayloadCodec.EncodeStartMap(revisit, firstEntry: false, NoActors, FirstSequence, NoDrops);

        Assert.Equal(0, body[13]);
        Assert.Equal(1, body[0]);
        Assert.Equal(1, body[1]);
        Assert.Equal(61001, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(14)));
    }

    /// <remarks>
    /// The captured body, first ten bytes: the client asked for cell (1,1), which the tutorial's
    /// maze declares as room 61001 -- the room to the right of the one it was standing in. The
    /// request and the archive agree, which is what makes these two offsets more than transcription.
    /// </remarks>
    [Fact]
    public void The_captured_move_names_the_cell_the_maze_puts_next_door()
    {
        byte[] body =
        [
            0x01, 0x01, 0xC3, 0x02, 0x00, 0x00, 0x14, 0x01, 0x00, 0x00,
            0x00, 0x11, 0x40, 0x0F, 0x00, 0x00,
        ];

        var request = (MoveMapRequest)new MoveMapDecoder()
            .Decode(GameMessageMetadata.Empty, body)!;

        Assert.Equal(1, request.NextX);
        Assert.Equal(1, request.NextY);
        Assert.Equal(707u, request.PathPositionX);
        Assert.Equal(276u, request.PathPositionY);
        Assert.Equal(61001, Tutorial.Rooms[new MazeCell(request.NextX, request.NextY)]);
    }

    [Fact]
    public void A_move_body_shorter_than_its_evidenced_fields_is_refused()
    {
        Assert.Throws<InvalidDataException>(() =>
            new MoveMapDecoder().Decode(GameMessageMetadata.Empty, new byte[9]));
    }

    [Fact]
    public void The_support_roster_is_a_count_of_zero()
    {
        Assert.Equal([0x00, 0x00], DungeonEntryPayloadCodec.EncodeSupportTag());
    }
}
