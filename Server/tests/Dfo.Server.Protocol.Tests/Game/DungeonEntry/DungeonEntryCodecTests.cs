using System.Buffers.Binary;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
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
    [Theory]
    [InlineData(5)]
    [InlineData(255)]
    public void Undefined_difficulty_is_rejected_before_business(int value)
    {
        var body = Convert.FromHexString("96000000000000FFFF000000000000");
        body[4] = (byte)value;
        Assert.Throws<InvalidDataException>(() => new SelectDungeonDecoder().Decode(GameMessageMetadata.Empty, body));
    }

    [Fact]
    public void Permission_entries_preserve_u32_map_ids_and_five_byte_stride()
    {
        var body = Dfo.Server.Protocol.Game.StateSynchronization.StateSynchronizationPayloadCodec.EncodeDungeonPermissions(
            [new(150, 1), new(65686, 2)]);
        Assert.Equal(Convert.FromHexString("020096000000019600010002"), body);
    }

    [Fact]
    public void A21_adventure_request_preserves_u32_id_and_offset_four_difficulty()
    {
        var body = Convert.FromHexString("96000100010000FFFF000000000000");
        var request = Assert.IsType<SelectDungeonRequest>(new SelectDungeonDecoder()
            .Decode(GameMessageMetadata.Empty, body));
        Assert.Equal(65686, request.DungeonId);
        Assert.Equal(1, (byte)request.Difficulty);
    }

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
        Cells: new Dictionary<MazeCell, DungeonCellDefinition> {
            [new MazeCell(0, 1)] = DungeonCellDefinition.Fixed(61000),
            [new MazeCell(1, 1)] = DungeonCellDefinition.Fixed(61001),
            [new MazeCell(3, 0)] = DungeonCellDefinition.Fixed(61006),
        });

    /// <summary>The run as it stands on entry: in the start room, having seen only it.</summary>
    private static readonly DungeonRunState AtStart = new(
        Tutorial,
        Tutorial.StartCell,
        Tutorial.StartRoomMapId,
        new HashSet<MazeCell> { Tutorial.StartCell },
        DateTimeOffset.UnixEpoch,
        CurrentCellState: DungeonCellSeed.CompleteEmpty(
            Tutorial.StartCell, Tutorial.StartRoomMapId).Snapshot());

    /// <remarks>
    /// #101 T02: the ordinary spider entrance, captured as a 15-byte request.
    /// </remarks>
    [Fact]
    public void Captured_spider_normal_request_decodes_at_difficulty_zero()
    {
        byte[] body = Convert.FromHexString("96000000000000FFFF000000000000");

        var request = (SelectDungeonRequest)new SelectDungeonDecoder()
            .Decode(GameMessageMetadata.Empty, body)!;

        Assert.Equal(150, request.DungeonId);
        Assert.Equal(Dfo.Server.Data.World.DungeonDifficulty.Normal, request.Difficulty);
        Assert.Equal(0, request.RequestMode);
        Assert.Equal(0, request.PracticeMode);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void A_body_shorter_than_its_evidenced_fields_is_refused(int length)
    {
        Assert.Throws<InvalidDataException>(() =>
            new SelectDungeonDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));
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
    /// Ordinary A21 vectors preserve zero/zero at offsets 8/9. Active-hell coordinates follow
    /// upstream and are covered separately; native A21 reads two independent bytes.
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
            AtStart, firstEntry: true, Rooms.Describe(actors), FirstSequence, NoDrops);

        Assert.Equal(23 + (21 * actors.Length), body.Length);
        Assert.Equal(actors.Length, body[18]);
        for (var index = 0; index < actors.Length; index++)
        {
            var entry = body.AsSpan(19 + (index * 21));
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(entry)); // template order
            Assert.Equal(FirstSequence + index, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));
            Assert.Equal(actors[index].Code, BinaryPrimitives.ReadInt32LittleEndian(entry[8..]));
            Assert.Equal(actors[index].Level, entry[12]);
            Assert.Equal(actors[index].ActorType, entry[13]);
        }

        // A monster and an AI character are each the first of their own kind.
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(21)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(42)));

        // The four bytes the frame has always ended with sit after the actors, not before them.
        Assert.Equal([0, 0, 0, 0xFF], body[^4..]);
    }

    /// <summary>
    /// Each actor is numbered within its own kind, and the two kinds are numbered apart.
    /// </summary>
    /// <remarks>
    /// Every actor of every room carried zero here until 2026-08-27, which the tutorial's first
    /// room could not show: it holds one monster and one AI character, and those are the first of
    /// their two kinds. The second room holds four, and a client given four actors that all claim
    /// the same place keeps one of them. What the player saw was a room whose two monsters were
    /// missing and whose shoulder piece was already on the floor -- the client reveals a declared
    /// drop on its own script, and the step it waits on had nothing left to wait for.
    /// </remarks>
    [Fact]
    public void Actors_are_numbered_within_their_own_kind()
    {
        DungeonRoomActor[] actors =
        [
            new DungeonRoomActor(61670, 1, 0),
            new DungeonRoomActor(61359, 1, 0),
            new DungeonRoomActor(461, 80, 5),
            new DungeonRoomActor(61670, 1, 0),
            new DungeonRoomActor(462, 80, 5),
        ];

        var body = DungeonEntryPayloadCodec.EncodeStartMap(
            AtStart, firstEntry: true, Rooms.Describe(actors), FirstSequence, NoDrops);

        int[] expected = [0, 1, 0, 2, 1];
        for (var index = 0; index < actors.Length; index++)
        {
            Assert.Equal(
                expected[index],
                BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(19 + (index * 21) + 2)));
        }
    }

    /// <summary>
    /// The tutorial's second room, byte for byte against the frame the reference server sent.
    /// </summary>
    /// <remarks>
    /// Captured from the reference server on 2026-08-27, running the same client against the same
    /// archive: room (1,1) of dungeon 10000, map 61001, four actors and one breakable object
    /// holding a shoulder piece. The seed, the sequence base and the drop's instance value are
    /// each side's own and are substituted here; everything else must match.
    /// <para>
    /// This room is the first one that can tell two numberings apart. The room before it holds one
    /// monster and one AI character, which agree under any of them.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_reference_servers_second_tutorial_room_is_reproduced_byte_for_byte()
    {
        byte[] reference =
        [
            0x01, 0x01, 0x00, 0x43, 0x23, 0xB2, 0x30, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x49, 0xEE, 0x00, 0x00, 0x04,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x99, 0x63, 0xE6, 0xF0, 0x00, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x9A, 0x63, 0xAF, 0xEF, 0x00, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x9B, 0x63, 0xE6, 0xF0, 0x00, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x9C, 0x63, 0xAF, 0xEF, 0x00, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01,
            0x00, 0x01, 0x00, 0x40, 0x38, 0x00, 0x00, 0x8F, 0x36, 0x6C, 0x3B, 0x19, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0xFF,
        ];

        var run = AtStart with
        {
            CurrentCell = new MazeCell(1, 1),
            CurrentRoomMapId = 61001,
        };
        DungeonRoomActor[] actors =
        [
            new DungeonRoomActor(61670, 1, 0),
            new DungeonRoomActor(61359, 1, 0),
            new DungeonRoomActor(61670, 1, 0),
            new DungeonRoomActor(61359, 1, 0),
        ];
        var drops = await Floor(run, new DungeonRoomDrop(0, ShoulderItemId, 25));

        var body = DungeonEntryPayloadCodec.EncodeStartMap(
            run, firstEntry: true, Rooms.Describe(actors), 0x6399, drops);

        // Each side draws these for itself: the room's seed and the drop's instance value.
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), 0x30B22343);
        Array.Copy(reference, 111, body, 111, 4);

        Assert.Equal(reference, body);
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
    public async Task Start_map_carries_what_the_rooms_breakable_objects_hold()
    {
        var drops = await Floor(AtStart, new DungeonRoomDrop(0, ShoulderItemId, 25));

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
    /// A material on the floor is announced with its count, not with a discriminator.
    /// </summary>
    /// <remarks>
    /// The defect this pins, reported live on 2026-08-29: every piece on the floor carried the
    /// value that tells one piece of equipment from another, and for anything that stacks the
    /// client reads that field as a count. A 31-bit pseudo-random number read as several hundred
    /// thousand of a material.
    /// </remarks>
    [Fact]
    public async Task A_material_on_the_floor_is_announced_with_its_count()
    {
        var drops = await Floor(AtStart, new DungeonRoomDrop(0, MaterialItemId, 0, Count: 3));

        var body = DungeonEntryPayloadCodec.EncodeStartMap(
            AtStart, firstEntry: true, NoActors, FirstSequence, drops);

        var entry = body.AsSpan(20);
        Assert.Equal((uint)MaterialItemId, BinaryPrimitives.ReadUInt32LittleEndian(entry[3..]));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(entry[7..]));
    }

    /// <summary>
    /// A piece of equipment keeps the discriminator, and it is the one the pick-up banks.
    /// </summary>
    /// <remarks>
    /// The two halves have to agree or the piece in the bag stops being recognisably the piece
    /// that was on the floor -- which is what <c>GroundPickupSlice</c> relies on to carry the
    /// announcement across rather than invent a second one.
    /// </remarks>
    [Fact]
    public async Task Equipment_on_the_floor_keeps_the_discriminator_the_pickup_banks()
    {
        var drops = await Floor(AtStart, new DungeonRoomDrop(0, ShoulderItemId, 25));

        var body = DungeonEntryPayloadCodec.EncodeStartMap(
            AtStart, firstEntry: true, NoActors, FirstSequence, drops);

        var entry = body.AsSpan(20);
        Assert.Equal(
            GameDropIdentity.ForDeclaredObject(AtStart, 0),
            BinaryPrimitives.ReadUInt32LittleEndian(entry[7..]));
    }

    /// <summary>
    /// An item the archive does not declare is announced with a count rather than refused.
    /// </summary>
    /// <remarks>
    /// A room is being described and the client will draw the piece either way. A count is the
    /// reading that cannot put an absurd number on the floor, so it is the one an unknown item
    /// gets.
    /// </remarks>
    [Fact]
    public async Task An_item_the_archive_does_not_declare_is_announced_with_a_count()
    {
        var drops = await Floor(AtStart, new DungeonRoomDrop(0, 999999, 0, Count: 2));

        var body = DungeonEntryPayloadCodec.EncodeStartMap(
            AtStart, firstEntry: true, NoActors, FirstSequence, drops);

        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(27)));
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
            bases.Add(Rooms.FirstActorSequenceFor(
                CreationContext(AtStart with { CurrentRoomMapId = mapId })));
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

    [Fact]
    public void Reusing_one_map_resource_in_distinct_cells_does_not_reuse_actor_identities()
    {
        var first = CreationContext(AtStart) with { Cell = new(2, 1), MapId = 15305 };
        var second = first with { Cell = new(2, 2) };
        Assert.NotEqual(Rooms.FirstActorSequenceFor(first), Rooms.FirstActorSequenceFor(second));
        Assert.Equal(Rooms.FirstActorSequenceFor(first), Rooms.FirstActorSequenceFor(first));
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
        var first = Rooms.FirstActorSequenceFor(CreationContext(AtStart));

        Assert.Equal(first, Rooms.FirstActorSequenceFor(CreationContext(AtStart)));
        Assert.InRange(first, 1, 60000);
    }

    /// <summary>
    /// A piece already picked up is gone from the room without renumbering what is left.
    /// </summary>
    /// <remarks>
    /// The cell owns its current floor. Removing one item must not renumber the surviving items:
    /// a client that took the second of three has to keep naming the third by three.
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

        var beforeTaking = WithDeclaredGroundItems(AtStart, declared);
        Assert.Equal([1, 2, 3], beforeTaking.CurrentCellState.GroundItems.Select(drop => drop.Number));

        var afterTaking = beforeTaking with
        {
            CurrentCellState = beforeTaking.CurrentCellState with
            {
                Revision = 1,
                GroundItems = beforeTaking.CurrentCellState.GroundItems
                    .Where(drop => drop.Number != 2).ToArray(),
            },
        };

        Assert.Equal(
            [1, 3], afterTaking.CurrentCellState.GroundItems.Select(drop => drop.Number));
        Assert.Equal(14401, afterTaking.CurrentCellState.FindGroundItem(3)!.ItemId);
        Assert.Null(afterTaking.CurrentCellState.FindGroundItem(2));
    }

    /// <summary>
    /// The floor of one room is not the floor of another, even when both number from one.
    /// </summary>
    [Fact]
    public void Clearing_one_rooms_floor_leaves_the_next_rooms_alone()
    {
        DungeonRoomDrop[] declared = [new DungeonRoomDrop(0, 14400, 25)];
        var populated = WithDeclaredGroundItems(AtStart, declared);
        var afterTaking = populated with
        {
            CurrentCellState = populated.CurrentCellState with
            {
                Revision = 1,
                GroundItems = [],
            },
        };

        Assert.Empty(afterTaking.CurrentCellState.GroundItems);

        var nextRoom = afterTaking with
        {
            CurrentCell = new MazeCell(1, 1),
            CurrentRoomMapId = 61001,
            CurrentCellState = WithDeclaredGroundItems(
                AtStart with
                {
                    CurrentCell = new MazeCell(1, 1),
                    CurrentRoomMapId = 61001,
                },
                declared).CurrentCellState,
        };

        Assert.Single(nextRoom.CurrentCellState.GroundItems);
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
                var instance = GameDropIdentity.ForDeclaredObject(
                    AtStart with { CurrentRoomMapId = mapId }, objectIndex);

                Assert.InRange(instance, 1u, (uint)int.MaxValue);
            }
        }
    }

    /// <summary>A room the archive declares nothing in.</summary>
    private static readonly RoomActorDescription[] NoActors = [];

    /// <summary>
    /// The room rules, for the two answers these tests need of them: where an actor type stops
    /// being a monster, and the number a room's first actor takes. Neither reads a source.
    /// </summary>
    private static readonly IDungeonRoomRuleService Rooms =
        new DungeonRoomRuleService(new NoRoomActors(), new NoRoomDrops(), new ObjectDropRuleService(null!, null!, null!, new()), new NoRoomObjects());

    private static DungeonCellCreationContext CreationContext(DungeonRunState run) => new(
        run.RunId,
        run.Layout,
        run.CurrentCell,
        run.CurrentRoomMapId,
        run.Difficulty,
        run.StartedAt,
        run.Participants);


    /// <summary>A room with nothing breakable in it.</summary>
    private static readonly FloorPiece[] NoDrops = [];

    /// <summary>The equipment the tutorial's second room holds, and a material that stacks.</summary>
    private const int ShoulderItemId = 14400;

    private const int MaterialItemId = 10088621;

    /// <summary>
    /// Describes the floor through the real resolver, with an archive that knows two items.
    /// </summary>
    /// <remarks>
    /// Through the resolver rather than by building the pieces here: what goes in that field is
    /// the thing under test, and a fixture that decided it itself would agree with whatever the
    /// production code did.
    /// </remarks>
    private static async ValueTask<IReadOnlyList<FloorPiece>> Floor(
        DungeonRunState run, params DungeonRoomDrop[] declared)
    {
        // Which pieces are equipment is the placement rules' answer, taken from the real service
        // over an archive that knows two items, so this fixture decides nothing the frame is
        // about.
        var placementRules = new ItemPlacementRuleService(new TwoItems(), new NoWearables());
        var described = new List<FloorDropDescription>();
        var populated = WithDeclaredGroundItems(run, declared);
        foreach (var item in populated.CurrentCellState.GroundItems)
        {
            var drop = new GroundDrop(
                item.Number,
                new DungeonRoomDrop(
                    item.Source.ObjectIndex ?? checked((byte)(item.Number - 1)),
                    item.ItemId,
                    item.Durability,
                    item.Count));
            described.Add(new FloorDropDescription(
                drop,
                await placementRules.IsEquipmentAsync(drop.Drop.ItemId, CancellationToken.None)));
        }

        return FloorPieceValues.Describe(populated, described);
    }

    private static DungeonRunState WithDeclaredGroundItems(
        DungeonRunState run,
        IReadOnlyList<DungeonRoomDrop> declared) => run with
    {
        CurrentCellState = new DungeonCellState(
            run.CurrentCell,
            run.CurrentRoomMapId,
            Revision: 0,
            FirstActorSequence: run.CurrentCellState.FirstActorSequence,
            Actors: run.CurrentCellState.Actors,
            GroundItems: declared.Select((drop, index) => new DungeonGroundItem(
                Number: checked((ushort)(index + 1)),
                drop.ItemId,
                Math.Max(1, drop.Count),
                drop.Durability,
                IsEquipment: false,
                DungeonGroundItemSource.Declared(drop.ObjectIndex))).ToArray()),
    };

    /// <summary>An archive in which the shoulder is equipment and the material is not.</summary>
    private sealed class TwoItems : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(itemId switch
            {
                ShoulderItemId => new ItemPlacement(ItemKind: 1, StackLimit: 1,
                    Destination: new(BusinessInventorySpace.CarriedEquipment, 0, 0))
                {
                    WorldClass = ItemWorldClass.Equipment,
                },
                MaterialItemId => new ItemPlacement(ItemKind: 4, StackLimit: 999,
                    Destination: new(BusinessInventorySpace.Material, 0, 0)),
                _ => null,
            });
    }

    /// <summary>Nothing here is worn; these tests never put a piece on.</summary>
    private sealed class NoWearables : IEquipmentSlotDefinitionSource
    {
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BusinessSlot?>(null);
    }

    /// <summary>A room declaring no actors; these tests supply their own or need none.</summary>
    private sealed class NoRoomObjects : IDungeonRoomObjectSource
    {
        public ValueTask<IReadOnlyList<DungeonRoomObject>> GetObjectsAsync(int mapId, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomObject>>([]);
    }

    private sealed class NoRoomActors : IDungeonRoomActorSource
    {
        public ValueTask<IReadOnlyList<DungeonRoomActor>> GetAsync(
            int mapId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomActor>>([]);
    }

    /// <summary>A room declaring nothing on its floor.</summary>
    private sealed class NoRoomDrops : IDungeonRoomDropSource
    {
        public ValueTask<IReadOnlyList<ObjectDropDefinition>> GetAsync(
            int dungeonId, int mapId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ObjectDropDefinition>>([]);
    }

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
    /// The seed is on the wire because the client draws on it, and it is the room's rather than
    /// the run's: the reference server draws a fresh one for every room it builds. Two things are
    /// guarded here -- that two rooms of one run do not get the same value, and that a room
    /// described twice describes itself the same way, which is what a revisit needs and what the
    /// reference gets from caching the room instead.
    /// </remarks>
    [Fact]
    public void Start_map_carries_a_seed_of_its_own_room_that_a_revisit_repeats()
    {
        var next = AtStart with
        {
            CurrentCell = new MazeCell(1, 1),
            CurrentRoomMapId = 61001,
        };

        var first = DungeonEntryPayloadCodec.EncodeStartMap(AtStart, firstEntry: true, NoActors, FirstSequence, NoDrops);
        var second = DungeonEntryPayloadCodec.EncodeStartMap(next, firstEntry: true, NoActors, FirstSequence, NoDrops);
        var again = DungeonEntryPayloadCodec.EncodeStartMap(next, firstEntry: false, NoActors, FirstSequence, NoDrops);

        var firstSeed = BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(3));
        var secondSeed = BinaryPrimitives.ReadInt32LittleEndian(second.AsSpan(3));

        Assert.NotEqual(firstSeed, secondSeed);
        Assert.Equal(secondSeed, BinaryPrimitives.ReadInt32LittleEndian(again.AsSpan(3)));
    }

    /// <remarks>
    /// The reference server clears bit 18 of every room seed it sends, and does not say why. The
    /// client has not been read for what it does with the bit; what is known is that the reference
    /// will not send it set, so neither does this.
    /// </remarks>
    [Fact]
    public void A_room_seed_never_carries_the_bit_the_reference_always_clears()
    {
        MazeCell[] cells =
        [
            new MazeCell(0, 0), new MazeCell(1, 1), new MazeCell(2, 1),
            new MazeCell(3, 0), new MazeCell(4, 2),
        ];

        foreach (var cell in cells)
        {
            var body = DungeonEntryPayloadCodec.EncodeStartMap(
                AtStart with { CurrentCell = cell }, firstEntry: true, NoActors, FirstSequence, NoDrops);

            var seed = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(3));
            Assert.Equal(0, seed & 0x40000);
            Assert.True(seed >= 0);
        }
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

    [Fact]
    public void Party_start_map_carries_the_receiving_participants_slot()
    {
        var participant = new DungeonRunParticipant(
            new Dfo.Server.Data.Accounts.AccountId(2),
            new Dfo.Server.Data.Characters.CharacterId(1002),
            Slot: 1,
            IsLeader: false);
        var run = AtStart with
        {
            Participant = participant,
            Participants = [participant],
        };

        var body = DungeonEntryPayloadCodec.EncodeStartMap(
            run, firstEntry: true, NoActors, FirstSequence, NoDrops);

        Assert.Equal(1, body[^1]);
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
        Assert.Equal(61001, Tutorial.Cells[new MazeCell(request.NextX, request.NextY)].MapCandidates[0]);
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
