using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

/// <summary>A run's reservations live and die with the run.</summary>
public sealed class DungeonRunClaimsTests
{
    [Fact]
    public void A_death_claimed_before_the_run_ended_never_commits()
    {
        TestGameplay.Run(() =>
        {
            var runs = TestParties.Runs();
            var run = Enter(runs);
            var claim = Assert.IsType<DungeonActorDeathClaim>(runs.TryClaimActorDeath(new(2), 7));
            Assert.True(runs.IsActorDeathClaimCurrent(claim));

            Assert.True(runs.End(new(2), run.RunId));

            Assert.Null(runs.Find(run.RunId));
            Assert.False(runs.IsActorDeathClaimCurrent(claim));
            Assert.Null(runs.CommitActorDeathChange(claim));
            Assert.False(runs.ReleaseActorDeath(claim));
        });
    }

    [Fact]
    public void Claims_of_one_run_do_not_reach_another()
    {
        TestGameplay.Run(() =>
        {
            var runs = TestParties.Runs();
            var first = Enter(runs);
            var claim = Assert.IsType<DungeonActorDeathClaim>(runs.TryClaimActorDeath(new(2), 7));
            Assert.True(runs.End(new(2), first.RunId));

            var second = Enter(runs);
            Assert.NotEqual(first.RunId, second.RunId);
            // The same actor in the new run is free to claim, and the old claim does not commit into it.
            var fresh = Assert.IsType<DungeonActorDeathClaim>(runs.TryClaimActorDeath(new(2), 7));
            Assert.Null(runs.CommitActorDeathChange(claim));
            Assert.True(runs.IsActorDeathClaimCurrent(fresh));
        });
    }

    private static DungeonRunState Enter(IDungeonRunDomainService runs)
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(144, 0, 1, 1, cell, cell, 61000, 1,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(61000) });
        DungeonRunParticipant[] members = [new(new(1), new(2), 0, true)];
        TestParties.EnsureEntryParty(runs, members);
        return runs.PreparePartyEntry(members, layout, DungeonDifficulty.Normal,
            new(cell, 61000, 1, [new(7, new(13099, 1, 0), false, false, [])], []),
            DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
    }
}
