using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public enum GuildCompoundKind { GuardianGem, Medal }

public sealed record GuildCompoundRule(int MaxMaterials, int UpgradeBasisPointsPerMaterial,
    IReadOnlyList<int> Products, IReadOnlyList<int> UpgradedProducts);

public interface IGuildCompoundContentSource
{
    GuildCompoundRule? FindRule(GuildCompoundKind kind, int rarity);
    int? GetRarity(GuildCompoundKind kind, int itemId);
}

public sealed record GuildCompoundInput(GuildCompoundKind Kind, int Rarity, IReadOnlyList<BusinessSlot> Materials);

public sealed record GuildCompoundSlotChange(BusinessSlot Slot, ItemSnapshot? Item, bool ReplacesConsumedSlot = false);

public sealed record GuildCompoundReceipt(int ItemId, IReadOnlyList<GuildCompoundSlotChange> Changes);

public interface IGuildItemCompoundDomainService
{
    ValueTask<GuildCompoundReceipt?> CompoundAsync(SelectedSessionIdentity session, GuildCompoundInput input,
        IEffectFootprintRecorder footprint, CancellationToken cancellationToken);
}
