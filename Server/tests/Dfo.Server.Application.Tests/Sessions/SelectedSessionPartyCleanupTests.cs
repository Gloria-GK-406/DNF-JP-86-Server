using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class SelectedSessionPartyCleanupTests
{
    [Fact]
    public async Task Member_cleanup_clears_the_departing_member_and_refreshes_survivors()
    {
        var fixture = new Fixture();
        var leader = fixture.Admit(1);
        var member = fixture.Admit(2);
        var observer = fixture.Admit(3);
        fixture.Form(leader, member);

        var plan = await fixture.Cleanup.CleanupAsync(
            member, includeOwner: false, CancellationToken.None);

        Assert.Equal(
            [member.CharacterId],
            plan.PartyNotifications.ClearAudience.ToArray());
        var survivors = Assert.IsType<Dfo.Server.Data.Parties.PartySnapshot>(
            plan.PartyNotifications.Snapshot);
        Assert.Equal(
            [leader.WireUserId],
            survivors.Members.Select(value => value.WireUserId));
        Assert.Equal(
            new PartyDirectoryChange(new PartyDirectoryScope(11, 1, 2), new PartyDirectoryScope(11, 1, 2)),
            plan.PartyNotifications.DirectoryChange);
        Assert.True(fixture.Parties.FindParty(member).IsVirtual);
        Assert.False(fixture.Parties.FindParty(leader).IsVirtual);
    }

    [Fact]
    public async Task Leader_cleanup_promotes_the_next_member_and_clears_only_the_owner()
    {
        var fixture = new Fixture();
        var leader = fixture.Admit(1);
        var member = fixture.Admit(2);
        var observer = fixture.Admit(3);
        fixture.Form(leader, member);

        var plan = await fixture.Cleanup.CleanupAsync(
            leader, includeOwner: false, CancellationToken.None);

        var survivors = Assert.IsType<Dfo.Server.Data.Parties.PartySnapshot>(
            plan.PartyNotifications.Snapshot);
        Assert.Equal(member.WireUserId, survivors.LeaderWireUserId);
        Assert.Equal(member.CharacterId, Assert.Single(survivors.Members).CharacterId);
        Assert.Equal(
            [leader.CharacterId],
            plan.PartyNotifications.ClearAudience.ToArray());
        Assert.Equal(
            new PartyDirectoryChange(
                new PartyDirectoryScope(11, 1, 2),
                new PartyDirectoryScope(11, 1, 2)),
            plan.PartyNotifications.DirectoryChange);
        Assert.True(fixture.Parties.FindParty(leader).IsVirtual);
        Assert.False(fixture.Parties.FindParty(member).IsVirtual);
    }

    [Fact]
    public async Task Late_old_session_cleanup_cannot_remove_a_replacement_party()
    {
        var fixture = new Fixture();
        var oldLeader = fixture.Admit(1);
        var oldMember = fixture.Admit(2);
        fixture.Form(oldLeader, oldMember);
        await fixture.Cleanup.CleanupAsync(
            oldLeader, includeOwner: false, CancellationToken.None);

        var replacement = fixture.Admit(1, generation: 2, connection: "replacement");
        var replacementMember = fixture.Admit(3);
        fixture.Form(replacement, replacementMember);

        var stalePlan = await fixture.Cleanup.CleanupAsync(
            oldLeader, includeOwner: false, CancellationToken.None);

        Assert.Empty(stalePlan.PartyNotifications.ClearAudience);
        Assert.Null(stalePlan.PartyNotifications.Snapshot);
        Assert.False(fixture.Parties.FindParty(replacement).IsVirtual);
        Assert.False(fixture.Parties.FindParty(replacementMember).IsVirtual);
    }

    [Fact]
    public async Task Later_cleanup_failure_preserves_the_already_resolved_party_projection()
    {
        var fixture = new Fixture(failAfterPartyCleanup: true);
        var leader = fixture.Admit(1);
        var member = fixture.Admit(2);
        fixture.Form(leader, member);

        var failure = await Assert.ThrowsAsync<SelectedSessionCleanupFailure>(async () =>
            await fixture.Cleanup.CleanupAsync(
                member, includeOwner: false, CancellationToken.None));

        Assert.Equal(
            [member.CharacterId],
            failure.NotificationPlan.PartyNotifications.ClearAudience.ToArray());
        Assert.NotNull(failure.NotificationPlan.PartyNotifications.Snapshot);
    }

    [Fact]
    public async Task Cleanup_notifies_only_town_peers_on_the_same_listener()
    {
        var fixture = new Fixture();
        var owner = fixture.Admit(1);
        var sameListener = fixture.Admit(2);
        _ = fixture.Admit(3, listenerPort: 10012);

        var plan = await fixture.Cleanup.CleanupAsync(
            owner, includeOwner: false, CancellationToken.None);

        Assert.Equal(
            [sameListener.CharacterId],
            plan.PeerAudience.Select(value => value.CharacterId));
    }

    private sealed class Fixture
    {
        private readonly SelectedSessionDomainService sessions = new();
        private readonly SessionPresenceDomainService presence = new();

        public Fixture(bool failAfterPartyCleanup = false)
        {
            Parties = new PartyServiceTestAdapter(
                sessions,
                presence,
                new StubPartyMemberStatusDomainService(),
                new PartyModeRuleService(),
                TimeProvider.System);
            Cleanup = new SelectedSessionCleanupService(
                sessions,
                new SelectedSessionRuntimeDirectory(),
                new EmptyInventoryLease(),
                new CharacterContainerDomainService(),
                new CharacterItemCooldownDomainService(TimeProvider.System),
                failAfterPartyCleanup
                    ? new ThrowingActiveCharacters()
                    : new PassiveActiveCharacters(),
                presence,
                new ExpertStoreDomainService(),
                new PvpLobbyDomainService(),
                [],
                new CharacterPositionDomainService(),
                Parties.Service,
                new DungeonRunDomainService(),
                []);
        }

        public PartyServiceTestAdapter Parties { get; }

        public SelectedSessionCleanupService Cleanup { get; }

        public SelectedSessionIdentity Admit(
            int value,
            long generation = 1,
            string? connection = null,
            int listenerPort = 10011)
        {
            var session = SelectedSessionIdentity.Create(
                new AccountId(value),
                new CharacterId(value),
                new NetworkConnectionId(connection ?? $"party-{value}"),
                generation,
                listenerPort);
            sessions.Select(session);
            presence.Admit(new SessionPresence(
                session.CharacterId, listenerPort, 1, 2, true, session,
                ChannelId: listenerPort - 10_000));
            return session;
        }

        public void Form(SelectedSessionIdentity leader, SelectedSessionIdentity member)
        {
            Assert.True(Parties.RequestInvite(
                leader,
                member.WireUserId,
                new Dfo.Server.Data.Parties.PartyInviteDisplayState(64, 188, 0, 0)).Succeeded);
            Assert.True(Parties.AcceptInvite(member, leader.WireUserId).Succeeded);
        }
    }

    private sealed class EmptyInventoryLease : IEntryInventoryLeaseRegistry
    {
        public EntryInventoryLeaseSnapshot? Find(AccountId accountId) => null;

        public bool TryMarkDirtyExact(
            SelectedSessionIdentity session,
            CharacterItemLeaseState inventory) => false;

        public void Replace(
            SelectedSessionIdentity session,
            CharacterInventoryEntryState inventory)
        {
        }

        public bool ReleaseExact(SelectedSessionIdentity session) => false;
    }

    private sealed class PassiveActiveCharacters : IActiveCharacterDomainService
    {
        public void Publish(SelectedSessionIdentity session)
        {
        }

        public bool ClearExact(SelectedSessionIdentity session) => false;
    }

    private sealed class ThrowingActiveCharacters : IActiveCharacterDomainService
    {
        public void Publish(SelectedSessionIdentity session)
        {
        }

        public bool ClearExact(SelectedSessionIdentity session) =>
            throw new InvalidOperationException("fault after party cleanup");
    }
}
