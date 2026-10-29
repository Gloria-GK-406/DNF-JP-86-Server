using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// Issue #202 characterization: what every way of leaving or ending an ordinary run does to the run,
/// to the party, and to each departing character, pinned on the baseline before the run stops keeping
/// a roster of its own. Assertions read only views, lookups, party snapshots and recorded side effects.
/// </summary>
public sealed partial class DungeonRunDomainServiceTests
{
    private static readonly DungeonRunParticipant DepartureLeader = new(new AccountId(101), new CharacterId(1), 0, true);
    private static readonly DungeonRunParticipant DepartureMember = new(new AccountId(202), new CharacterId(2), 1, false);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Departing_member_leaves_only_their_own_state_and_the_party_keeps_the_run(bool departingIsLeader, bool removed)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var probe = DepartureProbe.ForParty();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            probe.ReadyAll(run);
            var (leaving, staying) = departingIsLeader ? (DepartureLeader, DepartureMember) : (DepartureMember, DepartureLeader);
            Assert.NotNull(probe.Runs.TryMarkParticipantDead(leaving.CharacterId, run.RunId, run.RoomVersion));
            probe.Combat.Clear();

            var change = removed && !departingIsLeader
                ? probe.Parties.RemoveMember(DepartureLeader.CharacterId, leaving.CharacterId)
                : probe.Parties.LeaveParty(leaving.CharacterId);
            Assert.True(change.Succeeded);
            Assert.True(await probe.Runs.EndAsync(leaving.CharacterId, run.RunId));

            Assert.Null(probe.Runs.Find(leaving.CharacterId));
            Assert.Null(probe.Runs.TryClaimSettlement(leaving.CharacterId, run.RunId));
            var current = Assert.IsType<DungeonRunState>(probe.Runs.Find(staying.CharacterId));
            Assert.Equal(run.RunId, current.RunId);
            var only = Assert.Single(current.Participants);
            Assert.Equal(staying.CharacterId, only.CharacterId);
            Assert.True(only.IsLeader);
            Assert.DoesNotContain(leaving.CharacterId, current.ReadyParticipants);
            Assert.DoesNotContain(leaving.CharacterId, current.DeadParticipants);
            probe.AssertDeparted(leaving.CharacterId, run, failureWeakness: true);
            probe.AssertUntouched(staying.CharacterId);
            var party = Assert.IsType<PartyMembershipSnapshot>(probe.Parties.FindByKey(current.PartyKey));
            Assert.Equal(staying.CharacterId, party.LeaderCharacterId);
            Assert.Equal(staying.CharacterId, Assert.Single(party.Members).CharacterId);
        });
    }

    [Fact]
    public async Task Departing_member_after_clear_gets_no_failure_weakness()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var probe = DepartureProbe.ForParty();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            Assert.NotNull(probe.Runs.TryOpenClear(DepartureLeader.CharacterId, run.RunId, run.RoomVersion));

            Assert.True(probe.Parties.LeaveParty(DepartureMember.CharacterId).Succeeded);
            Assert.True(await probe.Runs.EndAsync(DepartureMember.CharacterId, run.RunId));

            probe.AssertDeparted(DepartureMember.CharacterId, run, failureWeakness: false);
        });
    }

    [Fact]
    public void Transferring_leadership_in_a_run_moves_the_leader_and_nobody_departs()
    {
        TestGameplay.Run(() =>
        {
            var probe = DepartureProbe.ForParty();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            probe.Combat.Clear();

            Assert.True(probe.Parties.TransferLeadership(DepartureLeader.CharacterId, DepartureMember.CharacterId).Succeeded);

            foreach (var member in new[] { DepartureLeader, DepartureMember })
            {
                var view = Assert.IsType<DungeonRunState>(probe.Runs.Find(member.CharacterId));
                Assert.Equal(run.RunId, view.RunId);
                Assert.Equal(2, view.Participants.Count);
                Assert.Equal(DepartureMember.CharacterId, Assert.Single(view.Participants, p => p.IsLeader).CharacterId);
                probe.AssertUntouched(member.CharacterId);
            }
        });
    }

    [Fact]
    public async Task Solo_departure_ends_the_whole_run()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var probe = DepartureProbe.Solo();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader], Tutorial, DungeonDifficulty.Normal).Run;

            Assert.True(await probe.Runs.EndAsync(DepartureLeader.CharacterId, run.RunId));

            Assert.Null(probe.Runs.Find(DepartureLeader.CharacterId));
            Assert.Null(probe.Runs.Find(run.RunId));
            probe.AssertDeparted(DepartureLeader.CharacterId, run, failureWeakness: true);
        });
    }

    [Fact]
    public async Task Clear_return_ends_the_run_for_everyone_and_keeps_the_party()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var probe = DepartureProbe.ForParty();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            var now = DateTimeOffset.UnixEpoch;
            Assert.NotNull(probe.Runs.TryOpenClear(DepartureLeader.CharacterId, run.RunId, run.RoomVersion));
            var free = new ClearRewardCardFace(ClearRewardCardKind.Free, [new(0, null)], 1);
            var paid = new ClearRewardCardFace(ClearRewardCardKind.Paid, [new(0, null)], 2);
            foreach (var member in new[] { DepartureLeader, DepartureMember })
            {
                Assert.NotNull(probe.Runs.FixClearRewardCards(member.CharacterId, run.RunId, free, paid, 3));
                Assert.NotNull(probe.Runs.CommitSettlement(probe.Runs.TryClaimSettlement(member.CharacterId, run.RunId)!));
                Assert.NotNull(probe.Runs.TryOpenCardLayout(member.CharacterId, run.RunId));
            }
            probe.Runs.ScheduleClearReturn(DepartureLeader.CharacterId, run.RunId, now);

            Assert.NotNull(await probe.Runs.TryEndClearReturnAsync(run.RunId, now.AddSeconds(60)));

            Assert.Null(probe.Runs.Find(run.RunId));
            foreach (var member in new[] { DepartureLeader, DepartureMember })
                probe.AssertDeparted(member.CharacterId, run, failureWeakness: false);
            probe.AssertPartyKept(run);
        });
    }

    [Fact]
    public async Task Party_wipe_ends_the_run_for_everyone_with_failure_weakness_and_keeps_the_party()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var probe = DepartureProbe.ForParty();
            var run = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;
            probe.ReadyAll(run);
            var now = DateTimeOffset.UnixEpoch;
            Assert.NotNull(probe.Runs.TryMarkParticipantDead(DepartureLeader.CharacterId, run.RunId, run.RoomVersion));
            Assert.NotNull(probe.Runs.TryMarkParticipantDead(DepartureMember.CharacterId, run.RunId, run.RoomVersion));
            Assert.Empty(probe.Runs.FindExpiredPartyWipes(now));

            Assert.NotNull(await probe.Runs.TryEndPartyWipeAsync(run.RunId, now.AddSeconds(10)));

            Assert.Null(probe.Runs.Find(run.RunId));
            foreach (var member in new[] { DepartureLeader, DepartureMember })
                probe.AssertDeparted(member.CharacterId, run, failureWeakness: true);
            probe.AssertPartyKept(run);
        });
    }

    [Fact]
    public void Entry_abort_detaches_everyone_without_lifecycle_or_failure_weakness()
    {
        TestGameplay.Run(() =>
        {
            var probe = DepartureProbe.ForParty();
            var prepared = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal);

            Assert.True(probe.Runs.TryAbortEntry(prepared.Run.RunId, prepared.Revision, prepared.Recipients));

            Assert.Null(probe.Runs.Find(prepared.Run.RunId));
            foreach (var member in new[] { DepartureLeader, DepartureMember })
                probe.AssertDetachedOnly(member.CharacterId, prepared.Run);
            probe.AssertPartyKept(prepared.Run);
        });
    }

    [Fact]
    public void A_new_run_for_the_same_party_detaches_the_old_one_without_lifecycle_or_failure_weakness()
    {
        TestGameplay.Run(() =>
        {
            var probe = DepartureProbe.ForParty();
            var old = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;

            var next = probe.Runs.PreparePartyEntry([DepartureLeader, DepartureMember], Tutorial, DungeonDifficulty.Normal).Run;

            Assert.Null(probe.Runs.Find(old.RunId));
            foreach (var member in new[] { DepartureLeader, DepartureMember })
            {
                Assert.Equal(next.RunId, probe.Runs.Find(member.CharacterId)!.RunId);
                probe.AssertReleasedFrom(member.CharacterId, old);
                Assert.DoesNotContain(probe.Lifecycle.Departures, x => x.CharacterId == member.CharacterId);
                Assert.DoesNotContain("Set", probe.Weakness.Calls(member.CharacterId));
                Assert.Equal(new DungeonParticipationOwner(DungeonParticipationMode.Ordinary, next.RunId.Value),
                    probe.Participation.Find(member.CharacterId));
            }
            probe.AssertPartyKept(next);
        });
    }

    private sealed class DepartureProbe
    {
        private DepartureProbe()
        {
            Parties = new PartyService(
                new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
                TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance, new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
            Runs = new DungeonRunDomainService(
                transactions: null!, parties: Parties, lostLandFever: new LostLandFeverRuleService(),
                weakness: Weakness, creatures: Combat,
                lifecycle: Lifecycle, participation: Participation);
        }

        public PartyService Parties { get; }
        public DungeonRunDomainService Runs { get; }
        public RecordingWeakness Weakness { get; } = new();
        public RecordingCombat Combat { get; } = new();
        public RecordingLifecycle Lifecycle { get; } = new();
        public RecordingParticipation Participation { get; } = new();

        /// <summary>The leader and the member already in one real party, leader first.</summary>
        public static DepartureProbe ForParty()
        {
            var probe = new DepartureProbe();
            var scope = new PartyDirectoryScope(11, 1, 1);
            var leader = new PartyCharacterSnapshot(DepartureLeader.AccountId, DepartureLeader.CharacterId, 1, scope);
            var member = new PartyCharacterSnapshot(DepartureMember.AccountId, DepartureMember.CharacterId, 2, scope);
            Assert.True(probe.Parties.RequestInvite(leader, member, new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(probe.Parties.AcceptInvite(member, leader).Succeeded);
            return probe;
        }

        /// <summary>Nobody in a real party: a solo entry runs in the leader's virtual party.</summary>
        public static DepartureProbe Solo() => new();

        public void ReadyAll(DungeonRunState run)
        {
            foreach (var participant in run.Participants)
                Assert.NotNull(Runs.TryMarkParticipantReady(participant.CharacterId, run.RunId, run.RoomVersion));
        }

        public void AssertDeparted(CharacterId character, DungeonRunState run, bool failureWeakness)
        {
            AssertDetached(character, run);
            Assert.Equal([new DungeonParticipantLeft(character, run.RunId, run.Layout.DungeonId)],
                Lifecycle.Departures.Where(x => x.CharacterId == character));
            Assert.Equal(failureWeakness ? 1 : 0, Weakness.Calls(character).Count(x => x == "Set"));
        }

        public void AssertDetachedOnly(CharacterId character, DungeonRunState run)
        {
            AssertDetached(character, run);
            Assert.DoesNotContain(Lifecycle.Departures, x => x.CharacterId == character);
            Assert.DoesNotContain("Set", Weakness.Calls(character));
        }

        public void AssertUntouched(CharacterId character)
        {
            Assert.DoesNotContain("Resume", Weakness.Calls(character));
            Assert.DoesNotContain("Set", Weakness.Calls(character));
            Assert.DoesNotContain((character, false), Combat.Calls);
            Assert.DoesNotContain(Lifecycle.Departures, x => x.CharacterId == character);
            Assert.NotNull(Participation.Find(character));
        }

        public void AssertPartyKept(DungeonRunState run)
        {
            var party = Assert.IsType<PartyMembershipSnapshot>(Parties.FindByKey(run.PartyKey));
            Assert.Equal(DepartureLeader.CharacterId, party.LeaderCharacterId);
            Assert.Equal([DepartureLeader.CharacterId, DepartureMember.CharacterId],
                party.Members.Select(x => x.CharacterId).OrderBy(x => x.Value));
        }

        private void AssertDetached(CharacterId character, DungeonRunState run)
        {
            Assert.Null(Runs.Find(character));
            Assert.Null(Participation.Find(character));
            AssertReleasedFrom(character, run);
        }

        public void AssertReleasedFrom(CharacterId character, DungeonRunState run)
        {
            Assert.Equal(1, Participation.Releases.Count(x => x == (character, new DungeonParticipationOwner(DungeonParticipationMode.Ordinary, run.RunId.Value))));
            Assert.Contains("Resume", Weakness.Calls(character));
            Assert.Equal(1, Combat.Calls.Count(x => x == (character, false)));
        }
    }

    private sealed class RecordingWeakness : IWeaknessDomainService
    {
        private readonly Dictionary<CharacterId, List<string>> calls = [];
        public IReadOnlyList<string> Calls(CharacterId character) => calls.GetValueOrDefault(character) ?? [];
        public IWeakness For(CharacterId characterId)
        {
            if (!calls.TryGetValue(characterId, out var list)) calls.Add(characterId, list = []);
            return new Weakness(list);
        }
        public TimeSpan RecoveryInterval => TimeSpan.FromMinutes(1);
        public IReadOnlyList<CharacterId> AdvanceRecovery() => [];

        private sealed class Weakness(List<string> calls) : IWeakness
        {
            public byte Value => 0;
            public ValueTask<int> RecoveryGoldCost(CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public ValueTask SetWeakness() { calls.Add("Set"); return ValueTask.CompletedTask; }
            public void Recover() => throw new NotSupportedException();
            public void PauseRecovery() => calls.Add("Pause");
            public void ResumeRecovery() => calls.Add("Resume");
        }
    }

    private sealed class RecordingCombat : ICreatureRuntimeDomainService
    {
        public List<(CharacterId Character, bool Combat)> Calls { get; } = [];
        public void Clear() => Calls.Clear();
        public void ChangeCombat(CharacterId characterId, bool combat) => Calls.Add((characterId, combat));
        public ValueTask<CreatureTransformationReceipt?> HatchAsync(SelectedSessionIdentity session, BusinessSlot eggSlot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Enter(SelectedSessionIdentity session) => throw new NotSupportedException();
        public void DiscardExact(SelectedSessionIdentity session) => throw new NotSupportedException();
        public void QueueRoomExperience(SelectedSessionIdentity session, DungeonRunId runId, MazeCell cell, int amount) => throw new NotSupportedException();
        public IReadOnlyList<CreaturePendingProjection> DrainProjections() => throw new NotSupportedException();
        public bool IsEquipped(SelectedSessionIdentity session, int itemUid) => throw new NotSupportedException();
        public ValueTask LeaveAsync(SelectedSessionIdentity session, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IReadOnlyList<SelectedSessionIdentity> DueSessions() => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<CreatureRuntimeChange>> AdvanceAsync(SelectedSessionIdentity session, CancellationToken cancellationToken, bool? combat = null) => throw new NotSupportedException();
        public ValueTask<CreatureRuntimeChange?> GainExperienceAsync(SelectedSessionIdentity session, int amount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CreatureRuntimeChange?> GainRoomExperienceAsync(SelectedSessionIdentity session, DungeonRunId runId, MazeCell cell, int amount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Invalidate(CharacterId characterId) => throw new NotSupportedException();
    }

    private sealed class RecordingLifecycle : IDungeonLifecycleDomainService
    {
        public List<DungeonParticipantLeft> Departures { get; } = [];
        public ValueTask RecordDepartureAsync(DungeonParticipantLeft fact, CancellationToken cancellationToken = default)
        {
            Departures.Add(fact);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingParticipation : IDungeonParticipationDomainService
    {
        private readonly Dictionary<CharacterId, DungeonParticipationOwner> members = [];
        public List<(CharacterId Character, DungeonParticipationOwner Owner)> Releases { get; } = [];
        public DungeonParticipationOwner? Find(CharacterId character) => members.GetValueOrDefault(character);
        public bool TryClaim(IReadOnlyList<CharacterId> characters, DungeonParticipationOwner owner)
        {
            foreach (var character in characters) members[character] = owner;
            return true;
        }
        public void Release(CharacterId character, DungeonParticipationOwner expected) => ReleaseCommitted(character, expected);
        public void ReleaseCommitted(CharacterId character, DungeonParticipationOwner expected)
        {
            Releases.Add((character, expected));
            if (members.GetValueOrDefault(character) == expected) members.Remove(character);
        }
    }
}
