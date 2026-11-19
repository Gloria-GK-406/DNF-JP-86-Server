using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Raids;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
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
            Assert.Equal(members[0].Session, leave!.Raid!.Leader.Session);
            Assert.Equal(5, fixture.Raids.Find(200, raid.Number)!.Members.Count);
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
            Raids = new AntonRaidDomainService(sessions, locations, TestParties.Create(), ChannelEntryOptions.Legacy, Clock);
        }

        public TestClock Clock { get; } = new();
        public AntonRaidDomainService Raids { get; }

        public AntonRaidMember Admit(int id, int channel = 200)
        {
            var session = sessions.InWorld(registry, new AccountId(id), new CharacterId(id), $"raid-{id}", 10000 + channel);
            runtime.Begin(session, new TownAreaKey(channel, 19, 2), new CharacterPositionState(19, 2, 10, 20, 0), true, 100);
            return new(session, new byte[] { (byte)id }, 0, 0, 0);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
