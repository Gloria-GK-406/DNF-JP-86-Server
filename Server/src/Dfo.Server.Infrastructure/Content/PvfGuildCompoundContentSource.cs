using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfGuildCompoundContentSource(PvfArchiveProvider provider, PvfContentOptions options)
    : IGuildCompoundContentSource
{
    private const string RulesPath = "etc/guilddungeoncompound.etc";
    private static readonly PvfField<int> Rarity = PvfField.Int32("[rarity]", minimum: 0, maximum: 6);
    private static readonly PvfField<IReadOnlyList<PvfToken>> StackableType = PvfField.Tokens("[stackable type]");
    private static readonly PvfField<IReadOnlyList<PvfToken>> EquipmentType = PvfField.Tokens("[equipment type]");

    private readonly Lazy<IReadOnlyDictionary<GuildCompoundKind, Catalog>> content = new(() =>
        options.ResolveArchivePath() is null ? new Dictionary<GuildCompoundKind, Catalog>() : Read(provider.Archive));

    public GuildCompoundRule? FindRule(GuildCompoundKind kind, int rarity) =>
        content.Value.GetValueOrDefault(kind)?.Rules.GetValueOrDefault(rarity);

    public int? GetRarity(GuildCompoundKind kind, int itemId) =>
        content.Value.TryGetValue(kind, out var catalog) && catalog.Rarities.TryGetValue(itemId, out var rarity)
            ? rarity : null;

    private static Dictionary<GuildCompoundKind, Catalog> Read(PvfArchive archive)
    {
        if (PvfDocument.TryOpen(archive, RulesPath) is not { } rules) return [];
        return new()
        {
            [GuildCompoundKind.GuardianGem] = ReadCatalog(rules.AtRoot().Read(PvfField.Int32List("[compound info]")),
                archive, "stackable", "flaggem/", StackableType, "[flag gem]"),
            [GuildCompoundKind.Medal] = ReadCatalog(rules.AtRoot().Read(PvfField.Int32List("[guild flag compound info]")),
                archive, "equipment", "character/common/flag/", EquipmentType, "[flag]"),
        };
    }

    private static Catalog ReadCatalog(IReadOnlyList<int> values, PvfArchive archive,
        string tree, string directory, PvfField<IReadOnlyList<PvfToken>> type, string tag)
    {
        const int RarityScale = 10_000;
        PvfField.Require(values.Count > 0 && values.Count % 3 == 0, RulesPath, tag, "expected rarity, maximum and upgrade rate");
        var rarities = new Dictionary<int, int>();
        foreach (var (id, name) in PvfListFile.Paths(archive, $"{tree}/{tree}.lst", PvfListDuplicates.First))
        {
            var file = name.Replace('\\', '/');
            if (!file.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) continue;
            var item = PvfDocument.Open(archive, $"{tree}/{file}").AtRoot();
            if (item.TryRead(type, out var tokens) && tokens.Count > 0 && tokens[0].IsString
                && string.Equals(archive.ResolveString(tokens[0].Value), tag, StringComparison.OrdinalIgnoreCase))
                rarities.Add(id, item.Read(Rarity));
        }

        var pools = rarities.GroupBy(item => item.Value).ToDictionary(group => group.Key,
            group => group.Select(item => item.Key).Order().ToArray());
        var table = new Dictionary<int, GuildCompoundRule>();
        for (var i = 0; i < values.Count; i += 3)
        {
            var rarity = values[i];
            var maximum = values[i + 1];
            var rate = values[i + 2];
            var products = pools.GetValueOrDefault(rarity) ?? [];
            var upgradedProducts = pools.Where(pool => pool.Key > rarity).OrderBy(pool => pool.Key)
                .Select(pool => pool.Value).FirstOrDefault() ?? [];
            PvfField.Require(rarity is >= 0 and <= 6 && maximum is >= 2 and <= 4
                && rate >= 0 && rate <= RarityScale / maximum && products.Length > 0
                && (rate == 0 || upgradedProducts.Length > 0)
                && table.TryAdd(rarity, new(maximum, rate, products, upgradedProducts)),
                RulesPath, tag, "invalid or duplicate rarity rule or missing product pool");
        }
        return new(table, rarities);
    }

    private sealed record Catalog(IReadOnlyDictionary<int, GuildCompoundRule> Rules, IReadOnlyDictionary<int, int> Rarities);
}
