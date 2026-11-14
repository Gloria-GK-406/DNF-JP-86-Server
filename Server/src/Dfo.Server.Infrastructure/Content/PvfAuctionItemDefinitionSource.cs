using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Auction;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>The PVF facts the auction house lists and searches by.</summary>
/// <remarks>
/// <c>[packagable]</c> is the 70 server's main switch (<c>CItem::isPackagable</c>, 70Reference); a
/// template without the field is packagable. A declared usable period or expiration date marks a
/// timed template, which the 70 server refuses. The family is derived from <c>[equipment type]</c>
/// for the client's category tree.
/// </remarks>
internal sealed class PvfAuctionItemDefinitionSource(PvfArchiveProvider provider)
    : IAuctionItemDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> PackagableField = PvfField.Int32List("[packagable]", optional: true);
        internal static readonly PvfField<int> CashField = PvfField.Int32("[cash]", optional: true);
        internal static readonly PvfField<int> RarityField = PvfField.Int32("[rarity]", optional: true);
        internal static readonly PvfField<int> MinimumLevelField = PvfField.Int32("[minimum level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> UsablePeriodField = PvfField.Tokens("[usable period]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ExpirationDateField = PvfField.Tokens("[expiration date]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> EquipmentTypeField = PvfField.Tokens("[equipment type]", optional: true);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, AuctionItemDefinition?> cache = [];
    private IReadOnlyDictionary<int, string>? equipmentPaths;
    private IReadOnlyDictionary<int, string>? stackablePaths;

    public string Name => "auction-item-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Index();
            return ValueTask.FromResult(equipmentPaths!.Count + stackablePaths!.Count);
        }
    }

    public ValueTask<AuctionItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!cache.TryGetValue(itemId, out var definition))
                cache[itemId] = definition = Read(itemId);
            return ValueTask.FromResult(definition);
        }
    }

    private void Index()
    {
        var archive = provider.Archive;
        equipmentPaths ??= PvfListFile.Paths(archive, "equipment/equipment.lst");
        stackablePaths ??= PvfListFile.Paths(archive, "stackable/stackable.lst");
    }

    private AuctionItemDefinition? Read(int itemId)
    {
        Index();
        var archive = provider.Archive;
        var equipment = equipmentPaths!.TryGetValue(itemId, out var equipmentPath);
        var path = equipment ? "equipment/" + equipmentPath
            : stackablePaths!.TryGetValue(itemId, out var stackablePath) ? "stackable/" + stackablePath : null;
        if (path is null || archive.FindFile(path) < 0) return null;

        var document = PvfDocument.Open(archive, path);
        var packagable = !document.TryRead(Fields.PackagableField, out var flags) || flags.All(flag => flag != 0);
        var cash = document.TryRead(Fields.CashField, out var price) ? price : 0;
        var timed = document.TryRead(Fields.UsablePeriodField, PvfItemFieldRepeats.For(Fields.UsablePeriodField), out _) || document.TryRead(Fields.ExpirationDateField, out _);
        var rarity = document.TryRead(Fields.RarityField, out var r) ? (byte)Math.Clamp(r, 0, 255) : (byte)0;
        var level = document.TryRead(Fields.MinimumLevelField, out var l) ? (byte)Math.Clamp(l, 0, 255) : (byte)0;
        var family = AuctionItemFamily.Stackable;
        if (equipment)
        {
            var type = document.TryRead(Fields.EquipmentTypeField, PvfItemFieldRepeats.For(Fields.EquipmentTypeField), out var tokens) && tokens.Count > 0 && tokens[0].IsString
                ? archive.ResolveString(tokens[0].Value) : string.Empty;
            family = FamilyOf(type);
        }
        return new AuctionItemDefinition(packagable, cash, timed, rarity, level, family);
    }

    internal static AuctionItemFamily FamilyOf(string equipmentType)
    {
        var type = equipmentType.Trim().ToLowerInvariant();
        if (type.Contains("avatar", StringComparison.Ordinal)) return AuctionItemFamily.Avatar;
        if (type.Contains("weapon", StringComparison.Ordinal)) return AuctionItemFamily.Weapon;
        if (type is "[coat]" or "[shoulder]" or "[pants]" or "[legs]" or "[shoes]" or "[waist]" or "[belt]")
            return AuctionItemFamily.Armor;
        if (type is "[amulet]" or "[ring]" or "[wrist]" or "[neck]" or "[bracelet]") return AuctionItemFamily.Accessory;
        if (type.Contains("title", StringComparison.Ordinal)) return AuctionItemFamily.TitleName;
        if (type.Contains("support", StringComparison.Ordinal) || type.Contains("magic stone", StringComparison.Ordinal))
            return AuctionItemFamily.SpecialEquipment;
        return AuctionItemFamily.Other;
    }
}
