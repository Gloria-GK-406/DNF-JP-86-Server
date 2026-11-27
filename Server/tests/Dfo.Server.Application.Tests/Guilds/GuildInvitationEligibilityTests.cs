using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Guilds;

[Trait("Category", "Unit")]
public sealed class GuildInvitationEligibilityTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public async Task Only_a_member_can_offer_to_an_unaffiliated_recipient_who_has_not_blocked_them(
        bool inviterIsMember, bool recipientIsMember, bool blocksInviter, bool eligible)
    {
        var inviter = SelectedSessionIdentity.Create(new(1), new(11), new("from"), 1, 10011);
        var invitee = SelectedSessionIdentity.Create(new(2), new(22), new("to"), 1, 10011);
        var guild = new Guild(new(5), "Iris", "Join us", 1);
        var member = new GuildMember(guild.Id, inviter.CharacterId, false, DateTimeOffset.UnixEpoch);
        var roster = new GuildRosterEntry(member, inviter.AccountId, "sender"u8.ToArray(), 1, 0, 0);
        var directory = TestRequired.Absent<IMailboxQueryDomainService>((_, _) =>
            ValueTask.FromResult<MailRecipient?>(new(invitee.CharacterId, "target"u8.ToArray(), 1, 0, 0)));
        var memberships = TestRequired.Absent<IGuildMembershipQueryDomainService>((method, args) =>
        {
            if (method.Name == nameof(IGuildMembershipQueryDomainService.ReadRosterEntriesAsync))
                return ValueTask.FromResult<IReadOnlyList<GuildRosterEntry>>([roster]);
            var id = (CharacterId)args![0]!;
            var exists = id == inviter.CharacterId ? inviterIsMember : recipientIsMember;
            return ValueTask.FromResult<GuildMembership?>(exists ? new(guild, member with { Character = id }) : null);
        });
        var sessions = TestRequired.Absent<ISelectedSessionDomainService>((_, args) =>
        {
            Assert.Equal(invitee.CharacterId, (CharacterId)args![0]!);
            args[1] = invitee;
            return true;
        });
        var relations = TestRequired.Absent<ICharacterRelationQueryDomainService>((method, args) =>
        {
            Assert.Equal(nameof(ICharacterRelationQueryDomainService.ObserversOfAsync), method.Name);
            Assert.Equal(inviter.CharacterId, (CharacterId)args![0]!);
            Assert.Equal(CharacterRelationKind.Blacklist, (CharacterRelationKind)args[1]!);
            return ValueTask.FromResult<IReadOnlyList<CharacterId>>(blocksInviter ? [invitee.CharacterId] : []);
        });

        var invitations = new GuildInvitationSessionDomainService(sessions, memberships, directory, relations, TimeProvider.System);
        var offer = await invitations.PrepareOfferAsync(inviter, "target"u8.ToArray(), default);
        if (!eligible)
        {
            Assert.Null(offer);
            return;
        }
        Assert.NotNull(offer);
        Assert.Same(inviter, offer.Inviter);
        Assert.Same(invitee, offer.Invitee);
        Assert.Equal(guild, offer.Guild);
        Assert.Equal(roster.Name, offer.InviterName);
        Assert.Equal("target"u8.ToArray(), offer.InviteeName);
        Assert.Equal(1, offer.MemberCount);
    }
}
