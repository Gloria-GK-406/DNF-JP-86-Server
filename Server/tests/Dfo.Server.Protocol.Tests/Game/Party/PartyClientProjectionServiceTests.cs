using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Application.Parties;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Party;

public sealed class PartyClientProjectionServiceTests
{
    [Fact]
    public void Explicit_leadership_transfer_preserves_the_client_view_and_member_slots()
    {
        TestGameplay.Run(() =>
        {
            var inner = new PartyService(new FixedStatus(), new PartyModeRuleService(),
                TimeProvider.System, new NoEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
            var service = new PartyClientProjectionService(inner, new PartyClientIdRegistry());
            var scope = new PartyDirectoryScope(11, 1, 1);
            var leader = new PartyCharacterSnapshot(new AccountId(1), new CharacterId(1), 1, scope);
            var successor = new PartyCharacterSnapshot(new AccountId(2), new CharacterId(2), 2, scope);
            Assert.True(service.RequestInvite(leader, successor,
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(service.AcceptInvite(successor, leader).Succeeded);
            var before = service.FindParty(leader);

            var transfer = service.TransferLeadership(leader.CharacterId, successor.CharacterId);

            Assert.True(transfer.Succeeded);
            Assert.Equal(before.Key, transfer.Notifications.Snapshot!.Key);
            Assert.Equal(before.PartyId, transfer.Notifications.Snapshot.PartyId);
            Assert.Null(transfer.Notifications.ClearPartyId);
            Assert.Empty(transfer.Notifications.ClearAudience);
            Assert.Equal(
                [(leader.CharacterId, (byte)0), (successor.CharacterId, (byte)1)],
                transfer.Notifications.Snapshot.Members.Select(member => (member.CharacterId, member.Slot)));
        });
    }

    [Fact]
    public void Leader_departure_rekeys_only_the_client_view_while_the_party_key_stays_stable()
    {
        TestGameplay.Run(() =>
        {
            var inner = new PartyService(
                new FixedStatus(), new PartyModeRuleService(), TimeProvider.System,
                new NoEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
            var service = new PartyClientProjectionService(inner, new PartyClientIdRegistry());
            var scope = new PartyDirectoryScope(11, 1, 1);
            var leader = new PartyCharacterSnapshot(new AccountId(1), new CharacterId(1), 1, scope);
            var member = new PartyCharacterSnapshot(new AccountId(2), new CharacterId(2), 2, scope);
            Assert.True(service.RequestInvite(leader, member,
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(service.AcceptInvite(member, leader).Succeeded);
            var before = service.FindParty(leader);

            var left = service.LeaveParty(leader.CharacterId);

            Assert.True(left.Succeeded);
            var after = service.FindParty(member);
            Assert.Equal(before.Key, after.Key);
            Assert.NotEqual(before.PartyId, after.PartyId);
            Assert.Equal(before.PartyId, left.Notifications.ClearPartyId);
            Assert.Equal(after.PartyId, left.Notifications.Snapshot!.PartyId);
            Assert.Equal([leader.CharacterId, member.CharacterId],
                left.Notifications.ClearAudience.ToArray());
            Assert.Equal((ushort)0, inner.FindParty(member).PartyId);
            Assert.Equal(member.CharacterId,
                Assert.Single(inner.FindByKey(before.Key)!.Members).CharacterId);
        });
    }

    private sealed class NoEndpoints : IPartyMemberSessionEndpointSource
    {
        public SessionEndpoint? FindThroughCurrentSession(CharacterId characterId) => null;
    }

    private sealed class FixedStatus : IPartyMemberStatusDomainService
    {
        public ValueTask<PartyInviteDisplayState> InvitationOfAsync(
            PartyCharacterSnapshot character, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PartyInviteDisplayState(0, 0, 0, 0));

        public byte CurrentHpPercentOf(CharacterId characterId) => 100;
    }
}
