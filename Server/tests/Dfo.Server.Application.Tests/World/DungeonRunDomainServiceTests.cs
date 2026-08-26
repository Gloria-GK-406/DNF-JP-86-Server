using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// What the server remembers about a run in progress.
/// </summary>
/// <remarks>
/// Deliberately little: the maze the character was given, which room they are in, and which of the
/// pieces lying on the floor have been taken. The last of those is here because what a room holds
/// is read back out of the archive every time the room is described -- without a record of what
/// has gone, a player could walk out of a room and back in and be handed the box again.
/// </remarks>
public sealed class DungeonRunDomainServiceTests
{
    private static readonly CharacterId Character = new(1);

    private static readonly DungeonLayout Tutorial = new(
        DungeonId: 10000,
        MazeIndex: 0,
        Width: 3,
        Height: 2,
        StartCell: new MazeCell(0, 1),
        BossCell: new MazeCell(2, 0),
        StartRoomMapId: 61000,
        Seed: 0x0BADF00D,
        Rooms: new Dictionary<MazeCell, int>
        {
            [new MazeCell(0, 1)] = 61000,
            [new MazeCell(1, 1)] = 61001,
            [new MazeCell(2, 0)] = 61005,
        });

    private static readonly DungeonRoomDrop[] OneBreakableObject = [new DungeonRoomDrop(0, 14400, 25)];

    [Fact]
    public void A_run_opens_with_nothing_taken_off_its_floor()
    {
        var service = new DungeonRunDomainService();

        var run = service.Begin(Character, Tutorial);

        Assert.Empty(run.TakenGroundDrops);
        Assert.Single(run.RemainingGroundDrops(OneBreakableObject));
    }

    [Fact]
    public void A_piece_can_be_taken_once()
    {
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial);

        var afterTaking = service.TryTakeGroundDrop(Character, 1);

        Assert.NotNull(afterTaking);
        Assert.Empty(afterTaking.RemainingGroundDrops(OneBreakableObject));
        Assert.Null(service.TryTakeGroundDrop(Character, 1));
    }

    /// <summary>
    /// Walking out of a cleared room and back into it does not put the piece back on the floor.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the run remembers anything about the floor. The room's own
    /// declaration is unchanged -- the archive still says the box holds a shoulder -- so a revisit
    /// that consulted only the archive would describe the room as it was before the player emptied
    /// it, and hand over a second shoulder for the asking.
    /// </remarks>
    [Fact]
    public void A_revisited_room_does_not_get_its_floor_back()
    {
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial);
        service.TryTakeGroundDrop(Character, 1);

        service.TryMove(Character, new MazeCell(1, 1));
        var returned = service.TryMove(Character, new MazeCell(0, 1));

        Assert.NotNull(returned);
        Assert.Empty(returned.RemainingGroundDrops(OneBreakableObject));
        Assert.Null(service.TryTakeGroundDrop(Character, 1));
    }

    /// <summary>Emptying one room's floor leaves the next room's alone.</summary>
    [Fact]
    public void The_next_room_still_has_what_it_declares()
    {
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial);
        service.TryTakeGroundDrop(Character, 1);

        var moved = service.TryMove(Character, new MazeCell(1, 1));

        Assert.NotNull(moved);
        Assert.Single(moved.RemainingGroundDrops(OneBreakableObject));
        Assert.NotNull(service.TryTakeGroundDrop(Character, 1));
    }

    [Fact]
    public void A_character_who_is_not_in_a_run_takes_nothing()
    {
        var service = new DungeonRunDomainService();

        Assert.Null(service.TryTakeGroundDrop(Character, 1));
    }
}
