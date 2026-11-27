using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Guilds;

public sealed record GuildCreationAuthorization(string Name, string Promotion);

public interface IGuildNameOperationSessionDomainService
{
    ValueTask<GuildMutationStatus> StartAsync(SelectedSessionIdentity session, string? name, CancellationToken ct);
    bool SetPromotion(SelectedSessionIdentity session, string? promotion);
    bool Authorize(SelectedSessionIdentity session);
    GuildCreationAuthorization? AuthorizedCreation(SelectedSessionIdentity session);
    GuildRenameAuthorization? CheckedRename(SelectedSessionIdentity session);
    void Cancel(SelectedSessionIdentity session);
}
