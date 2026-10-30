using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Application.Tests.World.DungeonDepartureProtocolCharacterizationTests;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// Issue #202 through the production container: a solo give-up ends the whole run (AC-006), and a member's
/// back-to-village request leaves the party's run running (AC-008).
/// </summary>
public sealed class DungeonPartyOwnedRunProtocolTests
{
    private const ushort GiveUp = 0x2A, BackToVillage = 0x84;
    private const string ParticipantLeft = "SELECT COUNT(*) FROM domain_events WHERE event_type='dungeon.participant-left'";

    [Fact]
    public async Task Giving_up_alone_ends_the_whole_run()
    {
        // The second character is online but in no party and no run.
        using var run = await TwoMemberRun.EnterAsync(party: false);
        var departures = run.F.Scalar(ParticipantLeft);

        await run.WireAsync(Side.Leader, GiveUp, []);

        Assert.Contains((Side.Leader, GiveUp), run.Sequence());
        Assert.DoesNotContain(run.Sequence(), frame => frame.Side == Side.Member);
        Assert.Empty(run.F.Sender.Outbound.Closes);
        Assert.Equal(departures + 1, run.F.Scalar(ParticipantLeft));
        TestGameplay.Run(run.F.Services, () =>
        {
            var runs = run.F.Services.DungeonRuns();
            Assert.Null(runs.Find(run.RunId));
            Assert.Null(runs.Find(run.Leader.CharacterId));
            Assert.Empty(run.Deliveries.Find(run.RunId));
            Assert.Null(run.F.Services.GetRequiredService<IDungeonParticipationDomainService>().Find(run.Leader.CharacterId));
        });
    }

    [Fact]
    public async Task Back_to_village_by_a_member_does_not_end_the_run()
    {
        // Not reachable today (an entry intent and a run are not held together); only the leader may end the run.
        using var run = await TwoMemberRun.EnterAsync();
        TestGameplay.Run(run.F.Services, () => run.F.Services.GetRequiredService<IDungeonEntryIntentDomainService>()
            .Record(run.Member, DungeonEntryIntent.Interactive));
        var departures = run.F.Scalar(ParticipantLeft);

        await run.WireAsync(Side.Member, BackToVillage, []);

        Assert.Contains((Side.Member, BackToVillage), run.Sequence());
        Assert.Equal(departures, run.F.Scalar(ParticipantLeft));
        TestGameplay.Run(run.F.Services, () =>
        {
            var runs = run.F.Services.DungeonRuns();
            var participation = run.F.Services.GetRequiredService<IDungeonParticipationDomainService>();
            foreach (var member in new[] { run.Leader, run.Member })
            {
                var view = Assert.IsType<DungeonRunState>(runs.Find(member.CharacterId));
                Assert.Equal(run.RunId, view.RunId);
                Assert.Equal(2, view.Participants.Count);
                Assert.Equal(run.Leader.CharacterId, Assert.Single(view.Participants, p => p.IsLeader).CharacterId);
                Assert.NotNull(participation.Find(member.CharacterId));
            }
            Assert.Equal([run.Leader, run.Member], run.Deliveries.Find(run.RunId));
            run.AssertParty(leader: run.Leader, run.Leader, run.Member);
        });
    }
}
