using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Raids;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Raids;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Raids;

public sealed class AntonRaidPhaseRuntimeTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);

    [PvfFact]
    public void First_stage_resets_without_extending_deadline_and_counts_both_legs()
    {
        using var archive = new PvfArchiveProvider(new PvfContentOptions());
        var rules = new PvfAntonRaidContentSource(archive).Load();
        Assert.Equal(28, rules.Phases[0].Triggers.Count);
        var runtime = new AntonRaidPhaseRuntime(Guid.NewGuid(), 1, 0, 1, rules, Start, Start.AddMinutes(40), 4);
        var deadline = runtime.Deadlines.Single();
        Assert.Equal(AntonRaidCombatDeadlineKind.Attack, deadline.Kind);
        Assert.False(runtime.HasCapacity(213, 1));
        Clear(runtime, 210, 4, Start);
        Assert.Equal(AntonRaidMapStatus.Hidden, runtime.Snapshot().Maps[210]);
        Assert.Equal(AntonRaidMapStatus.Closed, runtime.Snapshot().Maps[211]);
        var firstNavalOpening = runtime.Deadlines.Single(timer => timer.Key == 216 && timer.Kind == AntonRaidCombatDeadlineKind.ReservedMap);
        Assert.True(runtime.Advance(firstNavalOpening, Start.AddSeconds(20), 4));
        Assert.False(runtime.Advance(firstNavalOpening, Start.AddSeconds(20), 4));
        Assert.Equal(new[] { 1, 3 }, runtime.Deadlines.Where(timer => timer.Key == 216).Select(timer => (int)timer.TimerType).Order().ToArray());

        var failure = runtime.Deadlines.Single(timer => timer.Key == 212 && timer.TimerType == 1);
        Assert.True(runtime.Advance(failure, failure.At, 4));
        Assert.Null(runtime.Succeeded);
        Assert.True(runtime.HasCapacity(210, 1));
        Assert.False(runtime.HasCapacity(213, 1));
        Assert.Contains(deadline, runtime.Deadlines);

        Clear(runtime, 210, 4, failure.At);
        Clear(runtime, 213, 2, failure.At);
        Clear(runtime, 215, 1, failure.At);
        Assert.Null(runtime.Succeeded);
        Clear(runtime, 215, 1, failure.At);
        Assert.True(runtime.Succeeded);
        Assert.Empty(runtime.Deadlines);
    }

    [PvfFact]
    public void Second_stage_selects_three_hatcheries_and_finishes_after_five_heart_clears()
    {
        using var archive = new PvfArchiveProvider(new PvfContentOptions());
        var rules = new PvfAntonRaidContentSource(archive).Load();
        Assert.Equal(23, rules.Phases[1].Triggers.Count);
        Assert.Equal(5, rules.Buffs.Count);
        var runtime = new AntonRaidPhaseRuntime(Guid.NewGuid(), 1, 1, 1, rules, Start, Start.AddMinutes(40), 4);
        Assert.Equal(10_000u, runtime.Snapshot().Symbols[rules.Symbols["[BLACK VOLCANO BARRIER]"]]);
        Assert.False(runtime.HasCapacity(220, 1));
        var hatchery = runtime.Deadlines.Single(timer => timer.Key == 219);
        Assert.True(runtime.Advance(hatchery, hatchery.At, 4));
        var snapshot = runtime.Snapshot();
        Assert.Equal(3, rules.Hatcheries.Count(id => snapshot.Maps[id] == AntonRaidMapStatus.Open));
        Assert.Equal(AntonRaidMapStatus.Open, snapshot.Maps[(int)snapshot.InfectedMap]);

        Clear(runtime, 219, 1, hatchery.At);
        Assert.All(rules.Hatcheries, id => Assert.Equal(AntonRaidMapStatus.Hidden, runtime.Snapshot().Maps[id]));
        Clear(runtime, 220, 4, hatchery.At);
        Assert.Null(runtime.Succeeded);
        Clear(runtime, 220, 1, hatchery.At);
        Assert.True(runtime.Succeeded);
        Assert.Empty(runtime.Deadlines);
    }

    private static void Clear(AntonRaidPhaseRuntime runtime, int dungeon, int count, DateTimeOffset now)
    {
        for (var index = 0; index < count; index++)
        {
            Assert.True(runtime.HasCapacity(dungeon, 1));
            var sortie = new AntonRaidPhaseRuntime.Sortie(DungeonRunId.New(), dungeon, 1,
                new Dictionary<CharacterId, Dfo.Server.Data.World.SelectedSessionIdentity>());
            runtime.Sorties.Add(1, sortie);
            Assert.True(runtime.Clear(sortie, [new CharacterId(1)], now, 4));
            runtime.Sorties.Remove(1);
        }
    }
}
