using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfNpcShopDefinitionSource(PvfArchiveProvider provider) : INpcShopDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<int> NPCField = PvfField.Int32("[NPC]", optional: true);
        internal static readonly PvfField<int> PriceField = PvfField.Int32("[price]", optional: true);
        internal static readonly PvfField<int> AddPriceField = PvfField.Int32("[add price]", optional: true);
        internal static readonly PvfField<int> DurabilityField = PvfField.Int32("[durability]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> RoleField = PvfField.Tokens("[role]", optional: true);
        internal static readonly PvfField<int> VisibleOnDungeonClearField = PvfField.Int32("[visible on dungeon clear]", optional: true);
        internal static readonly PvfField<bool> BoosterInfoField = PvfField.Marker("[booster info]", optional: true);
        internal static readonly PvfField<bool> CloseBoosterInfoField = PvfField.Marker("[/booster info]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> NeedMaterialField = PvfField.Int32List("[need material]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> DailyPurchaseLimitField = PvfField.Tokens("[daily purchase limit]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ResetDailyPurchaseItemField = PvfField.Tokens("[reset daily purchase item]", optional: true);
        internal static readonly PvfField<string> AttachTypeField = PvfField.String("[attach type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> CoinField = PvfField.Tokens("[coin]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> BuyCoinField = PvfField.Tokens("[buy coin]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> CeraField = PvfField.Tokens("[cera]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> SeraField = PvfField.Tokens("[sera]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> CashField = PvfField.Tokens("[cash]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> PriceTypeField = PvfField.Tokens("[price type]", optional: true);
        internal static readonly PvfField<bool> SellInfoField = PvfField.Marker("[sell info]", optional: true);
        internal static readonly PvfField<bool> CloseSellInfoField = PvfField.Marker("[/sell info]", optional: true);
        internal static readonly PvfField<string> UseCategoryField = PvfField.String("[use category]", optional: true);
        internal static readonly PvfField<bool> CategoryEntryField = PvfField.Marker("[category entry]", optional: true);
        internal static readonly PvfField<bool> CloseCategoryEntryField = PvfField.Marker("[/category entry]", optional: true);
        internal static readonly PvfField<int> IdField = PvfField.Int32("[id]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> ItemListField = PvfField.Int32List("[item list]", optional: true);
        internal static readonly PvfField<bool> TabField = PvfField.Marker("[tab]", optional: true);
        internal static readonly PvfField<bool> CloseTabField = PvfField.Marker("[/tab]", optional: true);
        internal static readonly PvfField<bool> CloseItemListField = PvfField.Marker("[/item list]", optional: true);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, NpcShopDefinition?> shops = [];
    private readonly Dictionary<int, NpcShopItemDefinition?> items = [];
    private IReadOnlyDictionary<int, string>? npcs;
    private IReadOnlyDictionary<int, string>? shopPaths;
    private IReadOnlyDictionary<int, string>? stackables;
    private IReadOnlyDictionary<int, string>? equipment;

    public string Name => "npc-shop-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var archive = provider.Archive;
            NpcsIndex(archive);
            ShopPathsIndex(archive);
            StackablesIndex(archive);
            EquipmentIndex(archive);
            return ValueTask.FromResult(npcs.Count + shopPaths.Count + stackables.Count + equipment.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(npcs))]
    private void NpcsIndex(Dfo.Pvf.PvfArchive archive)
    {
        npcs ??= Paths(archive, "npc/npc.lst");
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(shopPaths))]
    private void ShopPathsIndex(Dfo.Pvf.PvfArchive archive)
    {
        shopPaths ??= Paths(archive, "itemshop/itemshop.lst");
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(stackables))]
    private void StackablesIndex(Dfo.Pvf.PvfArchive archive)
    {
        stackables ??= Paths(archive, "stackable/stackable.lst");
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(equipment))]
    private void EquipmentIndex(Dfo.Pvf.PvfArchive archive)
    {
        equipment ??= Paths(archive, "equipment/equipment.lst");
    }

    public ValueTask<NpcShopDefinition?> GetShopAsync(int npcId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!shops.TryGetValue(npcId, out var shop))
                shops[npcId] = shop = ReadShop(npcId);
            return ValueTask.FromResult(shop);
        }
    }

    public ValueTask<NpcShopItemDefinition?> GetItemAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!items.TryGetValue(itemId, out var item))
                items[itemId] = item = ReadItem(itemId);
            return ValueTask.FromResult(item);
        }
    }

    private NpcShopDefinition? ReadShop(int npcId)
    {
        var archive = provider.Archive;
        NpcsIndex(archive);
        ShopPathsIndex(archive);
        if (!npcs.TryGetValue(npcId, out var npcPath))
            return null;
        var npcSections = Sections(archive, "npc/" + npcPath);
        var role = npcSections.FirstOrDefault(section => section.Name == Fields.RoleField.Name);
        var npcReadable = true;
        var clearVisibility = Integer(archive, npcSections, Fields.VisibleOnDungeonClearField, ref npcReadable) ?? 0;
        if (!npcReadable || clearVisibility is not (0 or 1))
            return null;
        var shopId = 0;
        if (role is not null)
            for (var i = 0; i + 1 < role.Values.Count; i++)
                if (Text(archive, role.Values[i]) is "[item shop]" or "[simple shop]"
                    && role.Values[i + 1].Kind == PvfTokenKind.Int32)
                    shopId = role.Values[i + 1].Value;
        if (shopId <= 0 || !shopPaths.TryGetValue(shopId, out var relative))
            return null;

        var path = "itemshop/" + relative;
        // The sampled A21 archive names Seria's active shop with the (r) regional prefix.
        if (archive.FindFile(path) < 0)
            path = "itemshop/" + Path.GetDirectoryName(relative)?.Replace('\\', '/')
                + (relative.Contains('/') ? "/" : "") + "(r)" + Path.GetFileName(relative);
        var sections = Sections(archive, path);
        var readable = true;
        if (Integer(archive, sections, Fields.NPCField, ref readable) != npcId || !readable)
            return null;
        var pages = new List<NpcShopPage>();
        var selling = false;
        var categoryEntry = false;
        int? job = null;
        foreach (var section in sections)
        {
            switch (section.Name)
            {
                case var fieldName when (Fields.SellInfoField.Matches(fieldName)): selling = true; break;
                case var fieldName when (Fields.CloseSellInfoField.Matches(fieldName)): selling = false; break;
                case var fieldName when (Fields.UseCategoryField.Matches(fieldName)) && selling:
                    if (section.Values.Count != 1 || Text(archive, section.Values[0]) != "basic job")
                        return null;
                    break;
                case var fieldName when (Fields.CategoryEntryField.Matches(fieldName)) && selling: categoryEntry = true; job = null; break;
                case var fieldName when (Fields.CloseCategoryEntryField.Matches(fieldName)): categoryEntry = false; job = null; break;
                case var fieldName when (Fields.IdField.Matches(fieldName)) && selling && categoryEntry:
                    if (section.Values.Count != 1 || section.Values[0].Kind != PvfTokenKind.Int32)
                        return null;
                    job = section.Values[0].Value;
                    break;
                case var fieldName when (Fields.ItemListField.Matches(fieldName)) && selling:
                    if (categoryEntry && job is null
                        || section.Values.Any(value => value.Kind != PvfTokenKind.Int32 || value.Value is 0 or < -1))
                        return null;
                    pages.Add(new(job, section.Values.Where(value => value.Value > 0).Select(value => value.Value).Distinct().ToArray()));
                    break;
                case var fieldName when (Fields.TabField.Matches(fieldName) || Fields.CloseTabField.Matches(fieldName) || Fields.CloseItemListField.Matches(fieldName)): break;
                default:
                    // Unknown sale conditions must not become an unrestricted item list.
                    if (selling)
                        return null;
                    break;
            }
        }
        return pages.Count > 0 ? new(npcId, shopId, pages)
            { VisibleOnDungeonClear = clearVisibility == 1 } : null;
    }

    private NpcShopItemDefinition? ReadItem(int itemId)
    {
        var archive = provider.Archive;
        StackablesIndex(archive);
        EquipmentIndex(archive);
        var path = stackables.TryGetValue(itemId, out var stackable)
            ? "stackable/" + stackable
            : equipment.TryGetValue(itemId, out var piece) ? "equipment/" + piece : null;
        if (path is null || archive.FindFile(path) < 0)
            return null;
        return ParseItem(archive, itemId, Sections(archive, path));
    }

    internal static NpcShopItemDefinition ParseItem(PvfArchive archive, int itemId, IReadOnlyList<Section> sections)
    {
        var readable = true;
        // A gift box's [booster info]/[cera] describes its opening reward, not its purchase cost.
        var itemSections = new List<Section>();
        var boosterDepth = 0;
        foreach (var section in sections)
        {
            if (section.Name == Fields.BoosterInfoField.Name)
                boosterDepth++;
            else if (section.Name == Fields.CloseBoosterInfoField.Name)
            {
                if (boosterDepth == 0)
                    readable = false;
                else
                    boosterDepth--;
            }
            else if (boosterDepth == 0)
                itemSections.Add(section);
        }
        readable &= boosterDepth == 0;
        sections = itemSections;
        var price = Integer(archive, sections, Fields.PriceField, ref readable);
        var correction = Integer(archive, sections, Fields.AddPriceField, ref readable) ?? 0;
        var durability = Integer(archive, sections, Fields.DurabilityField, ref readable) ?? 0;
        var materials = new List<NpcShopMaterialCost>();
        foreach (var section in sections.Where(section => section.Name == Fields.NeedMaterialField.Name))
        {
            if (section.Values.Count == 0 || section.Values.Count % 2 != 0
                || section.Values.Any(value => value.Kind != PvfTokenKind.Int32 || value.Value is 0 or < -1))
            {
                readable = false;
                continue;
            }
            for (var i = 0; i < section.Values.Count; i += 2)
                materials.Add(new(section.Values[i].Value, section.Values[i + 1].Value));
        }

        NpcShopPurchaseLimit? limit = null;
        var limits = sections.Where(section => section.Name == Fields.DailyPurchaseLimitField.Name).ToArray();
        if (limits.Length > 1)
            readable = false;
        if (limits.FirstOrDefault() is { } limited)
        {
            var scope = limited.Values.Count > 0 ? Text(archive, limited.Values[0]) : null;
            // The current Yinha PVF uses this exact misspelling for character limits.
            if (scope == "chraracter") scope = "character";
            if (limited.Values.Count != 2 || scope is not ("account" or "character")
                || limited.Values[1].Kind != PvfTokenKind.Int32 || limited.Values[1].Value <= 0)
                readable = false;
            else
                limit = new(scope == "account" ? NpcShopLimitScope.Account : NpcShopLimitScope.Character,
                    limited.Values[1].Value, sections.Any(section => section.Name == Fields.ResetDailyPurchaseItemField.Name));
        }
        var attachments = sections.Where(section => Fields.AttachTypeField.Matches(section.Name)).ToArray();
        var sealedItem = false;
        if (attachments.Length > 1) readable = false;
        if (attachments.Length == 1)
        {
            try { sealedItem = Fields.AttachTypeField.Decode(archive, attachments[0].Values, "NPC shop item").Equals("[sealing]", StringComparison.OrdinalIgnoreCase); }
            catch (InvalidDataException) { readable = false; }
        }
        if (price is < 0 || durability is < 0 or > ushort.MaxValue
            || sections.Any(section => (section.Name == Fields.CoinField.Name || section.Name == Fields.BuyCoinField.Name || section.Name == Fields.CeraField.Name || section.Name == Fields.SeraField.Name || section.Name == Fields.CashField.Name || section.Name == Fields.PriceTypeField.Name)))
            readable = false;
        return new(itemId, price, correction, materials, limit,
            (ushort)Math.Clamp(durability, 0, ushort.MaxValue), sealedItem, readable);
    }

    private static int? Integer(PvfArchive archive, IReadOnlyList<Section> sections, PvfField<int> name, ref bool readable)
    {
        var found = sections.Where(section => section.Name == name.Name).ToArray();
        if (found.Length == 0)
            return null;
        if (found.Length != 1 || found[0].Values.Count != 1 || found[0].Values[0].Kind != PvfTokenKind.Int32)
        {
            readable = false;
            return null;
        }
        return name.Decode(archive, found[0].Values, "NPC shop item");
    }

    private static string? Text(PvfArchive archive, PvfToken token) =>
        token.IsString ? archive.ResolveString(token.Value).ToLowerInvariant() : null;

    private static IReadOnlyDictionary<int, string> Paths(PvfArchive archive, string list) =>
        PvfListFile.Paths(archive, list);

    internal sealed record Section(string Name, List<PvfToken> Values);

    internal static IReadOnlyList<Section> Sections(PvfArchive archive, string path)
    {
        var index = archive.FindFile(path);
        if (index < 0)
            return [];
        if (archive.GetFileBytes(index).Length % PvfToken.Size != 0)
            throw new InvalidDataException($"PVF '{path}': truncated token record.");
        var reader = archive.OpenScript(index);
        var result = new List<Section>();
        Section? section = null;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                section = new(archive.ResolveString(token.Value), []);
                result.Add(section);
            }
            else
                section?.Values.Add(token);
        }
        return result;
    }
}
