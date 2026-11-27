using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Guilds;

public sealed record GuildInvitation(GuildId Guild, SelectedSessionIdentity Inviter, byte[] InviteeName, DateTimeOffset ExpiresAt);

public interface IGuildInvitationSessionDomainService
{
    bool TryOffer(SelectedSessionIdentity inviter, SelectedSessionIdentity invitee, GuildId guild, byte[] inviteeName);
    GuildInvitation? Find(SelectedSessionIdentity invitee);
    void Remove(SelectedSessionIdentity invitee, GuildInvitation invitation);
}
