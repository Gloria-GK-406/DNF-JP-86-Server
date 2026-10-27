using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// Issue #202: a run belongs to one party, and its members are the party's current members who entered it.
/// Characters reach the run through their party; a member leaves the party before leaving the run.
/// </summary>
public sealed partial class DungeonRunDomainServiceTests
{
    private static readonly DungeonRunParticipant SoloEntrant = new(new AccountId(303), new CharacterId(3), 0, true);

    [Fact]
    public void Members_find_the_run_of_their_current_party_and_lose_it_when_they_leave()
    {
        TestGameplay.Run(() =>
        {
            var runs = TestParties.Runs();
            var parties = TestParties.Of(runs);
            var party = runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            var solo = runs.PreparePartyEntry([SoloEntrant], Tutorial, DungeonDifficulty.Normal).Run;

            foreach (var member in new[] { DepartureLeader, DepartureMember })
            {
                var view = Assert.IsType<DungeonRunState>(runs.Find(member.CharacterId));
                Assert.Equal(party.RunId, view.RunId);
                Assert.Equal(PartyKeyKind.Real, view.PartyKey.Kind);
                Assert.Equal(parties.KeyOf(member.CharacterId), view.PartyKey);
            }
            var soloView = Assert.IsType<DungeonRunState>(runs.Find(SoloEntrant.CharacterId));
            Assert.Equal(solo.RunId, soloView.RunId);
            Assert.Equal(PartyKey.Virtual(SoloEntrant.CharacterId), soloView.PartyKey);

            TestParties.Leave(runs, DepartureMember.CharacterId);

            Assert.Null(runs.Find(DepartureMember.CharacterId));
            Assert.Equal(party.RunId, runs.Find(DepartureLeader.CharacterId)!.RunId);
            Assert.Equal(solo.RunId, runs.Find(SoloEntrant.CharacterId)!.RunId);
        });
    }

    [Fact]
    public void A_new_run_of_the_same_party_is_the_one_every_member_finds()
    {
        TestGameplay.Run(() =>
        {
            var runs = TestParties.Runs();
            var old = runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;

            var next = runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;

            Assert.Null(runs.Find(old.RunId));
            Assert.Equal(next.RunId, runs.Find(DepartureLeader.CharacterId)!.RunId);
            Assert.Equal(next.RunId, runs.Find(DepartureMember.CharacterId)!.RunId);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_member_still_beside_others_in_the_party_cannot_end_the_run_alone(bool leader)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var probe = DepartureProbe.ForParty();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            var asking = leader ? DepartureLeader : DepartureMember;
            var revision = probe.Runs.Find(run.RunId)!.EntryRevision;

            Assert.False(await probe.Runs.EndAsync(asking.CharacterId, run.RunId));
            Assert.False(probe.Runs.End(asking.CharacterId, run.RunId));
            Assert.False(probe.Runs.End(asking.CharacterId));

            var current = Assert.IsType<DungeonRunState>(probe.Runs.Find(run.RunId));
            Assert.Equal(revision, current.EntryRevision);
            Assert.Equal(DepartureLeader.CharacterId, Assert.Single(current.Participants, p => p.IsLeader).CharacterId);
            foreach (var member in new[] { DepartureLeader, DepartureMember })
            {
                Assert.Equal(run.RunId, probe.Runs.Find(member.CharacterId)!.RunId);
                probe.AssertUntouched(member.CharacterId);
            }
            probe.AssertPartyKept(run);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_last_member_departing_ends_the_whole_run(bool leavesThePartyFirst)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var probe = DepartureProbe.ForParty();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            Assert.True(probe.Parties.LeaveParty(DepartureMember.CharacterId).Succeeded);
            Assert.True(await probe.Runs.EndAsync(DepartureMember.CharacterId, run.RunId));
            Assert.Equal(run.RunId, probe.Runs.Find(DepartureLeader.CharacterId)!.RunId);
            // Alone in the party the leader may give up directly, or after the party is gone.
            if (leavesThePartyFirst)
                Assert.True(probe.Parties.LeaveParty(DepartureLeader.CharacterId).Succeeded);

            Assert.True(await probe.Runs.EndAsync(DepartureLeader.CharacterId, run.RunId));

            Assert.Null(probe.Runs.Find(run.RunId));
            Assert.Null(probe.Runs.Find(DepartureLeader.CharacterId));
            foreach (var member in new[] { DepartureLeader, DepartureMember })
                probe.AssertDeparted(member.CharacterId, run, failureWeakness: true);
            Assert.False(await probe.Runs.EndAsync(DepartureMember.CharacterId, run.RunId));
        });
    }

    [Fact]
    public async Task A_departed_member_departs_only_once()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var probe = DepartureProbe.ForParty();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            Assert.True(probe.Parties.LeaveParty(DepartureMember.CharacterId).Succeeded);
            Assert.True(await probe.Runs.EndAsync(DepartureMember.CharacterId, run.RunId));

            Assert.False(await probe.Runs.EndAsync(DepartureMember.CharacterId, run.RunId));
            Assert.False(probe.Runs.End(DepartureMember.CharacterId));

            probe.AssertDeparted(DepartureMember.CharacterId, run, failureWeakness: true);
            probe.AssertUntouched(DepartureLeader.CharacterId);
        });
    }

    [Fact]
    public async Task Ending_the_whole_run_departs_every_member_and_keeps_the_party()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var probe = DepartureProbe.ForParty();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            probe.ReadyAll(run);
            Assert.NotNull(probe.Runs.TryMarkParticipantDead(DepartureMember.CharacterId, run.RunId, run.RoomVersion));

            var ended = await probe.Runs.EndRunAsync(run.RunId);

            Assert.Equal([DepartureLeader.CharacterId, DepartureMember.CharacterId], ended);
            Assert.Null(probe.Runs.Find(run.RunId));
            foreach (var member in new[] { DepartureLeader, DepartureMember })
                probe.AssertDeparted(member.CharacterId, run, failureWeakness: true);
            probe.AssertPartyKept(run);
            Assert.Empty(await probe.Runs.EndRunAsync(run.RunId));
        });
    }

    [Fact]
    public async Task Ending_the_whole_run_after_clear_applies_no_failure_weakness()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var probe = DepartureProbe.ForParty();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            Assert.NotNull(probe.Runs.TryOpenClear(DepartureLeader.CharacterId, run.RunId, run.RoomVersion));

            Assert.Equal(2, (await probe.Runs.EndRunAsync(run.RunId)).Count);

            foreach (var member in new[] { DepartureLeader, DepartureMember })
                probe.AssertDeparted(member.CharacterId, run, failureWeakness: false);
        });
    }

    public enum ExitPath { MemberLeaves, LeaderLeaves, MemberRemoved, WholeEnd, ClearReturn, PartyWipe, EntryAbort, NewRun }

    /// <summary>
    /// RQ-009: while each exit runs (read from inside its per-character release) and after it, every view a
    /// character or the run id reaches has exactly one leader and contains the character it was read for.
    /// </summary>
    [Theory]
    [InlineData(ExitPath.MemberLeaves)]
    [InlineData(ExitPath.LeaderLeaves)]
    [InlineData(ExitPath.MemberRemoved)]
    [InlineData(ExitPath.WholeEnd)]
    [InlineData(ExitPath.ClearReturn)]
    [InlineData(ExitPath.PartyWipe)]
    [InlineData(ExitPath.EntryAbort)]
    [InlineData(ExitPath.NewRun)]
    public async Task Views_have_exactly_one_leader_during_and_after_every_exit(ExitPath path)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var weakness = new ViewReadingWeakness();
            var runs = TestParties.Runs(weakness: weakness);
            var parties = TestParties.Of(runs);
            var prepared = runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal);
            var run = prepared.Run;
            weakness.Watch(runs, run.RunId, DepartureLeader.CharacterId, DepartureMember.CharacterId);
            var now = DateTimeOffset.UnixEpoch;

            switch (path)
            {
                case ExitPath.MemberLeaves or ExitPath.LeaderLeaves or ExitPath.MemberRemoved:
                    var leaving = path == ExitPath.LeaderLeaves ? DepartureLeader : DepartureMember;
                    var change = path == ExitPath.MemberRemoved
                        ? parties.RemoveMember(DepartureLeader.CharacterId, leaving.CharacterId)
                        : parties.LeaveParty(leaving.CharacterId);
                    Assert.True(change.Succeeded);
                    weakness.Check();
                    Assert.True(await runs.EndAsync(leaving.CharacterId, run.RunId));
                    break;
                case ExitPath.WholeEnd:
                    Assert.Equal(2, (await runs.EndRunAsync(run.RunId)).Count);
                    break;
                case ExitPath.ClearReturn:
                    Assert.NotNull(runs.TryOpenClear(DepartureLeader.CharacterId, run.RunId, run.RoomVersion));
                    var free = new ClearRewardCardFace(ClearRewardCardKind.Free, [new(0, null)], 1);
                    var paid = new ClearRewardCardFace(ClearRewardCardKind.Paid, [new(0, null)], 2);
                    foreach (var member in new[] { DepartureLeader, DepartureMember })
                    {
                        Assert.NotNull(runs.FixClearRewardCards(member.CharacterId, run.RunId, free, paid, 3));
                        Assert.NotNull(runs.CommitSettlement(runs.TryClaimSettlement(member.CharacterId, run.RunId)!));
                        Assert.NotNull(runs.TryOpenCardLayout(member.CharacterId, run.RunId));
                    }
                    runs.ScheduleClearReturn(DepartureLeader.CharacterId, run.RunId, now);
                    Assert.NotNull(await runs.TryEndClearReturnAsync(run.RunId, now.AddSeconds(60)));
                    break;
                case ExitPath.PartyWipe:
                    foreach (var member in new[] { DepartureLeader, DepartureMember })
                        Assert.NotNull(runs.TryMarkParticipantReady(member.CharacterId, run.RunId, run.RoomVersion));
                    foreach (var member in new[] { DepartureLeader, DepartureMember })
                        Assert.NotNull(runs.TryMarkParticipantDead(member.CharacterId, run.RunId, run.RoomVersion));
                    Assert.Empty(runs.FindExpiredPartyWipes(now));
                    Assert.NotNull(await runs.TryEndPartyWipeAsync(run.RunId, now.AddSeconds(10)));
                    break;
                case ExitPath.EntryAbort:
                    Assert.True(runs.TryAbortEntry(run.RunId, prepared.Revision, prepared.Recipients));
                    break;
                case ExitPath.NewRun:
                    _ = runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal);
                    break;
            }

            weakness.Check();
            Assert.True(weakness.Checks > 1, "The views must have been read while the exit was releasing someone.");
            Assert.Empty(weakness.Failures);
        });
    }

    /// <summary>Reads the watched views each time a departing character's recovery resumes or weakness is set.</summary>
    private sealed class ViewReadingWeakness : IWeaknessDomainService
    {
        private IDungeonRunDomainService? runs;
        private DungeonRunId runId;
        private CharacterId[] characters = [];

        public int Checks { get; private set; }
        public List<Exception> Failures { get; } = [];
        public TimeSpan RecoveryInterval => TimeSpan.FromMinutes(1);

        public void Watch(IDungeonRunDomainService watched, DungeonRunId watchedRun, params CharacterId[] watchedCharacters) =>
            (runs, runId, characters) = (watched, watchedRun, watchedCharacters);

        public void Check()
        {
            if (runs is null)
                return;
            Checks++;
            try
            {
                foreach (var character in characters)
                {
                    if (runs.Find(character) is not { } view)
                        continue;
                    Assert.Contains(view.Participants, participant => participant.CharacterId == character);
                    Assert.Single(view.Participants, participant => participant.IsLeader);
                }
                if (runs.Find(runId) is { } shared)
                    Assert.Single(shared.Participants, participant => participant.IsLeader);
            }
            catch (Exception error)
            {
                Failures.Add(error);
            }
        }

        public IWeakness For(CharacterId characterId) => new Weakness(this);
        public IReadOnlyList<CharacterId> AdvanceRecovery() => [];

        private sealed class Weakness(ViewReadingWeakness owner) : IWeakness
        {
            public byte Value => 100;
            public ValueTask<int> RecoveryGoldCost(CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public ValueTask SetWeakness() { owner.Check(); return ValueTask.CompletedTask; }
            public void Recover() => throw new NotSupportedException();
            public void PauseRecovery() { }
            public void ResumeRecovery() => owner.Check();
        }
    }
}
