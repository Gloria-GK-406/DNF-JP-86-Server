namespace Dfo.Server.Abstractions.Content;

/// <summary>Where the archive says an item may be used.</summary>
public enum ItemUsePlace
{
    /// <summary>The content named a place this contract does not model.</summary>
    Unrecognized = 0,
    Village,
    Dungeon,
    SeriaRoom,
    Tournament,
}

/// <summary>Which jobs the archive lets use an item.</summary>
/// <remarks>
/// The vocabulary is the quest catalogue's, reused rather than restated: the archive spells a
/// job the same way wherever it names one, and a second enumeration of the same fourteen families
/// would be a second thing to keep correct when a family is added.
/// </remarks>
public sealed record ItemJobRestriction(
    bool IsExactAll,
    IReadOnlyList<QuestJobRequirement> JobFamilies)
{
    /// <summary>What an item declaring <c>[all]</c> carries.</summary>
    public static readonly ItemJobRestriction Any = new(IsExactAll: true, JobFamilies: []);

    public bool IsValid => IsExactAll
        ? JobFamilies.Count == 0
        : JobFamilies.Count > 0
            && JobFamilies.All(family => family is not (
                QuestJobRequirement.Any or QuestJobRequirement.Unrecognized));

    public bool Allows(QuestJobRequirement job) =>
        IsExactAll || job is not QuestJobRequirement.Unrecognized && JobFamilies.Contains(job);
}

/// <summary>Where the archive lets an item be used.</summary>
public sealed record ItemPlaceRestriction(
    bool IsUnrestricted,
    IReadOnlyList<ItemUsePlace> Places)
{
    /// <summary>What an item declaring no place at all carries.</summary>
    public static readonly ItemPlaceRestriction Anywhere = new(IsUnrestricted: true, Places: []);

    public bool IsValid => IsUnrestricted
        ? Places.Count == 0
        : Places.Count > 0 && Places.All(place => place is not ItemUsePlace.Unrecognized);

    public bool Allows(ItemUsePlace place) => IsUnrestricted || Places.Contains(place);
}

/// <summary>
/// What the archive declares about using one item: who, when, where, and whether it survives.
/// </summary>
/// <param name="IsConsumedOnUse">
/// False for the eighty-four items whose <c>[stackable type]</c> begins with <c>unlimited</c>.
/// For those, taking one off the pile is not a cost but a destruction.
/// </param>
/// <param name="CooldownMilliseconds">Zero when the archive declares no cooldown.</param>
/// <param name="CooltimeGroup">
/// Zero when the item shares its cooldown with nothing. The archive writes a literal <c>0</c> for
/// that case as often as it omits the tag, so the two are the same answer.
/// </param>
/// <param name="HasUnreadableDeclaration">
/// True when the archive declares one of the scalar tags -- the cooldown, its group, the minimum
/// level, the stackable type -- and no value could be read from it. Absence is not this: an item
/// that declares no cooldown has none, while an item that declares one nobody can read is an item
/// whose rules are unknown.
/// </param>
public sealed record ItemUseRule(
    bool IsConsumedOnUse,
    int CooldownMilliseconds,
    int CooltimeGroup,
    int MinimumLevel,
    ItemJobRestriction Jobs,
    ItemPlaceRestriction Places,
    bool HasUnreadableDeclaration = false)
{
    /// <summary>
    /// Whether every restriction this item declares could be read.
    /// </summary>
    /// <remarks>
    /// An item whose declaration names a job family or a place this contract does not model is
    /// unreadable rather than unrestricted. Treating an unknown token as "no restriction" would
    /// hand the player an item the archive meant to withhold, and nothing would report it.
    /// <para>
    /// The same applies to a scalar declaration with nothing readable behind it. An unreadable
    /// <c>[stackable type]</c> is the sharpest case: read as absence it means "consumed", and the
    /// item this whole outcome exists to stop destroying gets destroyed.
    /// </para>
    /// </remarks>
    public bool IsReadable => Jobs.IsValid && Places.IsValid && !HasUnreadableDeclaration;
}

/// <summary>Answers what the archive declares about using an item.</summary>
public interface IItemUseRuleDefinitionSource
{
    /// <summary>
    /// The rule for this item, or null when the archive declares no usable item under that
    /// identifier.
    /// </summary>
    ValueTask<ItemUseRule?> GetAsync(int itemId, CancellationToken cancellationToken);
}
