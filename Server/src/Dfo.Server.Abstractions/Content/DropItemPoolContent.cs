using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Abstractions.Content;

/// <summary>Which of the archive's two item families a pool was built from.</summary>
public enum DropItemFamily
{
    /// <summary>Everything under <c>equipment/equipment.lst</c>.</summary>
    Equipment = 0,

    /// <summary>Everything under <c>stackable/stackable.lst</c>.</summary>
    Stackable = 1,
}

/// <summary>One item a pool may produce, and what it weighs against its neighbours.</summary>
/// <param name="Weight">
/// The item's own <c>[creation rate]</c>. Equipment declares seventeen different values between
/// 30 and 1125; stackables almost all declare 1000. It is a weight within a bucket, not a
/// probability of anything.
/// </param>
/// <param name="Durability">
/// How much wear the piece is created with, from its own <c>[durability]</c>. Carried here rather
/// than looked up when it is picked up, because the file was already open when the pool was built
/// and because <b>a piece created at zero durability is a broken one</b> -- the same reason the
/// declared floor drops read it out of the archive rather than defaulting it. Stackables declare
/// none and read as zero, which is what they should be.
/// </param>
public readonly record struct DropPoolEntry(int ItemId, int Weight, ushort Durability = 0);

/// <summary>
/// The items an occasion may produce, bucketed by grade and rarity.
/// </summary>
/// <remarks>
/// <b>What decides whether an item is in here is the item itself.</b> Both families are gated the
/// same way: a file that declares <c>[creation rate]</c> above zero is creatable, and one that
/// declares nothing is not. 6062 of the archive's 118479 equipment files pass, and 774 of its
/// 42272 stackables.
/// <para>
/// The reference server gets this right for stackables and wrong for equipment, where it reads
/// <c>Etc/ItemDictionary/ItemDictionary.etc</c> instead. That file is the encyclopedia UI's index:
/// it holds 13474 of the 118479, its second column is a display class rather than the item's
/// rarity -- the two disagree for five of its eight values -- its third column is the item's
/// minimum level rather than its grade, and the column it takes for a weight has only the values
/// zero and one. The reference server is inconsistent with itself here, which is the tell.
/// </para>
/// </remarks>
public sealed class DropItemPool
{
    private readonly IReadOnlyDictionary<(int Grade, int Rarity), IReadOnlyList<DropPoolEntry>>
        _buckets;

    public DropItemPool(
        DropItemFamily family,
        IReadOnlyDictionary<(int Grade, int Rarity), IReadOnlyList<DropPoolEntry>> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        this.Family = family;
        this._buckets = buckets;
        this.Count = buckets.Values.Sum(bucket => bucket.Count);
    }

    /// <summary>Which family this was built from.</summary>
    public DropItemFamily Family { get; }

    /// <summary>How many items are in it, across every bucket.</summary>
    public int Count { get; }

    /// <summary>What this grade and rarity holds, or nothing when it holds nothing.</summary>
    public IReadOnlyList<DropPoolEntry> Bucket(int grade, DropRarity rarity) =>
        this._buckets.TryGetValue((grade, (int)rarity), out var bucket) ? bucket : [];

    /// <summary>Everything of that rarity whose grade falls inside the band.</summary>
    /// <remarks>
    /// Gathered rather than pre-joined: a band spans a handful of grades and which handful depends
    /// on the level, so there is nothing to precompute that is not just the buckets again.
    /// </remarks>
    public IReadOnlyList<DropPoolEntry> Band(ItemGradeBand band, DropRarity rarity)
    {
        var gathered = new List<DropPoolEntry>();
        for (var grade = band.Low; grade <= band.High; grade++)
            gathered.AddRange(this.Bucket(grade, rarity));
        return gathered;
    }
}

/// <summary>Reads one of the archive's two families into a pool.</summary>
public interface IDropItemPoolSource
{
    /// <summary>The pool this family declares. Read once and held.</summary>
    DropItemPool Load(DropItemFamily family);
}
