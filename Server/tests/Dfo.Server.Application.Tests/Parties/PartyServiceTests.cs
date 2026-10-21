using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Parties;

public sealed class PartyServiceTests
{
    [Fact]
    public void Applicant_can_join_the_surviving_leader_only_after_that_leader_approves()
    {
        var service = new PartyService(new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        var oldLeader = Character(2, scope); var survivor = Character(1, scope); var applicant = Character(8, scope);
        Assert.True(service.RequestInvite(oldLeader, survivor, Profile(1)).Succeeded);
        Assert.True(service.AcceptInvite(survivor, oldLeader).Succeeded);
        Assert.True(service.LeaveParty(oldLeader.CharacterId).Succeeded);
        Assert.True(service.CreateOrUpdateSettings(survivor, DirectorySettings(PartyCategory.Normal, 0),
            new PartyTitle("kept"u8)).Succeeded);
        var before = service.FindParty(survivor);
        var requested = service.RequestInvite(applicant, survivor, Profile(80));
        Assert.True(requested.Succeeded, requested.Failure.ToString());
        Assert.Equal(survivor.CharacterId, requested.Notifications.Invitation!.TargetCharacterId);
        Assert.Equal(applicant.WireUserId, requested.Notifications.Invitation.InviterWireUserId);
        Assert.True(service.FindParty(applicant).IsVirtual);
        Assert.Single(service.FindParty(survivor).Members);
        var accepted = service.AcceptInvite(survivor, applicant);
        Assert.True(accepted.Succeeded, accepted.Failure.ToString());
        var after = service.FindParty(survivor);
        Assert.Equal(before.Key, after.Key); Assert.Equal(before.LeadershipVersion, after.LeadershipVersion);
        Assert.Equal(before.Settings, after.Settings); Assert.Equal(before.Title, after.Title);
        Assert.Equal(survivor.WireUserId, after.LeaderWireUserId);
        Assert.Equal(after.Key, service.FindParty(applicant).Key);
        Assert.Equal(new[] { survivor.CharacterId, applicant.CharacterId }.OrderBy(x => x.Value),
            after.Members.Select(x => x.CharacterId).OrderBy(x => x.Value));
        Assert.Equal(before.LeaderMember.Slot, after.LeaderMember.Slot);
        Assert.Equal(applicant.CharacterId, Assert.Single(accepted.Notifications.ClearAudience));
        Assert.Equal(new PartyAcceptanceNotification(applicant.CharacterId, survivor.CharacterId,
            applicant.WireUserId, survivor.WireUserId, applicant.CharacterId), accepted.Notifications.Acceptance);
        Assert.Equal(PartyFailureReason.InviteNotFoundOrStale, service.AcceptInvite(survivor, applicant).Failure);
    }

    [Theory]
    [InlineData(0, PartyFailureReason.NotPartyLeader)]
    [InlineData(1, PartyFailureReason.PartyGenerationChanged)]
    [InlineData(2, PartyFailureReason.TargetAlreadyInParty)]
    [InlineData(3, PartyFailureReason.InviteExpired)]
    [InlineData(4, PartyFailureReason.PartyFull)]
    [InlineData(5, PartyFailureReason.NotInSameTownArea)]
    [InlineData(6, PartyFailureReason.InviteNotFoundOrStale)]
    [InlineData(7, PartyFailureReason.InviteNotFoundOrStale)]
    public void Join_approval_rechecks_current_leader_generation_membership_expiry_capacity_and_town(int change, PartyFailureReason expected)
    {
        var clock = new AdvanceablePartyClock(DateTimeOffset.UtcNow);
        var service = new PartyService(new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            clock, EmptyPartyMemberSessionEndpointSource.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        var leader = Character(1, scope); var member = Character(2, scope); var applicant = Character(8, scope);
        void Admit(PartyCharacterSnapshot owner, PartyCharacterSnapshot newcomer)
        {
            Assert.True(service.RequestInvite(owner, newcomer, Profile(1)).Succeeded);
            Assert.True(service.AcceptInvite(newcomer, owner).Succeeded);
        }
        Admit(leader, member);
        Assert.True(service.RequestInvite(applicant, leader, Profile(1)).Succeeded);
        switch (change)
        {
            case 0: Assert.True(service.TransferLeadership(leader.CharacterId, member.CharacterId).Succeeded); break;
            case 1:
                Assert.True(service.LeaveParty(leader.CharacterId).Succeeded);
                Assert.True(service.LeaveParty(member.CharacterId).Succeeded);
                Assert.True(service.CreateOrUpdateSettings(leader, new PartyModeRuleService().DefaultSettings, PartyTitle.Empty).Succeeded);
                break;
            case 2: Admit(Character(9, scope), applicant); break;
            case 3: clock.Advance(PartyService.InvitationLifetime + TimeSpan.FromTicks(1)); break;
            case 4: Admit(leader, Character(3, scope)); Admit(leader, Character(4, scope)); break;
            case 5: applicant = applicant with { DirectoryScope = new(11, 1, 2) }; break;
            case 6: Assert.True(service.CleanupCharacter(applicant.CharacterId).Succeeded); break;
            case 7: Assert.True(service.CleanupCharacter(leader.CharacterId).Succeeded); break;
        }
        var before = service.FindParty(leader);
        var applicantBefore = service.FindParty(applicant);
        Assert.Equal(expected, service.AcceptInvite(leader, applicant).Failure);
        Assert.Equal(before.Revision, service.FindParty(leader).Revision);
        Assert.Equal(applicantBefore.Key, service.FindParty(applicant).Key);
        Assert.Equal(before.Members.Select(x => x.CharacterId), service.FindParty(leader).Members.Select(x => x.CharacterId));
    }

    [Fact]
    public void Join_application_targets_only_a_leader_and_checks_admission_before_notifying()
    {
        var service = new PartyService(new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        var leader = Character(1, scope); var member = Character(2, scope); var applicant = Character(8, scope);
        Assert.True(service.RequestInvite(leader, member, Profile(1)).Succeeded);
        Assert.True(service.AcceptInvite(member, leader).Succeeded);
        var refusal = service.RequestInvite(applicant, member, Profile(1));
        Assert.Equal(PartyFailureReason.NotPartyLeader, refusal.Failure); Assert.Null(refusal.Notifications.Invitation);
        var settings = new PartyModeRuleService().DefaultSettings.ToWireBytes(); settings[2] = 2;
        Assert.True(service.CreateOrUpdateSettings(leader, new(settings), PartyTitle.Empty).Succeeded);
        refusal = service.RequestInvite(applicant, leader, Profile(1));
        Assert.Equal(PartyFailureReason.PartyFull, refusal.Failure); Assert.Null(refusal.Notifications.Invitation);
        Assert.True(service.FindParty(applicant).IsVirtual);
    }

    [Fact]
    public void Real_and_virtual_party_keys_are_distinct_and_resolve_only_current_members()
    {
        var service = new PartyService(
            new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        var first = Character(1, scope);
        var second = Character(2, scope);
        var loneA = Character(3, scope);
        var loneB = Character(4, scope);
        var defaults = new PartyModeRuleService().DefaultSettings;
        Assert.True(service.CreateOrUpdateSettings(first, defaults, PartyTitle.Empty).Succeeded);
        Assert.True(service.CreateOrUpdateSettings(second, defaults, PartyTitle.Empty).Succeeded);

        var keys = new[]
        {
            service.FindParty(first).Key,
            service.FindParty(second).Key,
            service.FindParty(loneA).Key,
            service.FindParty(loneB).Key,
        };

        Assert.Equal(4, keys.Distinct().Count());
        Assert.Equal(first.CharacterId,
            Assert.Single(service.FindByKey(keys[0])!.Members).CharacterId);
        Assert.Equal(second.CharacterId,
            Assert.Single(service.FindByKey(keys[1])!.Members).CharacterId);
        Assert.Equal(loneA.CharacterId,
            Assert.Single(service.FindByKey(keys[2])!.Members).CharacterId);
        Assert.Equal(loneB.CharacterId,
            Assert.Single(service.FindByKey(keys[3])!.Members).CharacterId);
    }

    [Fact]
    public void Party_key_identifies_current_membership_across_leader_succession()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var successor = fixture.Admit(2, 202);
        fixture.Form(leader, successor);

        var before = fixture.Parties.FindParty(leader);
        Assert.Equal(PartyKeyKind.Real, before.Key.Kind);
        Assert.Equal(
            [leader.CharacterId, successor.CharacterId],
            fixture.Parties.FindByKey(before.Key)!.Members.Select(member => member.CharacterId));

        Assert.True(fixture.Parties.LeavePartyExact(leader).Succeeded);
        var after = fixture.Parties.FindParty(successor);
        Assert.Equal(before.Key, after.Key);
        var current = Assert.IsType<PartyMembershipSnapshot>(fixture.Parties.FindByKey(before.Key));
        Assert.Equal(successor.CharacterId, current.LeaderCharacterId);
        Assert.Equal(successor.CharacterId, Assert.Single(current.Members).CharacterId);
        Assert.Null(fixture.Parties.FindByKey(PartyKey.Virtual(successor.CharacterId)));
        Assert.Equal(leader.CharacterId,
            Assert.Single(fixture.Parties.FindByKey(PartyKey.Virtual(leader.CharacterId))!.Members).CharacterId);
    }

    [Fact]
    public void Dungeon_roster_is_always_nonempty_without_creating_a_real_party()
    {
        IPartyService service = new PartyService(
            new StubPartyMemberStatusDomainService(),
            new PartyModeRuleService(),
            TimeProvider.System,
            EmptyPartyMemberSessionEndpointSource.Instance);
        var character = Character(91, new PartyDirectoryScope(11, 1, 1));

        var roster = service.FindParty(character);

        Assert.True(roster.IsVirtual);
        Assert.Equal(character.WireUserId, roster.LeaderWireUserId);
        var member = Assert.Single(roster.Members);
        Assert.Equal(character.CharacterId, member.CharacterId);
        Assert.Equal(0, member.Slot);
        Assert.True(service.FindParty(character).IsVirtual);
        Assert.Empty(service.Query(new PartyDirectoryQuery(character.DirectoryScope!)));
        Assert.Equal(PartyFailureReason.NotInParty,
            service.LeaveParty(character.CharacterId).Failure);

        Assert.True(service.CreateOrUpdateSettings(
            character,
            new PartyModeRuleService().DefaultSettings,
            PartyTitle.Empty).Succeeded);
        var actual = service.FindParty(character);
        Assert.False(actual.IsVirtual);
        Assert.Equal(character.WireUserId, actual.LeaderWireUserId);
        Assert.Equal(character.CharacterId, Assert.Single(actual.Members).CharacterId);
    }

    [Fact]
    public void Party_snapshot_reaches_an_endpoint_only_through_the_characters_current_session()
    {
        var state = new OnlineStateRegistry(new DomainTransactionScope());
        var onlineUsers = new OnlineUserDomainService(state);
        var sessionEndpoints = new GameSessionEndpointDomainService(state);
        var selectedSessions = new SelectedSessionDomainService(state);
        var connection = new NetworkConnectionContext(
            new NetworkConnectionId("party-session-endpoint"),
            NetworkEndpointKind.Game,
            10011);
        var connected = onlineUsers.RegisterConnection(connection);
        state.Authenticate(connected.OnlineUserId, new AccountId(42));
        var authenticated = onlineUsers.GetRequired(connected.OnlineUserId);
        var endpoint = Endpoint(30_976);
        Assert.True(sessionEndpoints.TryObserve(authenticated.SessionIdentity, endpoint));

        var selected = SelectedSessionIdentity.Create(
            new AccountId(42),
            new CharacterId(4242),
            connection.ConnectionId,
            authenticated.Generation,
            connection.ListenerPort);
        selectedSessions.Select(selected);
        selectedSessions.EnterWorld(selected);
        var parties = new PartyService(
            new StubPartyMemberStatusDomainService(),
            new PartyModeRuleService(),
            TimeProvider.System,
            new PartyMemberSessionEndpointSource(
                selectedSessions, onlineUsers, sessionEndpoints));
        Assert.True(parties.CreateOrUpdateSettings(
            new PartyCharacterSnapshot(
                selected.AccountId,
                selected.CharacterId,
                selected.WireUserId,
                new PartyDirectoryScope(11, 1, 1)),
            new PartyModeRuleService().DefaultSettings,
            PartyTitle.Empty).Succeeded);

        Assert.Equal(
            endpoint,
            Assert.Single(parties.FindParty(new PartyCharacterSnapshot(
                selected.AccountId, selected.CharacterId, selected.WireUserId, null)).Members).Endpoint);

        Assert.True(selectedSessions.ReleaseExact(selected));
        Assert.Null(Assert.Single(parties.FindParty(new PartyCharacterSnapshot(
            selected.AccountId, selected.CharacterId, selected.WireUserId, null)).Members).Endpoint);
        Assert.Equal(endpoint, sessionEndpoints.Find(authenticated.SessionIdentity));
    }

    [Fact]
    public void DirectoryQueryCombinesScopeCategoryAndModeFiltersAndReturnsAllMatches()
    {
        var service = new PartyService(
            new StubPartyMemberStatusDomainService(),
            new PartyModeRuleService(),
            TimeProvider.System,
            EmptyPartyMemberSessionEndpointSource.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        Create(service, 1, scope, DirectorySettings(PartyCategory.Story, 10));
        Create(service, 2, scope, DirectorySettings(PartyCategory.Story, 10));
        Create(service, 3, scope, DirectorySettings(PartyCategory.Cycle, 20));
        Create(service, 4, new PartyDirectoryScope(12, 1, 1), DirectorySettings(PartyCategory.Story, 10));
        Create(service, 5, new PartyDirectoryScope(11, 2, 1), DirectorySettings(PartyCategory.Story, 10));
        Create(service, 6, new PartyDirectoryScope(11, 1, 2), DirectorySettings(PartyCategory.Story, 10));

        Assert.Equal(3, service.Query(new PartyDirectoryQuery(scope)).Count);
        Assert.Equal(
            [1, 2],
            service.Query(new PartyDirectoryQuery(scope, PartyCategory.Story))
                .Select(snapshot => (int)snapshot.LeaderWireUserId));
        Assert.Equal(
            [1, 2],
            service.Query(new PartyDirectoryQuery(scope, ModeTargetRaw: 10))
                .Select(snapshot => (int)snapshot.LeaderWireUserId));
        Assert.Equal(
            [3],
            service.Query(new PartyDirectoryQuery(scope, PartyCategory.Cycle, 20))
                .Select(snapshot => (int)snapshot.LeaderWireUserId));
        Assert.Empty(service.Query(new PartyDirectoryQuery(
            scope,
            PartyCategory.Cycle,
            10)));
    }

    [Fact]
    public void LeaderScopeMigrationAndPrivatePresenceNeverLeaveAStaleDirectoryEntry()
    {
        var service = new PartyService(
            new StubPartyMemberStatusDomainService(),
            new PartyModeRuleService(),
            TimeProvider.System,
            EmptyPartyMemberSessionEndpointSource.Instance);
        var first = new PartyDirectoryScope(11, 1, 1);
        var second = new PartyDirectoryScope(11, 1, 2);
        Create(service, 1, first, DirectorySettings(PartyCategory.Story, 10));
        Create(service, 2, null, DirectorySettings(PartyCategory.Story, 10));

        Assert.Single(service.Query(new PartyDirectoryQuery(first)));
        service.UpdateDirectoryPresence(new CharacterId(1), null);
        Assert.Empty(service.Query(new PartyDirectoryQuery(first)));
        service.UpdateDirectoryPresence(new CharacterId(1), second);
        Assert.Empty(service.Query(new PartyDirectoryQuery(first)));
        Assert.Single(service.Query(new PartyDirectoryQuery(second)));

        service.UpdateDirectoryPresence(new CharacterId(2), first);
        Assert.Single(service.Query(new PartyDirectoryQuery(first)));
        service.UpdateDirectoryPresence(new CharacterId(2), null);
        Assert.Empty(service.Query(new PartyDirectoryQuery(first)));
    }

    [Fact]
    public void PrivateGatesStayOutOfTheDirectoryWhileSharedNormalAndDungeonGatesAreVisible()
    {
        var service = new PartyService(
            new StubPartyMemberStatusDomainService(),
            new PartyModeRuleService(),
            TimeProvider.System,
            EmptyPartyMemberSessionEndpointSource.Instance);
        var presence = new SessionPresenceDomainService(service);
        var first = SelectedSessionIdentity.Create(
            new AccountId(1), new CharacterId(1), new NetworkConnectionId("private-1"), 1, 10011);
        var second = SelectedSessionIdentity.Create(
            new AccountId(2), new CharacterId(2), new NetworkConnectionId("private-2"), 1, 10011);
        var publicScope = new PartyDirectoryScope(11, 1, 1);
        presence.Admit(new SessionPresence(
            first.CharacterId, 10011, 1, 1, true, first,
            SharesTownPresence: false, ChannelId: 11));
        presence.Admit(new SessionPresence(
            second.CharacterId, 10011, 1, 1, true, second,
            SharesTownPresence: false, ChannelId: 11));
        Assert.True(service.CreateOrUpdateSettings(
            Character(1, scope: null),
            new PartyModeRuleService().DefaultSettings,
            PartyTitle.Empty).Succeeded);
        Assert.True(service.CreateOrUpdateSettings(
            Character(2, scope: null),
            new PartyModeRuleService().DefaultSettings,
            PartyTitle.Empty).Succeeded);

        Assert.Empty(service.Query(new PartyDirectoryQuery(publicScope)));

        Assert.True(presence.RelocateExact(
            first, townId: 1, areaId: 1, standingInTown: true,
            sharesTownPresence: true));
        Assert.Equal(
            [first.CharacterId],
            service.Query(new PartyDirectoryQuery(publicScope))
                .Select(snapshot => snapshot.Members[0].CharacterId));

        Assert.True(presence.RelocateExact(
            second, townId: 1, areaId: 1, standingInTown: true,
            sharesTownPresence: true));
        Assert.Equal(
            [first.CharacterId, second.CharacterId],
            service.Query(new PartyDirectoryQuery(publicScope))
                .Select(snapshot => snapshot.Members[0].CharacterId));
    }

    [Fact]
    public void StalePresenceWithdrawCannotRemoveAReplacementCharactersDirectoryEntry()
    {
        var service = new PartyService(
            new StubPartyMemberStatusDomainService(),
            new PartyModeRuleService(),
            TimeProvider.System,
            EmptyPartyMemberSessionEndpointSource.Instance);
        var presence = new SessionPresenceDomainService(service);
        var old = SelectedSessionIdentity.Create(
            new AccountId(1), new CharacterId(1), new NetworkConnectionId("old"), 1, 10011);
        var replacement = old with
        {
            ConnectionId = new NetworkConnectionId("replacement"),
            ConnectionGeneration = 2,
        };
        var scope = new PartyDirectoryScope(11, 1, 1);
        presence.Admit(new SessionPresence(
            old.CharacterId, 10011, 1, 1, true, old, ChannelId: 11));
        Create(service, 1, scope, DirectorySettings(PartyCategory.Story, 10));
        presence.Admit(new SessionPresence(
            replacement.CharacterId, 10011, 1, 1, true, replacement, ChannelId: 11));

        Assert.False(presence.WithdrawExact(old));
        Assert.Single(service.Query(new PartyDirectoryQuery(scope)));

        Assert.True(presence.RelocateExact(
            replacement, townId: 1, areaId: 1, standingInTown: false));
        Assert.Empty(service.Query(new PartyDirectoryQuery(scope)));
    }

    [Fact]
    public void DirectoryQueryReturnsEveryPartyInTheRequestedLogicalScope()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var scope = new PartyDirectoryScope(ChannelId: 11, TownId: 1, AreaId: 1);
        fixture.Parties.UpdateDirectoryPresence(leader.CharacterId, scope);
        Assert.True(fixture.Parties.CreateOrUpdateSettingsExact(
            leader, new PartyModeRuleService().DefaultSettings, PartyTitle.Empty).Succeeded);

        var found = fixture.Parties.Query(new PartyDirectoryQuery(scope));

        var party = Assert.Single(found);
        Assert.Equal(leader.WireUserId, party.LeaderWireUserId);
    }

    [Fact]
    public void ProductionGraphRegistersOneSharedPartyService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPartyMemberStatusDomainService>(
            new StubPartyMemberStatusDomainService());
        new ApplicationModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IPartyService>();
        var second = provider.GetRequiredService<IPartyService>();

        Assert.Same(first, second);
    }

    [Fact]
    public void SettingsMutationsDescribeTheAffectedDirectoryScopes()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var laterObserver = fixture.Admit(5, 505);
        var earlierObserver = fixture.Admit(3, 303);
        var otherArea = fixture.Admit(4, 404);
        var otherTown = fixture.Admit(6, 606);
        var otherChannel = fixture.Admit(7, 707, listenerPort: 10012);
        var notStanding = fixture.Admit(8, 808);
        var notSharing = fixture.Admit(9, 909);
        var stalePresence = fixture.Admit(10, 1001);

        Assert.True(fixture.Presence.RelocateExact(
            otherArea, townId: 1, areaId: 2, standingInTown: true));
        Assert.True(fixture.Presence.RelocateExact(
            otherTown, townId: 2, areaId: 1, standingInTown: true));
        Assert.True(fixture.Presence.RelocateExact(
            notStanding, townId: 1, areaId: 1, standingInTown: false));
        Assert.True(fixture.Presence.RelocateExact(
            notSharing, townId: 1, areaId: 1, standingInTown: true,
            sharesTownPresence: false));
        fixture.ReplaceSelectionOnly(stalePresence, generation: 2);

        var settings = new PartyModeRuleService().DefaultSettings;
        var created = fixture.Parties.CreateOrUpdateSettingsExact(
            leader, settings, PartyTitle.Empty);
        var updated = fixture.Parties.CreateOrUpdateSettingsExact(
            leader, settings, PartyTitle.Empty);

        Assert.True(created.Succeeded);
        Assert.True(updated.Succeeded);
        var scope = new PartyDirectoryScope(11, 1, 1);
        Assert.Equal(new PartyDirectoryChange(null, scope), created.Notifications.DirectoryChange);
        Assert.Equal(new PartyDirectoryChange(scope, scope), updated.Notifications.DirectoryChange);
    }

    [Fact]
    public void AcceptDescribesTheLeadersCurrentDirectoryScope()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var existingMember = fixture.Admit(2, 202);
        fixture.Form(leader, existingMember);
        Assert.True(fixture.Presence.RelocateExact(
            existingMember, townId: 1, areaId: 2, standingInTown: true));

        var firstAreaObserver = fixture.Admit(4, 404);
        var secondAreaObserver = fixture.Admit(5, 505);
        Assert.True(fixture.Presence.RelocateExact(
            secondAreaObserver, townId: 1, areaId: 2, standingInTown: true));
        var invitee = fixture.Admit(3, 303);

        Assert.True(fixture.Parties.RequestInvite(
            leader, invitee.WireUserId, Profile(1)).Succeeded);
        var accepted = fixture.Parties.AcceptInvite(invitee, leader.WireUserId);

        Assert.True(accepted.Succeeded);
        var scope = new PartyDirectoryScope(11, 1, 1);
        Assert.Equal(new PartyDirectoryChange(scope, scope), accepted.Notifications.DirectoryChange);
    }

    [Fact]
    public void LeaveAndLeaderSuccessionDescribeTheAffectedDirectoryScopes()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var member = fixture.Admit(2, 202);
        var townObserver = fixture.Admit(3, 303);
        fixture.Form(leader, member);

        var memberLeave = fixture.Parties.LeavePartyExact(member);

        Assert.True(memberLeave.Succeeded);
        var scope = new PartyDirectoryScope(11, 1, 1);
        Assert.Equal(
            new PartyDirectoryChange(scope, scope),
            memberLeave.Notifications.DirectoryChange);

        fixture.Form(leader, member);
        var leaderLeave = fixture.Parties.LeavePartyExact(leader);

        Assert.True(leaderLeave.Succeeded);
        Assert.Equal(
            new PartyDirectoryChange(scope, scope),
            leaderLeave.Notifications.DirectoryChange);
        Assert.Equal(member.CharacterId,
            Assert.Single(leaderLeave.Notifications.Snapshot!.Members).CharacterId);
        Assert.Equal(member.WireUserId, leaderLeave.Notifications.Snapshot.LeaderWireUserId);
    }

    [Fact]
    public void LeadershipTransferMovesTheDirectoryToTheNewLeadersLatestScope()
    {
        var service = new PartyService(
            new StubPartyMemberStatusDomainService(),
            new PartyModeRuleService(),
            TimeProvider.System,
            EmptyPartyMemberSessionEndpointSource.Instance);
        var firstScope = new PartyDirectoryScope(11, 1, 1);
        var secondScope = new PartyDirectoryScope(11, 1, 2);
        var leader = Character(1, firstScope);
        var successor = Character(2, firstScope);

        Assert.True(service.RequestInvite(leader, successor, Profile(1)).Succeeded);
        Assert.True(service.AcceptInvite(successor, leader).Succeeded);
        service.UpdateDirectoryPresence(successor.CharacterId, secondScope);

        var transfer = service.TransferLeadership(
            leader.CharacterId,
            successor.CharacterId);

        Assert.True(transfer.Succeeded);
        Assert.Equal(
            new PartyDirectoryChange(firstScope, secondScope),
            transfer.Notifications.DirectoryChange);
        Assert.Same(transfer.Notifications.Snapshot, transfer.Notifications.LeadershipTransfer);
        Assert.Empty(service.Query(new PartyDirectoryQuery(firstScope)));
        var moved = Assert.Single(service.Query(new PartyDirectoryQuery(secondScope)));
        Assert.Equal(successor.WireUserId, moved.LeaderWireUserId);
        Assert.Equal(
            [(leader.CharacterId, (byte)0), (successor.CharacterId, (byte)1)],
            moved.Members.Select(member => (member.CharacterId, member.Slot)));
        Assert.Equal(successor.CharacterId, service.FindByKey(moved.Key)!.LeaderCharacterId);
        Assert.Equal(PartyFailureReason.NotPartyLeader,
            service.CreateOrUpdateSettings(
                leader, new PartyModeRuleService().DefaultSettings, PartyTitle.Empty).Failure);
        Assert.True(service.CreateOrUpdateSettings(
            successor with { DirectoryScope = secondScope },
            new PartyModeRuleService().DefaultSettings,
            PartyTitle.Empty).Succeeded);

        var third = Character(3, secondScope);
        Assert.True(service.RequestInvite(
            successor with { DirectoryScope = secondScope }, third, Profile(1)).Succeeded);
        Assert.True(service.AcceptInvite(
            third, successor with { DirectoryScope = secondScope }).Succeeded);
        Assert.Equal((byte)2, service.FindParty(third).Members.Single(
            member => member.CharacterId == third.CharacterId).Slot);

        Assert.True(service.LeaveParty(successor.CharacterId).Succeeded);
        Assert.Single(service.Query(new PartyDirectoryQuery(firstScope)));
        Assert.Empty(service.Query(new PartyDirectoryQuery(secondScope)));
        Assert.Equal(leader.WireUserId, service.FindParty(leader).LeaderWireUserId);
        Assert.Equal((byte)0, service.FindParty(leader).Members.Single(
            member => member.CharacterId == leader.CharacterId).Slot);
        Assert.Equal((byte)2, service.FindParty(third).Members.Single(
            member => member.CharacterId == third.CharacterId).Slot);
    }

    [Fact]
    public void Departing_slot_zero_member_does_not_replace_a_leader_in_another_slot()
    {
        var service = new PartyService(
            new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        var founder = Character(1, scope);
        var leader = Character(2, scope);
        var newcomer = Character(3, scope);
        Assert.True(service.RequestInvite(founder, leader, Profile(1)).Succeeded);
        Assert.True(service.AcceptInvite(leader, founder).Succeeded);
        var before = service.FindParty(founder);
        Assert.True(service.TransferLeadership(founder.CharacterId, leader.CharacterId).Succeeded);

        var departure = service.LeaveParty(founder.CharacterId);

        Assert.True(departure.Succeeded);
        var remaining = service.FindParty(leader);
        Assert.Equal(before.Key, remaining.Key);
        Assert.Equal(before.LeadershipVersion, remaining.LeadershipVersion);
        Assert.Equal(leader.CharacterId, service.FindByKey(before.Key)!.LeaderCharacterId);
        Assert.Equal((byte)1, Assert.Single(remaining.Members).Slot);
        Assert.True(service.RequestInvite(leader, newcomer, Profile(1)).Succeeded);
        Assert.True(service.AcceptInvite(newcomer, leader).Succeeded);
        Assert.Equal((byte)0, service.FindParty(newcomer).Members.Single(
            member => member.CharacterId == newcomer.CharacterId).Slot);
    }

    [Fact]
    public void Only_the_explicit_leader_can_remove_a_nonleader_without_reordering_survivors()
    {
        var service = new PartyService(
            new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        var leader = Character(1, scope);
        var removed = Character(2, scope);
        var survivor = Character(3, scope);
        Assert.True(service.RequestInvite(leader, removed, Profile(1)).Succeeded);
        Assert.True(service.AcceptInvite(removed, leader).Succeeded);
        Assert.True(service.RequestInvite(leader, survivor, Profile(1)).Succeeded);
        Assert.True(service.AcceptInvite(survivor, leader).Succeeded);

        Assert.Equal(
            PartyFailureReason.NotPartyLeader,
            service.RemoveMember(removed.CharacterId, survivor.CharacterId).Failure);
        var result = service.RemoveMember(leader.CharacterId, removed.CharacterId);

        Assert.True(result.Succeeded);
        Assert.Equal([removed.CharacterId], result.Notifications.ClearAudience.ToArray());
        Assert.Equal(
            [(leader.CharacterId, (byte)0), (survivor.CharacterId, (byte)2)],
            result.Notifications.Snapshot!.Members.Select(member =>
                (member.CharacterId, member.Slot)));
        Assert.True(service.FindParty(removed).IsVirtual);
        Assert.False(service.FindParty(survivor).IsVirtual);
    }

    [Fact]
    public void EndpointUpdateRequiresAValidEndpointAndNeverFlowsIntoAReplacementSession()
    {
        var fixture = new PartyFixture();
        var first = fixture.Admit(1, 101);
        var valid = Endpoint(30_976);

        Assert.True(fixture.Parties.ObserveSessionEndpoint(first, valid).Succeeded);
        Assert.Equal(valid, fixture.Parties.FindSessionEndpoint(first));

        var observedLoopback = valid with
        {
            NatType = 0,
            InnerAddress = new SessionIpv4Address(127, 0, 0, 1),
            OuterAddress = new SessionIpv4Address(127, 0, 0, 1),
            Mtu = 1500,
        };
        Assert.True(fixture.Parties.ObserveSessionEndpoint(first, observedLoopback).Succeeded);
        Assert.Equal(observedLoopback, fixture.Parties.FindSessionEndpoint(first));

        var invalid = valid with { Port = 0 };
        Assert.Equal(
            PartyFailureReason.InvalidEndpoint,
            fixture.Parties.ObserveSessionEndpoint(first, invalid).Failure);
        Assert.Equal(observedLoopback, fixture.Parties.FindSessionEndpoint(first));

        var replacement = fixture.Replace(first, generation: 2);
        Assert.Equal(
            PartyFailureReason.CallerNotCurrent,
            fixture.Parties.ObserveSessionEndpoint(first, valid with { Port = 36_624 }).Failure);
        Assert.Null(fixture.Parties.FindSessionEndpoint(replacement));
        Assert.True(fixture.Parties.ObserveSessionEndpoint(
            replacement, valid with { Port = 36_624 }).Succeeded);
        Assert.Equal(
            valid with { Port = 36_624 },
            fixture.Parties.FindSessionEndpoint(replacement));
    }

    [Fact]
    public void ValidInviteIsBoundToTheTwoCurrentSessionsAndTargetsOnlyTheInvitee()
    {
        var fixture = new PartyFixture();
        var inviter = fixture.Admit(1, 101);
        var invitee = fixture.Admit(2, 202);

        var result = fixture.Parties.RequestInvite(inviter, invitee.WireUserId, Profile(0x400000));

        Assert.True(result.Succeeded);
        var invitation = Assert.IsType<PartyInviteNotification>(result.Notifications.Invitation);
        Assert.Equal(inviter.WireUserId, invitation.InviterWireUserId);
        Assert.Equal(invitee.CharacterId, invitation.TargetCharacterId);
        Assert.Equal(Profile(0x400000), invitation.Display);
        Assert.Empty(result.Notifications.ClearAudience);
        Assert.Null(result.Notifications.Snapshot);
        Assert.Null(result.Notifications.Acceptance);
    }

    [Fact]
    public void InviteRejectsSelfAndEveryTownDomainMismatchWithoutStateChange()
    {
        var fixture = new PartyFixture();
        var inviter = fixture.Admit(1, 101);
        var target = fixture.Admit(2, 202);

        Assert.Equal(
            PartyFailureReason.SelfInvite,
            fixture.Parties.RequestInvite(inviter, inviter.WireUserId, Profile(1)).Failure);

        fixture.Presence.RelocateExact(target, townId: 2, areaId: 1, standingInTown: true);
        Assert.Equal(
            PartyFailureReason.NotInSameTownArea,
            fixture.Parties.RequestInvite(inviter, target.WireUserId, Profile(1)).Failure);

        fixture.Presence.RelocateExact(target, townId: 1, areaId: 1, standingInTown: false);
        Assert.Equal(
            PartyFailureReason.NotStandingInTown,
            fixture.Parties.RequestInvite(inviter, target.WireUserId, Profile(1)).Failure);

        var otherListener = fixture.Admit(3, 303, listenerPort: 10012);
        Assert.Equal(
            PartyFailureReason.NotInSameGameChannel,
            fixture.Parties.RequestInvite(inviter, otherListener.WireUserId, Profile(1)).Failure);

        Assert.True(fixture.Parties.FindParty(inviter).IsVirtual);
        Assert.True(fixture.Parties.FindParty(target).IsVirtual);
        Assert.True(fixture.Parties.FindParty(otherListener).IsVirtual);
    }

    [Fact]
    public void AcceptCreatesStableSlotsAndConsumesTheInviteOnlyOnce()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var second = fixture.Admit(2, 202);
        var third = fixture.Admit(3, 303);
        var fourth = fixture.Admit(4, 404);

        Assert.True(fixture.Parties.RequestInvite(leader, second.WireUserId, Profile(41)).Succeeded);
        var accepted = fixture.Parties.AcceptInvite(second, leader.WireUserId);

        Assert.True(accepted.Succeeded);
        Assert.Equal(
            new PartyAcceptanceNotification(
                leader.CharacterId,
                second.CharacterId,
                leader.WireUserId,
                second.WireUserId),
            accepted.Notifications.Acceptance);
        var firstSnapshot = Assert.IsType<PartySnapshot>(accepted.Notifications.Snapshot);
        Assert.Equal(second.CharacterId, Assert.Single(accepted.Notifications.ClearAudience));
        Assert.Equal(firstSnapshot.Key, accepted.Notifications.ClearPartyKey);
        Assert.Equal(firstSnapshot.LeadershipVersion, accepted.Notifications.ClearLeadershipVersion);
        Assert.Equal((ushort)0, firstSnapshot.PartyId);
        Assert.Equal(leader.WireUserId, firstSnapshot.LeaderWireUserId);
        Assert.Collection(
            firstSnapshot.Members,
            member => { Assert.Equal(leader.CharacterId, member.CharacterId); Assert.Equal(0, member.Slot); },
            member => { Assert.Equal(second.CharacterId, member.CharacterId); Assert.Equal(1, member.Slot); });

        Assert.Equal(
            PartyFailureReason.InviteNotFoundOrStale,
            fixture.Parties.AcceptInvite(second, leader.WireUserId).Failure);

        Assert.True(fixture.Parties.RequestInvite(leader, third.WireUserId, Profile(42)).Succeeded);
        Assert.True(fixture.Parties.AcceptInvite(third, leader.WireUserId).Succeeded);
        var expanded = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(leader));
        Assert.Equal([0, 1, 2], expanded.Members.Select(member => (int)member.Slot));
        Assert.Equal(
            [leader.WireUserId, second.WireUserId, third.WireUserId],
            expanded.Members.Select(member => member.WireUserId));

        Assert.True(fixture.Parties.LeavePartyExact(second).Succeeded);
        Assert.True(fixture.Parties.RequestInvite(leader, fourth.WireUserId, Profile(43)).Succeeded);
        Assert.True(fixture.Parties.AcceptInvite(fourth, leader.WireUserId).Succeeded);
        var reusedSlot = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(leader));
        Assert.Equal(
            (byte)1,
            Assert.Single(reusedSlot.Members, item => item.CharacterId == fourth.CharacterId).Slot);
    }

    [Fact]
    public void InviteExpiresAndCleanupPreventsCrossingReplacementOrPartyGenerations()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var expiredTarget = fixture.Admit(2, 202);
        Assert.True(fixture.Parties.RequestInvite(leader, expiredTarget.WireUserId, Profile(1)).Succeeded);
        fixture.Clock.Advance(PartyService.InvitationLifetime + TimeSpan.FromTicks(1));
        Assert.Equal(
            PartyFailureReason.InviteExpired,
            fixture.Parties.AcceptInvite(expiredTarget, leader.WireUserId).Failure);

        var staleTarget = fixture.Admit(3, 303);
        Assert.True(fixture.Parties.RequestInvite(leader, staleTarget.WireUserId, Profile(2)).Succeeded);
        Assert.True(fixture.Parties.CleanupSessionExact(staleTarget).Succeeded);
        var targetReplacement = fixture.Replace(staleTarget, generation: 2);
        Assert.Equal(
            PartyFailureReason.InviteNotFoundOrStale,
            fixture.Parties.AcceptInvite(targetReplacement, leader.WireUserId).Failure);

        var staleInviter = fixture.Admit(6, 606);
        var staleInviterTarget = fixture.Admit(7, 707);
        Assert.True(fixture.Parties.RequestInvite(staleInviter, staleInviterTarget.WireUserId, Profile(5)).Succeeded);
        Assert.True(fixture.Parties.CleanupSessionExact(staleInviter).Succeeded);
        fixture.Replace(staleInviter, generation: 2);
        Assert.Equal(
            PartyFailureReason.InviteNotFoundOrStale,
            fixture.Parties.AcceptInvite(staleInviterTarget, staleInviter.WireUserId).Failure);

        var generationLeader = fixture.Admit(8, 808);
        var oldGenerationTarget = fixture.Admit(4, 404);
        var partyMate = fixture.Admit(5, 505);
        Assert.True(fixture.Parties.RequestInvite(
            generationLeader, oldGenerationTarget.WireUserId, Profile(3)).Succeeded);
        Assert.True(fixture.Parties.RequestInvite(
            generationLeader, partyMate.WireUserId, Profile(4)).Succeeded);
        Assert.True(fixture.Parties.AcceptInvite(
            partyMate, generationLeader.WireUserId).Succeeded);
        Assert.Equal(
            PartyFailureReason.PartyGenerationChanged,
            fixture.Parties.AcceptInvite(
                oldGenerationTarget, generationLeader.WireUserId).Failure);
    }

    [Fact]
    public void InvalidTownDomainResponseDoesNotConsumeAnOtherwiseValidInvite()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var target = fixture.Admit(2, 202);
        Assert.True(fixture.Parties.RequestInvite(leader, target.WireUserId, Profile(1)).Succeeded);

        Assert.True(fixture.Presence.RelocateExact(
            target, townId: 2, areaId: 1, standingInTown: true));
        Assert.Equal(
            PartyFailureReason.NotInSameTownArea,
            fixture.Parties.AcceptInvite(target, leader.WireUserId).Failure);

        Assert.True(fixture.Presence.RelocateExact(
            target, townId: 1, areaId: 1, standingInTown: true));
        Assert.True(fixture.Parties.AcceptInvite(target, leader.WireUserId).Succeeded);
        Assert.False(fixture.Parties.FindParty(target).IsVirtual);
    }

    [Fact]
    public void OnlyTheLeaderOfANonFullPartyCanInviteAnUnattachedTarget()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var member = fixture.Admit(2, 202);
        var third = fixture.Admit(3, 303);
        var fourth = fixture.Admit(4, 404);
        var fifth = fixture.Admit(5, 505);
        fixture.Form(leader, member);

        Assert.Equal(
            PartyFailureReason.InviterNotLeader,
            fixture.Parties.RequestInvite(member, fifth.WireUserId, Profile(1)).Failure);

        fixture.Form(leader, third);
        fixture.Form(leader, fourth);
        Assert.Equal(
            PartyFailureReason.PartyFull,
            fixture.Parties.RequestInvite(leader, fifth.WireUserId, Profile(1)).Failure);

        var otherLeader = fixture.Admit(6, 606);
        var attachedTarget = fixture.Admit(7, 707);
        fixture.Form(otherLeader, attachedTarget);
        Assert.Equal(
            PartyFailureReason.TargetAlreadyInParty,
            fixture.Parties.RequestInvite(leader, attachedTarget.WireUserId, Profile(1)).Failure);
    }

    [Fact]
    public void MemberLeaveAndLeaderLeaveBothKeepRemainingMembersInTheParty()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var member = fixture.Admit(2, 202);
        var third = fixture.Admit(3, 303);
        fixture.Form(leader, member);
        fixture.Form(leader, third);

        var memberLeave = fixture.Parties.LeavePartyExact(member);

        Assert.True(memberLeave.Succeeded);
        Assert.Collection(
            memberLeave.Notifications.ClearAudience,
            cleared => Assert.Equal(member.CharacterId, cleared));
        var survivors = Assert.IsType<PartySnapshot>(memberLeave.Notifications.Snapshot);
        Assert.Equal(
            [leader.WireUserId, third.WireUserId],
            survivors.Members.Select(item => item.WireUserId));
        Assert.True(fixture.Parties.FindParty(member).IsVirtual);

        var leaderLeave = fixture.Parties.LeavePartyExact(leader);

        Assert.True(leaderLeave.Succeeded);
        var remaining = Assert.IsType<PartySnapshot>(leaderLeave.Notifications.Snapshot);
        Assert.NotEqual(survivors.LeadershipVersion, remaining.LeadershipVersion);
        Assert.Equal(survivors.Key, remaining.Key);
        Assert.Equal(survivors.Key, leaderLeave.Notifications.ClearPartyKey);
        Assert.Equal(survivors.LeadershipVersion, leaderLeave.Notifications.ClearLeadershipVersion);
        Assert.Equal(
            [leader.CharacterId, third.CharacterId],
            leaderLeave.Notifications.ClearAudience.ToArray());
        Assert.Null(leaderLeave.Notifications.LeadershipTransfer);
        Assert.Equal(third.CharacterId, Assert.Single(remaining.Members).CharacterId);
        Assert.Equal(third.WireUserId, remaining.LeaderWireUserId);
        Assert.True(fixture.Parties.FindParty(leader).IsVirtual);
        Assert.False(fixture.Parties.FindParty(third).IsVirtual);
        Assert.Equal(
            PartyFailureReason.NotInParty,
            fixture.Parties.LeavePartyExact(leader).Failure);
    }

    [Fact]
    public void CleanupIsExactForInvitesEndpointsAndMembership()
    {
        var fixture = new PartyFixture();
        var first = fixture.Admit(1, 101);
        var peer = fixture.Admit(2, 202);
        Assert.True(fixture.Parties.ObserveSessionEndpoint(first, Endpoint(30_976)).Succeeded);
        fixture.Form(first, peer);
        Assert.True(fixture.Parties.CleanupSessionExact(first).Succeeded);

        var replacement = fixture.Replace(first, generation: 2);
        var replacementPeer = fixture.Admit(3, 303);
        Assert.True(fixture.Parties.ObserveSessionEndpoint(replacement, Endpoint(36_624)).Succeeded);
        Assert.True(fixture.Parties.RequestInvite(
            replacement, replacementPeer.WireUserId, Profile(9)).Succeeded);

        Assert.Equal(
            PartyFailureReason.CallerNotCurrent,
            fixture.Parties.CleanupSessionExact(first).Failure);
        Assert.Equal(Endpoint(36_624), fixture.Parties.FindSessionEndpoint(replacement));
        Assert.True(fixture.Parties.AcceptInvite(replacementPeer, replacement.WireUserId).Succeeded);
        Assert.False(fixture.Parties.FindParty(replacement).IsVirtual);
    }

    [Fact]
    public void PublishedSnapshotsDoNotChangeWhenEndpointsOrMembersChangeLater()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var member = fixture.Admit(2, 202);
        fixture.Parties.ObserveSessionEndpoint(leader, Endpoint(30_976));
        var accepted = fixture.Form(leader, member);
        var original = Assert.IsType<PartySnapshot>(accepted.Notifications.Snapshot);

        fixture.Parties.ObserveSessionEndpoint(leader, Endpoint(36_624));
        fixture.Parties.LeavePartyExact(member);

        Assert.Equal(2, original.Members.Length);
        Assert.Equal((ushort)30_976, original.Members[0].Endpoint?.Port);
        var current = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(leader));
        Assert.Single(current.Members);
        Assert.Equal((ushort)36_624, current.Members[0].Endpoint?.Port);
        Assert.True(current.Revision > original.Revision);
    }

    private static SessionEndpoint Endpoint(ushort port) => new(
        NatType: 5,
        InnerAddress: new SessionIpv4Address(172, 17, 208, 1),
        OuterAddress: new SessionIpv4Address(172, 17, 208, 1),
        Port: port,
        Mtu: 1436);

    private static void Create(
        PartyService service,
        int characterId,
        PartyDirectoryScope? scope,
        PartySettings settings)
    {
        var result = service.CreateOrUpdateSettings(
            new PartyCharacterSnapshot(
                new AccountId(characterId),
                new CharacterId(characterId),
                checked((ushort)characterId),
                scope),
            settings,
            PartyTitle.Empty);
        Assert.True(result.Succeeded);
    }

    private static PartyCharacterSnapshot Character(
        int characterId,
        PartyDirectoryScope? scope) =>
        new(
            new AccountId(characterId),
            new CharacterId(characterId),
            checked((ushort)characterId),
            scope);

    private static PartySettings DirectorySettings(PartyCategory category, ushort target)
    {
        var bytes = new byte[] { 0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)category, 0, 0 };
        bytes[10] = checked((byte)target);
        bytes[11] = checked((byte)(target >> 8));
        return new PartySettings(bytes);
    }

    private sealed class PartyFixture
    {
        private int nextConnection;

        public PartyFixture()
        {
            Parties = new PartyServiceTestAdapter(
                Sessions,
                Presence,
                new StubPartyMemberStatusDomainService(),
                new PartyModeRuleService(),
                Clock);
        }

        public SelectedSessionDomainService Sessions { get; } = Dfo.Server.Testing.TestSessions.Directory();
        public SessionPresenceDomainService Presence { get; } = new();
        public AdvanceablePartyClock Clock { get; } = new(
            new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
        public PartyServiceTestAdapter Parties { get; }

        public SelectedSessionIdentity Admit(
            int account,
            int character,
            long generation = 1,
            int listenerPort = 10011)
        {
            var session = Session(account, character, generation, listenerPort);
            Sessions.Select(session);
            Presence.Admit(new SessionPresence(
                session.CharacterId, listenerPort, TownId: 1, AreaId: 1,
                StandingInTown: true, session,
                ChannelId: listenerPort - 10_000));
            return session;
        }

        public SelectedSessionIdentity Replace(SelectedSessionIdentity old, long generation)
        {
            var replacement = Session(
                old.AccountId.Value,
                old.CharacterId.Value,
                generation,
                old.ListenerPort);
            Sessions.Select(replacement);
            Presence.Admit(new SessionPresence(
                replacement.CharacterId, replacement.ListenerPort, TownId: 1, AreaId: 1,
                StandingInTown: true, replacement,
                ChannelId: replacement.ListenerPort - 10_000));
            return replacement;
        }

        public SelectedSessionIdentity ReplaceSelectionOnly(
            SelectedSessionIdentity old,
            long generation)
        {
            var replacement = Session(
                old.AccountId.Value,
                old.CharacterId.Value,
                generation,
                old.ListenerPort);
            Sessions.Select(replacement);
            return replacement;
        }

        public PartyOperationResult Form(
            SelectedSessionIdentity leader,
            SelectedSessionIdentity member)
        {
            Assert.True(Parties.RequestInvite(
                leader, member.WireUserId, Profile(member.WireUserId)).Succeeded);
            var accepted = Parties.AcceptInvite(member, leader.WireUserId);
            Assert.True(accepted.Succeeded);
            return accepted;
        }

        private SelectedSessionIdentity Session(
            int account,
            int character,
            long generation,
            int listenerPort) =>
            SelectedSessionIdentity.Create(
                new AccountId(account),
                new CharacterId(character),
                new NetworkConnectionId($"Game:party-{++nextConnection}"),
                generation,
                listenerPort);
    }

    private sealed class AdvanceablePartyClock(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }

    private static PartyInviteDisplayState Profile(int reviveCoinCount) =>
        new(reviveCoinCount, 188, 0, 0);
}
