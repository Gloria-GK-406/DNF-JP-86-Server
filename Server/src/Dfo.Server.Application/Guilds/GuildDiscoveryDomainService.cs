using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildDiscoveryDomainService(IGuildDomainStore store) : IGuildDiscoveryDomainService
{
    public ValueTask<GuildDiscoveryDetails?> FindGuildAsync(string name, CancellationToken ct) =>
        GuildProfileRuleService.NameKey(name) is { } key ? store.FindGuildByNameKeyAsync(key, ct) : ValueTask.FromResult<GuildDiscoveryDetails?>(null);

    public ValueTask<IReadOnlyList<GuildAccountEntry>> ReadAccountGuildsAsync(AccountId account, CancellationToken ct) =>
        store.ReadAccountGuildsAsync(account, byte.MaxValue, ct);

    public ValueTask<IReadOnlyList<GuildRecommendation>> ReadRecommendationsAsync(CharacterId viewer, CancellationToken ct) =>
        store.ReadRecommendationsAsync(viewer, 10, ct);
}
