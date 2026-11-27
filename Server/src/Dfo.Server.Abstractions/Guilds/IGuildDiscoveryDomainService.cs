using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildDiscoveryDomainService
{
    ValueTask<GuildDiscoveryDetails?> FindGuildAsync(string name, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildAccountEntry>> ReadAccountGuildsAsync(AccountId account, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRecommendation>> ReadRecommendationsAsync(CharacterId viewer, CancellationToken ct);
}
