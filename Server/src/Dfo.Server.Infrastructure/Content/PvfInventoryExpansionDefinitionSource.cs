using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfInventoryExpansionDefinitionSource(PvfArchiveProvider provider) : IInventoryExpansionDefinitionSource
{
    private static readonly PvfField<int> RequiredLevel = PvfField.Int32("[required level]");
    private static readonly PvfField<IReadOnlyList<int>> UpgradeInfo = PvfField.Int32List("[upgrade info]");
    private readonly Lock gate = new();
    private AccountCargoDefinition? account;
    private Dictionary<int, InventoryExpansionGrant>? items;
    public ValueTask<AccountCargoDefinition> AccountCargoAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate) { Load(); return ValueTask.FromResult(account!); }
    }
    public ValueTask<InventoryExpansionGrant?> ItemAsync(int itemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate) { Load(); return ValueTask.FromResult(items!.GetValueOrDefault(itemId)); }
    }
    public ValueTask<InventoryExpansionGrant?> ProductAsync(int productId, int itemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            Load();
            var tier = account!.Tiers.SingleOrDefault(t => t.ProductId > 0 && t.ProductId == productId);
            return ValueTask.FromResult(tier is null ? items!.GetValueOrDefault(itemId)
                : new InventoryExpansionGrant(InventoryExpansionKind.AccountCargo, tier.Capacity, productId));
        }
    }
    private void Load()
    {
        if (account is not null) return;
        var archive = provider.Archive;
        var document = PvfDocument.Open(archive, "etc/accountcargo.etc");
        if (!document.TryRead(RequiredLevel, out var level) || level < 1
            || !document.TryRead(UpgradeInfo, out var values) || values.Count == 0 || values.Count % 6 != 0)
            throw new InvalidDataException("Invalid account cargo declaration.");
        var tiers = new List<AccountCargoTier>();
        for (var i = 0; i < values.Count; i += 6)
        {
            var tier = new AccountCargoTier(values[i], values[i+1], values[i+2], values[i+3], values[i+4], values[i+5]);
            if (tier.Capacity <= (tiers.LastOrDefault()?.Capacity ?? 0) || tier.Capacity > 120 || tier.GoldLimit <= 0
                || tier.MaterialCount < 0 || tier.GoldCost < 0 || tier.MaterialCount > 0 && tier.MaterialId <= 0
                || tier.GoldCost == 0 && tier.MaterialCount == 0 && tier.ProductId <= 0)
                throw new InvalidDataException("Unsupported account cargo tier.");
            tiers.Add(tier);
        }
        // Explicit A21 content identities. Do not infer effects from localized display text.
        var paths = new Dictionary<string, InventoryExpansionGrant>(StringComparer.OrdinalIgnoreCase);
        for (var stage = 1; stage <= 12; stage++)
            paths.Add("cash/safe_upgradekit" + (stage == 1 ? "" : stage.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ".stk",
                new(InventoryExpansionKind.PersonalCargo, 8 + stage * 16));
        for (var stage = 1; stage <= 2; stage++)
            paths.Add($"cash/inven_upgradekit{stage}.stk", new(InventoryExpansionKind.Main, stage * 8));
        paths.Add("cash/chn_20140617_new_sales_item/chn_inventory_3rd_expansion_2683675.stk", new(InventoryExpansionKind.Main, 24));
        for (var stage = 1; stage <= 15; stage++)
            paths.Add($"cash/chn_20140819_avatar_inven/avatar_inven_{2683900 + stage}.stk", new(InventoryExpansionKind.Avatar, stage * 7));
        items = PvfListFile.Read(archive, "stackable/stackable.lst")
            .Where(e => paths.ContainsKey(e.RelativePath.Replace('\\', '/')))
            .ToDictionary(e => e.Id, e => paths[e.RelativePath.Replace('\\', '/')]);
        account = new(level, tiers);
    }
}
