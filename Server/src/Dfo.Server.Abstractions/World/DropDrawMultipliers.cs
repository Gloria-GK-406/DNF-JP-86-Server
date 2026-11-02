using System.Collections.Frozen;

namespace Dfo.Server.Abstractions.World;

/// <summary>One independently configurable group of ground-drop draws.</summary>
/// <remarks>
/// A draw belongs to exactly one entry, so no draw can be multiplied twice. The numeric values salt
/// the extra rounds' random streams, and <see cref="DropDrawEntries"/> holds the persisted keys:
/// neither may change once a value has shipped.
/// </remarks>
public enum DropDrawEntry
{
    MonsterGold = 1,
    MonsterStackable = 2,
    MonsterEquipment = 3,
    MonsterExtra = 4,
    MonsterDeclared = 5,
    IndependentMonster = 6,
    IndependentApc = 7,
    World = 8,
    ObjectRandomGold = 9,
    ObjectRandomItem = 10,
    ObjectSpecific = 11,
    HellEquipment = 12,
    HellEpicPiece = 13,
    DimensionEquipment = 14,
    DimensionMaterial = 15,
    LostLandRegular = 16,
    LostLandBoss = 17,
}

public static class DropDrawEntries
{
    private static readonly FrozenDictionary<DropDrawEntry, string> Keys = new Dictionary<DropDrawEntry, string>
    {
        [DropDrawEntry.MonsterGold] = "monster.gold",
        [DropDrawEntry.MonsterStackable] = "monster.stackable",
        [DropDrawEntry.MonsterEquipment] = "monster.equipment",
        [DropDrawEntry.MonsterExtra] = "monster.extra",
        [DropDrawEntry.MonsterDeclared] = "monster.declared",
        [DropDrawEntry.IndependentMonster] = "independent.monster",
        [DropDrawEntry.IndependentApc] = "independent.apc",
        [DropDrawEntry.World] = "world",
        [DropDrawEntry.ObjectRandomGold] = "object.random.gold",
        [DropDrawEntry.ObjectRandomItem] = "object.random.item",
        [DropDrawEntry.ObjectSpecific] = "object.specific",
        [DropDrawEntry.HellEquipment] = "hell.equipment",
        [DropDrawEntry.HellEpicPiece] = "hell.epic_piece",
        [DropDrawEntry.DimensionEquipment] = "dimension.equipment",
        [DropDrawEntry.DimensionMaterial] = "dimension.material",
        [DropDrawEntry.LostLandRegular] = "lostland.regular",
        [DropDrawEntry.LostLandBoss] = "lostland.boss",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, DropDrawEntry> Entries =
        Keys.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    public static IEnumerable<DropDrawEntry> All => Keys.Keys;

    public static string KeyOf(DropDrawEntry entry) => Keys[entry];

    public static bool TryParse(string key, out DropDrawEntry entry) => Entries.TryGetValue(key, out entry);
}

/// <summary>Which repetition of an entry's draw unit this is. The default is the original draw.</summary>
public readonly record struct DropDrawRound(DropDrawEntry Entry, int Round);

/// <summary>The multipliers in force at one generation moment, handed to rules as a fact.</summary>
public sealed class DropDrawMultipliers
{
    public const int Minimum = 1;
    public const int Maximum = 100;

    public static DropDrawMultipliers Default { get; } = new(new Dictionary<DropDrawEntry, int>());

    private readonly IReadOnlyDictionary<DropDrawEntry, int> values;

    public DropDrawMultipliers(IReadOnlyDictionary<DropDrawEntry, int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Values.Any(value => !IsValid(value)))
            throw new ArgumentOutOfRangeException(nameof(values));
        this.values = values;
    }

    public static bool IsValid(int multiplier) => multiplier is >= Minimum and <= Maximum;

    public int For(DropDrawEntry entry) => this.values.GetValueOrDefault(entry, Minimum);
}

/// <summary>Reads the extra-draw multiplier of each ground-drop entry.</summary>
public interface IDropDrawMultiplierDomainService
{
    ValueTask<DropDrawMultipliers> CurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Sets the multipliers; the drop rate activity is its only production consumer.</summary>
public interface IDropDrawMultiplierControlDomainService
{
    /// <summary>Sets one entry; a value outside 1..100 is rejected.</summary>
    ValueTask SetAsync(DropDrawEntry entry, int multiplier, CancellationToken cancellationToken);
}

public interface IDropDrawMultiplierPersistence
{
    ValueTask<IReadOnlyList<(string Key, int Multiplier)>> ReadAllAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(string key, int multiplier, CancellationToken cancellationToken);
}
