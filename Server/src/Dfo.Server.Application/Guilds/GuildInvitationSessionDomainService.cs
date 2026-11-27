using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildInvitationSessionDomainService(ISelectedSessionDomainService sessions, TimeProvider clock)
    : IGuildInvitationSessionDomainService
{
    private readonly ConditionalWeakTable<SelectedSessionIdentity, GuildInvitation> pending = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public bool TryOffer(SelectedSessionIdentity inviter, SelectedSessionIdentity invitee, GuildId guild, byte[] inviteeName)
    {
        GameplayWriteAssertion.Writing(nameof(TryOffer));
        if (inviter.CharacterId == invitee.CharacterId || !sessions.IsInWorld(inviter)
            || !sessions.IsInWorld(invitee) || Find(invitee) is not null)
            return false;
        pending.Remove(invitee);
        return pending.TryAdd(invitee, new(guild, inviter, inviteeName, clock.GetUtcNow() + Lifetime));
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
        GameplayWriteAssertion.Writing(nameof(Remove));
        if (pending.TryGetValue(invitee, out var current) && ReferenceEquals(current, invitation))
            pending.Remove(invitee);
    }
}
