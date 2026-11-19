using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Raids;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Raids;

public sealed class AntonRaidRecruitmentTests
{
    [Fact]
    public void Only_the_current_leader_can_approve_a_live_application()
    {
        TestGameplay.Run(() =>
        {
            var fixture = new Fixture();
            var leader = fixture.Admit(1);
            var applicant = fixture.Admit(2);
            var observer = fixture.Admit(3);
            var raid = fixture.Raids.Create(leader, "test"u8.ToArray());

            Assert.True(raid.Succeeded);
            Assert.Equal(raid.Number, fixture.Raids.CheckApplicantTarget(applicant.Session, leader.Session));
            Assert.NotNull(fixture.Raids.Request(applicant, leader));
            Assert.Null(fixture.Raids.Request(applicant, leader));
            Assert.False(fixture.Raids.Respond(observer.Session, applicant.Session, true).Succeeded);
            Assert.True(fixture.Raids.Respond(leader.Session, applicant.Session, false).Succeeded);
            Assert.Single(fixture.Raids.Find(200, raid.Number)!.Members);

            Assert.NotNull(fixture.Raids.Request(applicant, leader));
            Assert.True(fixture.Raids.Respond(leader.Session, applicant.Session, true).Succeeded);
            Assert.False(fixture.Raids.Respond(leader.Session, applicant.Session, true).Succeeded);
            Assert.Equal([leader.Session, applicant.Session],
                fixture.Raids.Find(200, raid.Number)!.Members.Select(member => member.Session));
        });
    }

    [Fact]
    public void Cross_channel_and_expired_applications_do_not_add_members()
    {
        TestGameplay.Run(() =>
        {
            var fixture = new Fixture();
            var leader = fixture.Admit(1);
            var applicant = fixture.Admit(2);
            var outsider = fixture.Admit(3, 201);
            var raid = fixture.Raids.Create(leader, "test"u8.ToArray());

            Assert.Null(fixture.Raids.CheckApplicantTarget(outsider.Session, leader.Session));
            Assert.Null(fixture.Raids.Request(outsider, leader));
            Assert.NotNull(fixture.Raids.Request(applicant, leader));
            fixture.Clock.Now += TimeSpan.FromMinutes(1);
            Assert.False(fixture.Raids.Respond(leader.Session, applicant.Session, true).Succeeded);
            Assert.Single(fixture.Raids.Find(200, raid.Number)!.Members);
        });
    }

    [Fact]
    public void Recruitment_squads_preserve_capacity_and_authority_without_a_regular_party()
    {
        TestGameplay.Run(() =>
        {
            var fixture = new Fixture();
            var leader = fixture.Admit(1);
            var raid = fixture.Raids.Create(leader, "test"u8.ToArray());
            var members = new List<AntonRaidMember>();
            for (var id = 2; id <= 6; id++)
            {
                var applicant = fixture.Admit(id);
                members.Add(applicant);
                Assert.NotNull(fixture.Raids.Request(applicant, leader));
                Assert.True(fixture.Raids.Respond(leader.Session, applicant.Session, true).Succeeded);
            }

            Assert.False(fixture.Raids.Assign(members[0].Session, members[1].Session.WireUserId, 1).Succeeded);
            foreach (var member in members.Take(4))
                Assert.True(fixture.Raids.Assign(leader.Session, member.Session.WireUserId, 1).Succeeded);
            Assert.False(fixture.Raids.Assign(leader.Session, members[4].Session.WireUserId, 1).Succeeded);
            Assert.True(fixture.Raids.Assign(members[4].Session, members[4].Session.WireUserId, 2).Succeeded);

            var leave = fixture.Raids.Disconnected(leader.Session);
            Assert.Equal(leader.Session, leave!.Raid!.Leader.Session);
            Assert.NotNull(leave.Raid.Leader.RejoinUntil);
            fixture.Clock.Now += TimeSpan.FromMinutes(5);
            var expired = fixture.Raids.ExpireRejoin(Assert.Single(fixture.Raids.RejoinExpirations()));
            Assert.Equal(members[0].Session, expired!.Raid!.Leader.Session);
            Assert.Equal(5, fixture.Raids.Find(200, raid.Number)!.Members.Count);
        });
    }

    [Fact]
    public void A_replacement_session_must_match_the_reserved_raid_and_lives_past_the_old_expiry()
    {
        TestGameplay.Run(() =>
        {
            var fixture = new Fixture();
            var leader = fixture.Admit(1);
            var raid = fixture.Raids.Create(leader, "test"u8.ToArray());
            fixture.Raids.Disconnected(leader.Session);
            var expiry = Assert.Single(fixture.Raids.RejoinExpirations());
            fixture.End(leader.Session);
            var wrongChannel = fixture.Admit(1, 201, "alternate");
            Assert.Equal(raid.Number, fixture.Raids.RejoinOffer(wrongChannel.Session)?.Number);
            Assert.False(fixture.Raids.Rejoin(wrongChannel.Session, raid.Number).Succeeded);
            fixture.End(wrongChannel.Session);

            var returning = fixture.Admit(1, suffix: "returning");
            Assert.False(fixture.Raids.Rejoin(returning.Session, raid.Number + 1).Succeeded);
            Assert.True(fixture.Raids.Rejoin(returning.Session, raid.Number).Succeeded);
            fixture.Clock.Now += TimeSpan.FromMinutes(5);
            Assert.Null(fixture.Raids.ExpireRejoin(expiry));
            Assert.Null(fixture.Raids.Disconnected(leader.Session));
            Assert.Equal(returning.Session, fixture.Raids.Find(200, raid.Number)!.Leader.Session);
        });
    }

    [Fact]
    public async Task Squad_preparation_requires_the_frozen_members_and_publishes_attack_once_after_payment_commits()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture();
            var leader = fixture.Admit(1);
            var partner = fixture.Admit(2);
            var raid = fixture.Raids.Create(leader, "test"u8.ToArray());
            Assert.NotNull(fixture.Raids.Request(partner, leader));
            Assert.True(fixture.Raids.Respond(leader.Session, partner.Session, true).Succeeded);
            Assert.True(fixture.Raids.Assign(leader.Session, leader.Session.WireUserId, 6).Succeeded);
            Assert.True(fixture.Raids.Assign(partner.Session, partner.Session.WireUserId, 6).Succeeded);
            Assert.Equal(PartyKeyKind.Virtual, fixture.Parties.KeyOf(leader.Session.CharacterId).Kind);
            await using (var refused = fixture.Transactions.Begin())
            {
                Assert.False((await fixture.Raids.StartAsync(partner.Session, default)).Started);
                Assert.False((await fixture.Raids.StartAsync(leader.Session, default)).Started);
            }

            fixture.TicketAvailable = true;
            await using (var start = fixture.Transactions.Begin())
            {
                Assert.True((await fixture.Raids.StartAsync(leader.Session, default)).Started);
                Assert.Equal(AntonRaidStage.Recruiting, fixture.Raids.Find(200, raid.Number)!.Stage);
                await start.CommitAsync(default);
            }
            var canceledDeadline = Assert.Single(fixture.Raids.PreparationDeadlines());
            Assert.False(fixture.Raids.Assign(partner.Session, partner.Session.WireUserId, 7).Succeeded);
            Assert.False(fixture.Raids.PrepareSquad(partner.Session, AntonRaidSquadSignal.Settings).Succeeded);
            Assert.False(fixture.Raids.PrepareSquad(partner.Session, AntonRaidSquadSignal.Invite, leader.Session).Succeeded);
            Assert.False(fixture.Raids.PrepareSquad(leader.Session, AntonRaidSquadSignal.Accept, partner.Session).Succeeded);
            Assert.True(fixture.Raids.PrepareSquad(partner.Session, AntonRaidSquadSignal.Accept, leader.Session).Succeeded);
            var key = fixture.Parties.KeyOf(leader.Session.CharacterId);
            Assert.Equal(key, fixture.Parties.KeyOf(partner.Session.CharacterId));
            Assert.Equal(leader.Session.CharacterId, fixture.Parties.FindByKey(key)!.LeaderCharacterId);

            Assert.Equal(AntonRaidStage.Recruiting, fixture.Raids.CancelPreparationFor(partner.Session).Change!.Raid!.Stage);
            Assert.Equal(PartyKeyKind.Virtual, fixture.Parties.KeyOf(partner.Session.CharacterId).Kind);
            Assert.Null((await fixture.Raids.FinishPreparationAsync(canceledDeadline, default)).AttackDeadline);
            await using (var restarted = fixture.Transactions.Begin())
            {
                Assert.True((await fixture.Raids.StartAsync(leader.Session, default)).Started);
                await restarted.CommitAsync(default);
            }
            var deadline = Assert.Single(fixture.Raids.PreparationDeadlines());
            Assert.NotEqual(canceledDeadline.Generation, deadline.Generation);
            Assert.True(fixture.Raids.PrepareSquad(leader.Session, AntonRaidSquadSignal.Settings).Succeeded);
            Assert.Equal(key, fixture.Parties.KeyOf(leader.Session.CharacterId));
            fixture.Clock.Now = deadline.At;
            await using (var failed = fixture.Transactions.Begin())
            {
                Assert.NotNull((await fixture.Raids.FinishPreparationAsync(deadline, default)).AttackDeadline);
            }
            Assert.Equal(AntonRaidStage.Preparing, fixture.Raids.Find(200, raid.Number)!.Stage);
            await using (var paid = fixture.Transactions.Begin())
            {
                Assert.NotNull((await fixture.Raids.FinishPreparationAsync(deadline, default)).AttackDeadline);
                await paid.CommitAsync(default);
            }
            Assert.Equal(AntonRaidStage.Attacking, fixture.Raids.Find(200, raid.Number)!.Stage);
            Assert.Null((await fixture.Raids.FinishPreparationAsync(deadline, default)).AttackDeadline);
            Assert.True(fixture.Raids.PrepareSquad(leader.Session, AntonRaidSquadSignal.Settings).Succeeded);
            Assert.True(fixture.Raids.LeaveSquad(partner.Session).Succeeded);
            Assert.Equal(PartyKeyKind.Virtual, fixture.Parties.KeyOf(partner.Session.CharacterId).Kind);
            Assert.True(fixture.Raids.MoveFromPartyRequest(leader.Session, partner.Session, AntonRaidSquadSignal.Invite).Succeeded);
            Assert.Equal(key, fixture.Parties.KeyOf(partner.Session.CharacterId));
            Assert.Equal(2, fixture.MaterialAttempts);
        });
    }

    private sealed class Fixture
    {
        private readonly SelectedSessionDomainService sessions;
        private readonly OnlineStateRegistry registry;
        private readonly OnlineCharacterRuntimeDomainService runtime;

        public Fixture()
        {
            sessions = TestSessions.Directory(out registry);
            var locations = new OnlineCharacterLocationDomainService();
            runtime = new OnlineCharacterRuntimeDomainService(Clock, locations);
            Parties = TestParties.Create();
            var item = new ItemReference(new object(), default, 0, 1, new ItemSnapshot { ItemId = 10096296, Count = 1 });
            var container = TestRequired.Absent<IBusinessItemContainer>((method, _) => method.Name == "GetAllAsync"
                ? ValueTask.FromResult<IReadOnlyList<ItemReference>>(TicketAvailable ? [item] : []) : null);
            var store = TestRequired.Absent<IBusinessInventoryStore>((method, args) =>
                method.Name == "FindContainer" && Equals(args![0], BusinessInventorySpace.Material) ? container : null);
            var inventory = TestRequired.Absent<ICharacterBusinessInventoryDomainService>((method, _) =>
                method.Name == "GetStore" ? store : null);
            var materialRules = TestRequired.Absent<IMaterialConsumptionRuleService>((method, _) =>
                method.Name == "IsConsumable" ? true : null);
            var materials = new TestMaterials(() => MaterialAttempts++);
            Raids = new AntonRaidDomainService(sessions, locations, Parties, ChannelEntryOptions.Legacy, Clock,
                Parties, new TestContent(), materials, inventory, materialRules, TestRequired.Absent<IDungeonDeadlineWake>(), Transactions,
                TestRequired.Absent<IChannelEntryDomainService>(), TestRequired.Absent<IDungeonAdmissionDomainService>(),
                TestRequired.Absent<IDungeonVisitDomainService>(), TestRequired.Absent<IDungeonStructureRuleService>(),
                TestRequired.Absent<IDungeonRunQueryDomainService>(), TestRequired.Absent<IEventService>());
        }

        public TestClock Clock { get; } = new();
        public DomainTransactionScope Transactions { get; } = TestRequired.TransactionScope();
        public Dfo.Server.Application.Parties.PartyService Parties { get; }
        public AntonRaidDomainService Raids { get; }
        public bool TicketAvailable { get; set; }
        public int MaterialAttempts { get; private set; }

        public AntonRaidMember Admit(int id, int channel = 200, string suffix = "")
        {
            var session = sessions.InWorld(registry, new AccountId(id), new CharacterId(id), $"raid-{id}-{suffix}", 10000 + channel);
            runtime.Begin(session, new TownAreaKey(channel, 19, 2), new CharacterPositionState(19, 2, 10, 20, 0), true, 100);
            return new(session, new byte[] { (byte)id }, 0, 0, 0);
        }

        public void End(SelectedSessionIdentity session)
        {
            runtime.EndExact(session);
            sessions.ReleaseExact(session);
        }
    }

    private sealed class TestContent : IAntonRaidContentSource
    {
        public AntonRaidRules Load() => new(2, 20, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5),
            10096296, 1, 10099897, 1, [TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(40)])
        {
            Phases = [new([], []), new([], [])],
        };
    }

    private sealed class TestMaterials(Action consume) : IDungeonEntryMaterialsDomainService, IDungeonEntryMaterialPlan
    {
        public ValueTask<DungeonEntryMaterialPreparation> PrepareAsync(IReadOnlyList<DungeonRunParticipant> participants,
            IReadOnlyList<Dfo.Server.Abstractions.Content.DungeonRequiredItem> requirements, CancellationToken ct)
        {
            Assert.Equal(2, participants.Count);
            Assert.False(Assert.Single(requirements).AllowSharedMaterial);
            return ValueTask.FromResult(new DungeonEntryMaterialPreparation(this));
        }

        public ValueTask<IReadOnlyList<DungeonEntryInventoryChange>> ConsumeAsync(CancellationToken cancellationToken)
        {
            consume();
            return ValueTask.FromResult<IReadOnlyList<DungeonEntryInventoryChange>>([]);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
