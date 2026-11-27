using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Guilds;

public sealed record GuildInvitation(GuildId Guild, SelectedSessionIdentity Inviter, byte[] InviteeName, DateTimeOffset ExpiresAt);

public sealed record GuildInvitationOffer(Guild Guild, SelectedSessionIdentity Inviter, SelectedSessionIdentity Invitee,
    byte[] InviterName, byte[] InviteeName, int MemberCount);

public interface IGuildInvitationSessionDomainService
{
    ValueTask<GuildInvitationOffer?> PrepareOfferAsync(SelectedSessionIdentity inviter, byte[] inviteeName, CancellationToken ct);
    bool TryOffer(GuildInvitationOffer offer);
    GuildInvitation? Find(SelectedSessionIdentity invitee);
    void Remove(SelectedSessionIdentity invitee, GuildInvitation invitation);
}
