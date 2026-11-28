using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildInvitationSessionDomainService(ISelectedSessionDomainService sessions,
    IGuildMembershipQueryDomainService guilds, IMailboxQueryDomainService characters,
    ICharacterRelationQueryDomainService relations, TimeProvider clock)
    : IGuildInvitationSessionDomainService
{
    private readonly ConditionalWeakTable<SelectedSessionIdentity, GuildInvitation> pending = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public async ValueTask<GuildInvitationOffer?> PrepareOfferAsync(SelectedSessionIdentity inviter, byte[] inviteeName,
        CancellationToken ct)
    {
        if (await guilds.FindMembershipAsync(inviter.CharacterId, ct).ConfigureAwait(false) is not { Guild: var guild }
            || await characters.FindRecipientAsync(inviteeName, ct).ConfigureAwait(false) is not { } candidate
            || candidate.CharacterId == inviter.CharacterId || !sessions.TryGetInWorld(candidate.CharacterId, out var invitee)
            || await guilds.FindMembershipAsync(candidate.CharacterId, ct).ConfigureAwait(false) is not null
            || (await relations.ObserversOfAsync(inviter.CharacterId, CharacterRelationKind.Blacklist, ct)
                .ConfigureAwait(false)).Contains(candidate.CharacterId))
            return null;

        var roster = await guilds.ReadRosterEntriesAsync(inviter.CharacterId, ct).ConfigureAwait(false);
        var member = roster.FirstOrDefault(entry => entry.Membership.Character == inviter.CharacterId);
        return member is null ? null : new(guild, inviter, invitee, member.Name, candidate.Name, roster.Count);
    }

    public bool TryOffer(GuildInvitationOffer offer)
    {
        GameplayWriteAssertion.Writing(nameof(GuildInvitationSessionDomainService) + "." + nameof(TryOffer));
        if (!sessions.IsInWorld(offer.Inviter) || !sessions.IsInWorld(offer.Invitee) || Find(offer.Invitee) is not null)
            return false;
        pending.Remove(offer.Invitee);
        return pending.TryAdd(offer.Invitee, new(offer.Guild.Id, offer.Inviter, offer.InviteeName, clock.GetUtcNow() + Lifetime));
    }

    public GuildInvitation? Find(SelectedSessionIdentity invitee)
    {
        if (!sessions.IsInWorld(invitee) || !pending.TryGetValue(invitee, out var offer)
            || offer.ExpiresAt <= clock.GetUtcNow() || !sessions.IsInWorld(offer.Inviter))
            return null;
        return offer;
    }

    public void Remove(SelectedSessionIdentity invitee, GuildInvitation invitation)
    {
        GameplayWriteAssertion.Writing(nameof(GuildInvitationSessionDomainService) + "." + nameof(Remove));
        if (pending.TryGetValue(invitee, out var current) && ReferenceEquals(current, invitation))
            pending.Remove(invitee);
    }
}
