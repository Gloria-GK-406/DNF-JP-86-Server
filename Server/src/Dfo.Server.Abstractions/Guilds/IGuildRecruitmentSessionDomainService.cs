using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Guilds;

public sealed record GuildCreationAuthorization(string Name, string Promotion);

public interface IGuildRecruitmentSessionDomainService
{
    ValueTask<GuildMutationStatus> StartAsync(SelectedSessionIdentity session, string? name, CancellationToken ct);
    bool SetPromotion(SelectedSessionIdentity session, string? promotion);
    bool Authorize(SelectedSessionIdentity session);
    GuildCreationAuthorization? AuthorizedCreation(SelectedSessionIdentity session);
    void Cancel(SelectedSessionIdentity session);
}
