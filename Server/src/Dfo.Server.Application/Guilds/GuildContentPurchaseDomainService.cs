using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildContentPurchaseDomainService(
    IGuildDomainStore store, IGuildPointAccount points, GuildProductRuleService products,
    IGuildBenefitGrants benefits, GuildWarehouseCapacityDomainService warehouse) : IGuildContentPurchaseDomainService
{
    public async ValueTask<GuildMutationResult> PurchaseAsync(CharacterId actor, uint productId, CancellationToken ct)
    {
        if (products.FindProduct(productId) is not { } product)
            return new(GuildMutationStatus.UnsupportedContent);
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return new(GuildMutationStatus.LeaderRequired);

        var guild = membership.Guild.Id;
        if (product.Kind == GuildContentKind.WarehouseExpansion)
        {
            var expansion = await warehouse.PrepareExpansionAsync(guild, product.Value, ct).ConfigureAwait(false);
            if (expansion.WarehouseCapacity is not { } capacity)
                return expansion;
            if (!await points.TrySpendAsync(guild, product.Price, ct).ConfigureAwait(false))
                return new(GuildMutationStatus.InsufficientGuildPoints);
            await warehouse.ApplyExpansionAsync(guild, capacity, ct).ConfigureAwait(false);
            return expansion;
        }

        var (status, content) = await benefits.PrepareAsync(guild, product, ct).ConfigureAwait(false);
        if (content is null)
            return new(status);
        if (!await points.TrySpendAsync(guild, product.Price, ct).ConfigureAwait(false))
            return new(GuildMutationStatus.InsufficientGuildPoints);
        await benefits.GrantAsync(guild, content, ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, guild);
    }
}
