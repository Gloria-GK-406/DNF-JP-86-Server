using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Infrastructure.Content;

internal static class PvfGuildContentCatalog
{
    public static GuildContentCatalog? Read(PvfArchive archive, IReadOnlyList<PvfSection> sections)
    {
        var section = PvfSections.Single(sections, PvfField.Numbers("[guild contents list]", optional: true),
            PvfRepeats.AsUndeclared, "etc/(r)guild.etc");
        if (section is null)
            return null;

        const int width = 9;
        var values = section.Values;
        if (values.Count % width != 0)
            return null;

        var products = new Dictionary<uint, GuildContentDefinition>();
        var ids = new HashSet<int>();
        var warehouseLimit = PvfSections.SingleInt32(archive, sections,
            PvfField.Int32("[guild cargo size limit]", optional: true, minimum: 1),
            PvfRepeats.AsUndeclared, "etc/(r)guild.etc");
        GuildAttributeBonus? attributes = null;
        GuildAttributeBonus? temporaryAttributes = null;
        for (var i = 0; i < values.Count; i += width)
        {
            for (var field = 0; field < width - 1; field++)
                if (values[i + field].Kind != PvfTokenKind.Int32)
                    return null;
            if (!values[i + width - 1].IsString)
                return null;

            var id = values[i].Value;
            var kind = values[i + 1].Value;
            var value = values[i + 2].Value;
            var days = values[i + 3].Value;
            var count = values[i + 4].Value;
            var canGift = values[i + 6].Value == 1;
            var price = values[i + 7].Value;
            if (id <= 0 || !ids.Add(id))
                return null;
            if (value <= 0 || price <= 0)
                continue;

            if (kind == (int)GuildContentKind.PermanentAttributes && days == -1 && count == 0)
            {
                if (attributes is not null && attributes.BuffId != value)
                    return null;
                attributes ??= ReadAttributes(archive, value);
                if (attributes is not null)
                    products.Add((uint)id, new((uint)id, GuildContentKind.PermanentAttributes, value, price));
            }
            if (kind == (int)GuildContentKind.PartyTeleport && count == 1)
                products.Add((uint)id, new((uint)id, GuildContentKind.PartyTeleport, value, price));
            if (kind == (int)GuildContentKind.Experience && days > 0 && count == 0 && value <= 100)
                products.Add((uint)id, new((uint)id, GuildContentKind.Experience, value, price, days, canGift));
            if (kind == (int)GuildContentKind.WarehouseExpansion && days == -1 && count == 0
                && warehouseLimit is > 0 and <= ushort.MaxValue && value <= warehouseLimit)
                products.Add((uint)id, new((uint)id, GuildContentKind.WarehouseExpansion, value, price));
            if (kind == (int)GuildContentKind.TemporaryAttributes && days > 0 && count == 0)
            {
                if (temporaryAttributes is not null && temporaryAttributes.BuffId != value)
                    return null;
                temporaryAttributes ??= ReadAttributes(archive, value);
                if (temporaryAttributes is not null)
                    products.Add((uint)id, new((uint)id, GuildContentKind.TemporaryAttributes, value, price, days, canGift));
            }
        }
        var goldRename = ReadGoldRename(sections);
        if (goldRename is not null && ids.Contains((int)goldRename.ProductId))
            goldRename = null;
        return new(products, attributes, temporaryAttributes, warehouseLimit, goldRename);
    }

    private static GuildGoldRenameOffer? ReadGoldRename(IReadOnlyList<PvfSection> sections)
    {
        var section = PvfSections.Single(sections, PvfField.Numbers("[additional guild contents list]", optional: true),
            PvfRepeats.AsUndeclared, "etc/(r)guild.etc");
        const int width = 10;
        if (section is null || section.Values.Count % width != 0)
            return null;

        var values = section.Values;
        var ids = new HashSet<int>();
        const int renameKind = 20;
        GuildGoldRenameOffer? offer = null;
        for (var i = 0; i < values.Count; i += width)
        {
            for (var field = 0; field < width - 2; field++)
                if (values[i + field].Kind != PvfTokenKind.Int32)
                    return null;
            if (!values[i + 8].IsString || values[i + 9].Kind != PvfTokenKind.Int32)
                return null;

            var id = values[i].Value;
            if (id <= 0 || !ids.Add(id))
                return null;
            if (values[i + 1].Value != renameKind || values[i + 5].Value != GuildGoldRenameOffer.VoucherItemId
                || values[i + 7].Value <= 0 || values[i + 9].Value != 1)
                continue;
            if (offer is not null)
                return null;
            offer = new((uint)id, values[i + 7].Value);
        }
        return offer;
    }

    private static GuildAttributeBonus? ReadAttributes(PvfArchive archive, int buffId)
    {
        var sections = PvfDocument.TryOpen(archive, "etc/variousbufflist.etc", PvfPreamble.Ignore)?.Sections ?? [];
        var effects = new Dictionary<string, int>(StringComparer.Ordinal);
        var at = 0;
        while (at < sections.Count)
        {
            var entry = sections[at++];
            if (entry.Name != "[index]" || entry.Values is not [{ Kind: PvfTokenKind.Int32, Value: var id }] || id != buffId)
                continue;

            while (at < sections.Count && sections[at].Name != "[/index]" && sections[at].Name != "[index]")
            {
                var field = sections[at++];
                if (field.Name != "[name]" || field.Values is not [{ IsString: true, Value: var name }]
                    || at >= sections.Count || sections[at] is not { Name: "[param]", Values:
                        [{ Kind: PvfTokenKind.Int32, Value: > 0 and <= short.MaxValue and var amount },
                         { Kind: PvfTokenKind.Int32, Value: 0 }] })
                    continue;
                if (!effects.TryAdd(archive.ResolveString(name), amount))
                    return null;
            }
            break;
        }

        return effects.TryGetValue("physical_attack", out var physicalAttack)
            && effects.TryGetValue("physical_defence", out var physicalDefense)
            && effects.TryGetValue("magical_attack", out var magicalAttack)
            && effects.TryGetValue("magical_defence", out var magicalDefense)
                ? new(buffId, physicalAttack, physicalDefense, magicalAttack, magicalDefense)
                : null;
    }
}
