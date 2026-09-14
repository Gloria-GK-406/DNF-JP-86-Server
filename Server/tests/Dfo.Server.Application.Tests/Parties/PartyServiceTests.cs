using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Parties;

public sealed class PartyServiceTests
{
    [Fact]
    public void DirectoryQueryCombinesScopeCategoryAndModeFiltersAndReturnsAllMatches()
    {
        var service = new PartyService(
            new StubPartyMemberStatusDomainService(),
            new PartyModeRuleService(),
            TimeProvider.System);
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
            TimeProvider.System);
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
            TimeProvider.System);
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
            TimeProvider.System);
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
    public void LeaveAndDisbandDescribeTheAffectedDirectoryScopes()
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
        var disband = fixture.Parties.LeavePartyExact(leader);

        Assert.True(disband.Succeeded);
        Assert.Equal(
            new PartyDirectoryChange(scope, null),
            disband.Notifications.DirectoryChange);
    }

    [Fact]
    public void LeadershipTransferMovesTheDirectoryToTheNewLeadersLatestScope()
    {
        var service = new PartyService(
            new StubPartyMemberStatusDomainService(),
            new PartyModeRuleService(),
            TimeProvider.System);
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
        Assert.Empty(service.Query(new PartyDirectoryQuery(firstScope)));
        var moved = Assert.Single(service.Query(new PartyDirectoryQuery(secondScope)));
        Assert.Equal(successor.WireUserId, moved.LeaderWireUserId);
        Assert.Equal(
            [(successor.CharacterId, (byte)0), (leader.CharacterId, (byte)1)],
            moved.Members.Select(member => (member.CharacterId, member.Slot)));

        Assert.True(service.LeaveParty(successor.CharacterId).Succeeded);
        Assert.Empty(service.Query(new PartyDirectoryQuery(secondScope)));
        Assert.Null(service.FindParty(leader.CharacterId));
    }

    [Fact]
    public void EndpointUpdateRequiresAValidEndpointAndTheCurrentExactSession()
    {
        var fixture = new PartyFixture();
        var first = fixture.Admit(1, 101);
        var valid = Endpoint(30_976);

        Assert.True(fixture.Parties.UpdateEndpoint(first, valid).Succeeded);
        Assert.Equal(valid, fixture.Parties.FindEndpoint(first));

        var observedLoopback = valid with
        {
            NatType = 0,
            InnerAddress = new PartyIpv4Address(127, 0, 0, 1),
            OuterAddress = new PartyIpv4Address(127, 0, 0, 1),
            Mtu = 1500,
        };
        Assert.True(fixture.Parties.UpdateEndpoint(first, observedLoopback).Succeeded);
        Assert.Equal(observedLoopback, fixture.Parties.FindEndpoint(first));

        var invalid = valid with { Port = 0 };
        Assert.Equal(
            PartyFailureReason.InvalidEndpoint,
            fixture.Parties.UpdateEndpoint(first, invalid).Failure);
        Assert.Equal(observedLoopback, fixture.Parties.FindEndpoint(first));

        var replacement = fixture.Replace(first, generation: 2);
        Assert.Equal(
            PartyFailureReason.CallerNotCurrent,
            fixture.Parties.UpdateEndpoint(first, valid with { Port = 36_624 }).Failure);
        Assert.Equal(observedLoopback, fixture.Parties.FindEndpoint(replacement));
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

        Assert.Null(fixture.Parties.FindParty(inviter));
        Assert.Null(fixture.Parties.FindParty(target));
        Assert.Null(fixture.Parties.FindParty(otherListener));
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
        Assert.NotNull(fixture.Parties.FindParty(target));
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
    public void MemberLeaveRefreshesSurvivorsWhileLeaderLeaveDisbandsEveryone()
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
        Assert.Null(fixture.Parties.FindParty(member));

        var leaderLeave = fixture.Parties.LeavePartyExact(leader);

        Assert.True(leaderLeave.Succeeded);
        Assert.Null(leaderLeave.Notifications.Snapshot);
        Assert.Equal(
            [leader.CharacterId.Value, third.CharacterId.Value],
            leaderLeave.Notifications.ClearAudience.Select(character => character.Value));
        Assert.Null(fixture.Parties.FindParty(leader));
        Assert.Null(fixture.Parties.FindParty(third));
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
        Assert.True(fixture.Parties.UpdateEndpoint(first, Endpoint(30_976)).Succeeded);
        fixture.Form(first, peer);
        Assert.True(fixture.Parties.CleanupSessionExact(first).Succeeded);

        var replacement = fixture.Replace(first, generation: 2);
        var replacementPeer = fixture.Admit(3, 303);
        Assert.True(fixture.Parties.UpdateEndpoint(replacement, Endpoint(36_624)).Succeeded);
        Assert.True(fixture.Parties.RequestInvite(
            replacement, replacementPeer.WireUserId, Profile(9)).Succeeded);

        Assert.Equal(
            PartyFailureReason.CallerNotCurrent,
            fixture.Parties.CleanupSessionExact(first).Failure);
        Assert.Equal(Endpoint(36_624), fixture.Parties.FindEndpoint(replacement));
        Assert.True(fixture.Parties.AcceptInvite(replacementPeer, replacement.WireUserId).Succeeded);
        Assert.NotNull(fixture.Parties.FindParty(replacement));
    }

    [Fact]
    public void PublishedSnapshotsDoNotChangeWhenEndpointsOrMembersChangeLater()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var member = fixture.Admit(2, 202);
        fixture.Parties.UpdateEndpoint(leader, Endpoint(30_976));
        var accepted = fixture.Form(leader, member);
        var original = Assert.IsType<PartySnapshot>(accepted.Notifications.Snapshot);

        fixture.Parties.UpdateEndpoint(leader, Endpoint(36_624));
        fixture.Parties.LeavePartyExact(member);

        Assert.Equal(2, original.Members.Length);
        Assert.Equal((ushort)30_976, original.Members[0].Endpoint?.Port);
        var current = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(leader));
        Assert.Single(current.Members);
        Assert.Equal((ushort)36_624, current.Members[0].Endpoint?.Port);
        Assert.True(current.Revision > original.Revision);
    }

    private static PartyEndpoint Endpoint(ushort port) => new(
        NatType: 5,
        InnerAddress: new PartyIpv4Address(172, 17, 208, 1),
        OuterAddress: new PartyIpv4Address(172, 17, 208, 1),
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

        public SelectedSessionDomainService Sessions { get; } = new();
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
