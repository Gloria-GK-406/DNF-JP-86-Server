using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildOverviewDomainService
{
    ValueTask<GuildOverview?> ReadAsync(CharacterId viewer, CancellationToken ct);
}
