using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Raids;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Raids;

[Trait("Category", "Unit")]
public sealed class AntonRaidTimeoutTests
{
    [Fact]
    public void Boss_clear_at_the_deadline_cannot_beat_a_delayed_timeout_job()
    {
        var begin = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        var attack = TimeSpan.FromMinutes(40);
        var rules = new AntonRaidRules(10, 20, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5),
            10096296, 1, 10099897, 1, [attack, attack])
        {
            Phases =
            [
                new([new(210, AntonRaidMapStatus.Open, 10, 1, 5, 4, DungeonDifficulty.Normal)],
                    [new([new(AntonRaidConditionKind.MapStatus, 210, (uint)AntonRaidMapStatus.Cleared)],
                        [new(AntonRaidActionKind.ClearPhase)])]),
            ],
        };
        var phase = new AntonRaidPhaseRuntime(Guid.NewGuid(), 1, 0, 1, rules, begin, begin + attack, 1);
        var deadline = Assert.Single(phase.Deadlines);
        var sortie = new AntonRaidPhaseRuntime.Sortie(new DungeonRunId(Guid.NewGuid()), 210, 1,
            new Dictionary<CharacterId, SelectedSessionIdentity>());
        phase.Sorties[1] = sortie;
        var early = phase.Copy();

        Assert.True(early.Clear(sortie, [new(1)], deadline.At.AddTicks(-1), 1));
        Assert.True(early.Succeeded);
        Assert.False(phase.Clear(sortie, [new(1)], deadline.At, 1));
        Assert.Empty(phase.ClearParticipants);
        Assert.True(phase.Advance(deadline, deadline.At, 1));
        Assert.False(phase.Succeeded);
    }
}
