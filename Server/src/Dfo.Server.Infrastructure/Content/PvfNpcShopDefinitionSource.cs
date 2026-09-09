using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfNpcShopDefinitionSource(PvfArchiveProvider provider) : INpcShopDefinitionSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, NpcShopDefinition?> shops = [];
    private readonly Dictionary<int, NpcShopItemDefinition?> items = [];
    private Dictionary<int, string>? npcs;
    private Dictionary<int, string>? shopPaths;
    private Dictionary<int, string>? stackables;
    private Dictionary<int, string>? equipment;

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
        npcs ??= Paths(archive, "npc/npc.lst");
        shopPaths ??= Paths(archive, "itemshop/itemshop.lst");
        if (!npcs.TryGetValue(npcId, out var npcPath))
            return null;
        var role = Sections(archive, "npc/" + npcPath).FirstOrDefault(section => section.Name == "[role]");
        var shopId = 0;
        if (role is not null)
            for (var i = 0; i + 1 < role.Values.Count; i++)
                if (Text(archive, role.Values[i]) == "[item shop]"
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
        if (Integer(sections, "[NPC]", ref readable) != npcId || !readable)
            return null;
        var pages = new List<NpcShopPage>();
        var selling = false;
        var categoryEntry = false;
        int? job = null;
        foreach (var section in sections)
        {
            switch (section.Name)
            {
                case "[sell info]": selling = true; break;
                case "[/sell info]": selling = false; break;
                case "[use category]" when selling:
                    if (section.Values.Count != 1 || Text(archive, section.Values[0]) != "basic job")
                        return null;
                    break;
                case "[category entry]" when selling: categoryEntry = true; job = null; break;
                case "[/category entry]": categoryEntry = false; job = null; break;
                case "[id]" when selling && categoryEntry:
                    if (section.Values.Count != 1 || section.Values[0].Kind != PvfTokenKind.Int32)
                        return null;
                    job = section.Values[0].Value;
                    break;
                case "[item list]" when selling:
                    if (categoryEntry && job is null
                        || section.Values.Any(value => value.Kind != PvfTokenKind.Int32 || value.Value <= 0))
                        return null;
                    pages.Add(new(job, section.Values.Select(value => value.Value).Distinct().ToArray()));
                    break;
                case "[tab]" or "[/tab]" or "[/item list]": break;
                default:
                    // Unknown sale conditions must not become an unrestricted item list.
                    if (selling)
                        return null;
                    break;
            }
        }
        return pages.Count > 0 ? new(npcId, shopId, pages) : null;
    }

    private NpcShopItemDefinition? ReadItem(int itemId)
    {
        var archive = provider.Archive;
        stackables ??= Paths(archive, "stackable/stackable.lst");
        equipment ??= Paths(archive, "equipment/equipment.lst");
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
            if (section.Name == "[booster info]")
                boosterDepth++;
            else if (section.Name == "[/booster info]")
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
        var price = Integer(sections, "[price]", ref readable);
        var correction = Integer(sections, "[add price]", ref readable) ?? 0;
        var durability = Integer(sections, "[durability]", ref readable) ?? 0;
        var materials = new List<NpcShopMaterialCost>();
        foreach (var section in sections.Where(section => section.Name == "[need material]"))
        {
            if (section.Values.Count == 0 || section.Values.Count % 2 != 0
                || section.Values.Any(value => value.Kind != PvfTokenKind.Int32 || value.Value <= 0))
            {
                readable = false;
                continue;
            }
            for (var i = 0; i < section.Values.Count; i += 2)
                materials.Add(new(section.Values[i].Value, section.Values[i + 1].Value));
        }

        NpcShopPurchaseLimit? limit = null;
        var limits = sections.Where(section => section.Name == "[daily purchase limit]").ToArray();
        if (limits.Length > 1)
            readable = false;
        if (limits.FirstOrDefault() is { } limited)
        {
            var scope = limited.Values.Count > 0 ? Text(archive, limited.Values[0]) : null;
            if (limited.Values.Count != 2 || scope is not ("account" or "character")
                || limited.Values[1].Kind != PvfTokenKind.Int32 || limited.Values[1].Value <= 0)
                readable = false;
            else
                limit = new(scope == "account" ? NpcShopLimitScope.Account : NpcShopLimitScope.Character,
                    limited.Values[1].Value, sections.Any(section => section.Name == "[reset daily purchase item]"));
        }
        var attach = sections.FirstOrDefault(section => section.Name == "[attach type]");
        var sealedItem = attach?.Values.Count == 1 && Text(archive, attach.Values[0]) == "[sealing]";
        if (price is < 0 || durability is < 0 or > ushort.MaxValue
            || sections.Any(section => section.Name is "[coin]" or "[buy coin]" or "[cera]"
                or "[sera]" or "[cash]" or "[price type]"))
            readable = false;
        return new(itemId, price, correction, materials, limit,
            (ushort)Math.Clamp(durability, 0, ushort.MaxValue), sealedItem, readable);
    }

    private static int? Integer(IReadOnlyList<Section> sections, string name, ref bool readable)
    {
        var found = sections.Where(section => section.Name == name).ToArray();
        if (found.Length == 0)
            return null;
        if (found.Length != 1 || found[0].Values.Count != 1 || found[0].Values[0].Kind != PvfTokenKind.Int32)
        {
            readable = false;
            return null;
        }
        return found[0].Values[0].Value;
    }

    private static string? Text(PvfArchive archive, PvfToken token) =>
        token.IsString ? archive.ResolveString(token.Value).ToLowerInvariant() : null;

    private static Dictionary<int, string> Paths(PvfArchive archive, string list) =>
        PvfListFile.Read(archive, list).ToDictionary(entry => entry.Id, entry => entry.RelativePath);

    internal sealed record Section(string Name, List<PvfToken> Values);

    private static IReadOnlyList<Section> Sections(PvfArchive archive, string path)
    {
        var index = archive.FindFile(path);
        if (index < 0)
            return [];
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
