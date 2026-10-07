using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class HellRewardSettlementTests
{
    private static readonly CharacterId Character = new(10);
    private static readonly MazeCell Cell = new(0, 0);
    private static readonly DungeonLayout Layout = new(77, 0, 1, 1, Cell, Cell, 60064, 123,
        new Dictionary<MazeCell, DungeonCellDefinition> { [Cell] = DungeonCellDefinition.Fixed(60064) });

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, true)]
    [InlineData(1, 2, false)]
    public void Only_actual_last_actor_releases_frozen_group_reward_once(int first, int last, bool eligible)
    {
        var own = new RolledDrop(3330, 1, DropRarity.Common, DropItemKind.Stackable);
        var reward = new RolledDrop(14400, 1, DropRarity.Epic, DropItemKind.Equipment, 25);
        var actors = new[] { Actor(1, 136, [own]), Actor(2, 136, [own]) };
        actors[last - 1] = actors[last - 1] with { EligibleForHellGroupReward = eligible };
        var seed = new DungeonCellSeed(Cell, 60064, 1, actors, [])
        { HellGroups = [new(136, [1, 2], [reward])] };
        var service = Start(seed);
        // Published state cannot be mutated through the seed arrays or query arrays.
        ((ushort[])service.Find(Character)!.CurrentCellState!.HellGroups[0].ActorSequences)[0] = 999;
        var firstClaim = service.TryClaimActorDeath(Character, (ushort)first)!;
        Assert.NotNull(firstClaim);
        var firstChange = service.CommitActorDeathChange(firstClaim)!;
        Assert.Equal(3330, Assert.Single(firstChange.AddedGroundItems).ItemId);
        Assert.False(Assert.Single(firstChange.Run.CurrentCellState!.HellGroups).IsSettled);
        Assert.Null(service.CommitActorDeathChange(firstClaim));
        var lastClaim = service.TryClaimActorDeath(Character, (ushort)last)!;
        var change = service.CommitActorDeathChange(lastClaim)!;
        Assert.Equal(eligible ? 2 : 1, change.AddedGroundItems.Count);
        Assert.Equal(eligible ? 1 : 0, change.AddedGroundItems.Count(x => x.ItemId == 14400));
        var group = Assert.Single(change.Run.CurrentCellState!.HellGroups);
        Assert.True(group.IsSettled);
        Assert.Empty(group.PendingLoot);
        Assert.Null(service.CommitActorDeathChange(lastClaim));
        Assert.Null(service.TryClaimActorDeath(Character, (ushort)last));
        Assert.Equal(eligible ? 3 : 2, service.Find(Character)!.CurrentCellState!.GroundItems.Count);
    }

    [Fact]
    public void Independent_groups_and_empty_rewards_complete_without_replay()
    {
        var seed = new DungeonCellSeed(Cell, 60064, 1,
            [Actor(1, 136, []), Actor(2, 136, []), Actor(3, 111, [])], [])
        { HellGroups = [new(136, [1, 2], []), new(111, [3], [new(99, 1, DropRarity.Rare, DropItemKind.Stackable)])] };
        var service = Start(seed);
        var pending = service.TryClaimActorDeath(Character, 3)!;
        Assert.True(service.ReleaseActorDeath(pending));
        Assert.False(service.Find(Character)!.CurrentCellState!.HellGroups.Single(x => x.GroupIndex == 111).IsSettled);
        Assert.Empty(service.Find(Character)!.CurrentCellState!.GroundItems);
        Assert.Null(service.CommitActorDeathChange(pending));
        var third = service.CommitActorDeathChange(service.TryClaimActorDeath(Character, 3)!)!;
        Assert.Single(third.AddedGroundItems);
        Assert.False(third.Run.CurrentCellState!.HellGroups.Single(x => x.GroupIndex == 136).IsSettled);
        service.CommitActorDeathChange(service.TryClaimActorDeath(Character, 2)!);
        var final = service.CommitActorDeathChange(service.TryClaimActorDeath(Character, 1)!)!;
        Assert.Empty(final.AddedGroundItems);
        Assert.All(final.Run.CurrentCellState!.HellGroups, group => Assert.True(group.IsSettled));
    }

    [Fact]
    public void Invalid_reward_does_not_partially_mark_actor_or_group_dead()
    {
        var seed = new DungeonCellSeed(Cell, 60064, 1, [Actor(1, 136, [])], [])
        { HellGroups = [new(136, [1], [new(99, 0, DropRarity.Rare, DropItemKind.Stackable)])] };
        var service = Start(seed);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CommitActorDeathChange(service.TryClaimActorDeath(Character, 1)!));
        var state = service.Find(Character)!.CurrentCellState!;
        Assert.False(state.Actors[0].IsDead);
        Assert.False(state.HellGroups[0].IsSettled);
        Assert.Empty(state.GroundItems);
    }

    private static DungeonCellActorState Actor(ushort sequence, int group, IReadOnlyList<RolledDrop> loot) =>
        new(sequence, new DungeonRoomActor(20444, 75, 5), true, false, loot, false)
        { IsHellTemplate = true, HellGroupIndex = group, EligibleForHellGroupReward = true };

    private static DungeonRunDomainService Start(DungeonCellSeed seed)
    {
        var service = new DungeonRunDomainService();
        service.PreparePartyEntry([new(new(1), Character, 0, true)], Layout, DungeonDifficulty.Normal,
            initialCell: seed, DungeonRunId.New(), DateTimeOffset.UtcNow);
        return service;
    }
}
