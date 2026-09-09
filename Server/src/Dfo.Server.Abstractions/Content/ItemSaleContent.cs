namespace Dfo.Server.Abstractions.Content;

public sealed record ItemSaleDefinition(int Value, int RatePerThousand, bool CanSell,
    bool IsEquipment = false, ushort MaxDurability = 0);

public interface IItemSaleDefinitionSource
{
    ValueTask<ItemSaleDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
}
