using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildWarehouseCapacityDomainService(IGuildWarehouseStore warehouse, GuildProductRuleService products)
{
    public async ValueTask<GuildMutationResult> PrepareExpansionAsync(GuildId guild, int slots, CancellationToken ct)
    {
        if (products.WarehouseCapacityLimit() is not { } limit)
            return new(GuildMutationStatus.UnsupportedContent);
        var capacity = await warehouse.ReadWarehouseCapacityAsync(guild, ct).ConfigureAwait(false);
        if (slots > limit - capacity)
            return new(GuildMutationStatus.WarehouseCapacityLimit);
        return new(GuildMutationStatus.Success, guild, WarehouseCapacity: capacity + slots);
    }

    public ValueTask ApplyExpansionAsync(GuildId guild, int capacity, CancellationToken ct) =>
        warehouse.SetWarehouseCapacityAsync(guild, capacity, ct);
}
