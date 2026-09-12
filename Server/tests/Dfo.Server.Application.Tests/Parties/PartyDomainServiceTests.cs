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

public sealed class PartyDomainServiceTests
{
    [Fact]
    public void ProductionGraphRegistersOneSharedPartyDomainService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPartyMemberStatusDomainService>(
            new StubPartyMemberStatusDomainService());
        new ApplicationModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IPartyDomainService>();
        var second = provider.GetRequiredService<IPartyDomainService>();

        Assert.Same(first, second);
    }

    [Fact]
    public void EndpointUpdateRequiresAValidEndpointAndTheCurrentExactSession()
    {
        var fixture = new PartyFixture();
        var first = fixture.Admit(1, 101);
        var valid = Endpoint(30_976);

        Assert.True(fixture.Parties.UpdateEndpoint(first, valid).Succeeded);
        Assert.Equal(valid, fixture.Parties.FindEndpoint(first));

        var invalid = valid with { Port = 0 };
        Assert.Equal(
            PartyFailureReason.InvalidEndpoint,
            fixture.Parties.UpdateEndpoint(first, invalid).Failure);
        Assert.Equal(valid, fixture.Parties.FindEndpoint(first));

        var replacement = fixture.Replace(first, generation: 2);
        Assert.Equal(
            PartyFailureReason.SessionNotCurrent,
            fixture.Parties.UpdateEndpoint(first, valid with { Port = 36_624 }).Failure);
        Assert.Null(fixture.Parties.FindEndpoint(replacement));
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
        Assert.Equal(inviter, invitation.Inviter);
        Assert.Equal(invitee, invitation.Target);
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
            new PartyAcceptanceNotification(leader, second),
            accepted.Notifications.Acceptance);
        var firstSnapshot = Assert.IsType<PartySnapshot>(accepted.Notifications.Snapshot);
        Assert.Equal(leader.WireUserId, firstSnapshot.LeaderWireUserId);
        Assert.Collection(
            firstSnapshot.Members,
            member => { Assert.Equal(leader, member.Session); Assert.Equal(0, member.Slot); },
            member => { Assert.Equal(second, member.Session); Assert.Equal(1, member.Slot); });

        Assert.Equal(
            PartyFailureReason.InviteNotFoundOrStale,
            fixture.Parties.AcceptInvite(second, leader.WireUserId).Failure);

        Assert.True(fixture.Parties.RequestInvite(leader, third.WireUserId, Profile(42)).Succeeded);
        Assert.True(fixture.Parties.AcceptInvite(third, leader.WireUserId).Succeeded);
        var expanded = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(leader));
        Assert.Equal([0, 1, 2], expanded.Members.Select(member => (int)member.Slot));
        Assert.Equal(
            [leader.WireUserId, second.WireUserId, third.WireUserId],
            expanded.Members.Select(member => member.Session.WireUserId));

        Assert.True(fixture.Parties.LeavePartyExact(second).Succeeded);
        Assert.True(fixture.Parties.RequestInvite(leader, fourth.WireUserId, Profile(43)).Succeeded);
        Assert.True(fixture.Parties.AcceptInvite(fourth, leader.WireUserId).Succeeded);
        var reusedSlot = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(leader));
        Assert.Equal(
            (byte)1,
            Assert.Single(reusedSlot.Members, item => item.Session == fourth).Slot);
    }

    [Fact]
    public void InviteExpiresAndCannotCrossSessionOrPartyGenerations()
    {
        var fixture = new PartyFixture();
        var leader = fixture.Admit(1, 101);
        var expiredTarget = fixture.Admit(2, 202);
        Assert.True(fixture.Parties.RequestInvite(leader, expiredTarget.WireUserId, Profile(1)).Succeeded);
        fixture.Clock.Advance(PartyDomainService.InvitationLifetime + TimeSpan.FromTicks(1));
        Assert.Equal(
            PartyFailureReason.InviteExpired,
            fixture.Parties.AcceptInvite(expiredTarget, leader.WireUserId).Failure);

        var staleTarget = fixture.Admit(3, 303);
        Assert.True(fixture.Parties.RequestInvite(leader, staleTarget.WireUserId, Profile(2)).Succeeded);
        var targetReplacement = fixture.Replace(staleTarget, generation: 2);
        Assert.Equal(
            PartyFailureReason.InviteNotFoundOrStale,
            fixture.Parties.AcceptInvite(targetReplacement, leader.WireUserId).Failure);

        var staleInviter = fixture.Admit(6, 606);
        var staleInviterTarget = fixture.Admit(7, 707);
        Assert.True(fixture.Parties.RequestInvite(staleInviter, staleInviterTarget.WireUserId, Profile(5)).Succeeded);
        fixture.Replace(staleInviter, generation: 2);
        Assert.Equal(
            PartyFailureReason.InviteNotFoundOrStale,
            fixture.Parties.AcceptInvite(staleInviterTarget, staleInviter.WireUserId).Failure);

        var oldGenerationTarget = fixture.Admit(4, 404);
        var partyMate = fixture.Admit(5, 505);
        Assert.True(fixture.Parties.RequestInvite(leader, oldGenerationTarget.WireUserId, Profile(3)).Succeeded);
        Assert.True(fixture.Parties.RequestInvite(leader, partyMate.WireUserId, Profile(4)).Succeeded);
        Assert.True(fixture.Parties.AcceptInvite(partyMate, leader.WireUserId).Succeeded);
        Assert.Equal(
            PartyFailureReason.PartyGenerationChanged,
            fixture.Parties.AcceptInvite(oldGenerationTarget, leader.WireUserId).Failure);
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
            cleared => Assert.Equal(member, cleared));
        var survivors = Assert.IsType<PartySnapshot>(memberLeave.Notifications.Snapshot);
        Assert.Equal(
            [leader.WireUserId, third.WireUserId],
            survivors.Members.Select(item => item.Session.WireUserId));
        Assert.Null(fixture.Parties.FindParty(member));

        var leaderLeave = fixture.Parties.LeavePartyExact(leader);

        Assert.True(leaderLeave.Succeeded);
        Assert.Null(leaderLeave.Notifications.Snapshot);
        Assert.Equal(
            [leader.WireUserId, third.WireUserId],
            leaderLeave.Notifications.ClearAudience.Select(session => session.WireUserId));
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
            PartyFailureReason.SessionNotCurrent,
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

    private sealed class PartyFixture
    {
        private int nextConnection;

        public PartyFixture()
        {
            Parties = new PartyDomainService(
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
        public PartyDomainService Parties { get; }

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
                StandingInTown: true, session));
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
                StandingInTown: true, replacement));
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
