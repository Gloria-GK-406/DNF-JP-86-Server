using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildWarehouseCapacityDomainService(IGuildWarehouseStore warehouse, GuildProductRuleService products)
{
    public async ValueTask<GuildMutationResult> PrepareExpansionAsync(GuildId guild, int slots, CancellationToken ct)
    {
        var capacity = await warehouse.ReadWarehouseCapacityAsync(guild, ct).ConfigureAwait(false);
        return products.EvaluateWarehouseExpansion(guild, capacity, slots);
    }

    public ValueTask ApplyExpansionAsync(GuildId guild, int capacity, CancellationToken ct) =>
        warehouse.SetWarehouseCapacityAsync(guild, capacity, ct);
}
