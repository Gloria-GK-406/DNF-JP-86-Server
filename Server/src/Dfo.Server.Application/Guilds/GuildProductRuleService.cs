using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildProductRuleService(IGuildEconomySource content)
{
    public GuildContentDefinition? FindProduct(uint productId) =>
        content.ReadContentCatalog()?.Products.GetValueOrDefault(productId);

    public GuildGoldRenameOffer? GoldRenameOffer(uint productId) =>
        content.ReadContentCatalog()?.GoldRename is { GoldCost: > 0 } offer && offer.ProductId == productId ? offer : null;

    public int? WarehouseCapacityLimit() => content.ReadContentCatalog()?.WarehouseCapacityLimit;

    public GuildAttributeBonus? Attributes(GuildContentKind kind, int buffId)
    {
        var catalog = content.ReadContentCatalog();
        var bonus = kind switch
        {
            GuildContentKind.PermanentAttributes => catalog?.Attributes,
            GuildContentKind.TemporaryAttributes => catalog?.TemporaryAttributes,
            _ => null,
        };
        return bonus?.BuffId == buffId ? bonus : null;
    }
}
