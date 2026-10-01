using Dfo.Pvf.Schema;
using System.Collections.Concurrent;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Builds a drop pool by asking every item in a family whether it may be created.
/// </summary>
/// <remarks>
/// There is no index to read. The archive's two list files name every equipment and stackable file
/// there is, and what decides whether one of them may be produced is a section inside that file --
/// so the whole family is walked once, on the first drop that needs it, and held.
/// <para>
/// That is 118479 equipment files and 42272 stackables, which takes a few seconds. It is done
/// lazily rather than at startup so that a server nobody is playing on does not pay for it, and
/// once rather than per kill for the obvious reason.
/// </para>
/// <para>
/// <b>The alternative is an index that does not mean what it looks like it means.</b>
/// <c>ItemDictionary.etc</c> is one row per item and would be a hundred times cheaper to read; it
/// is also the encyclopedia's, holds an eighth of the items, and its columns are not the ones they
/// appear to be. Cheap and wrong was the choice already made once here.
/// </para>
/// </remarks>
internal sealed class PvfDropItemPoolSource(PvfArchiveProvider provider) : IDropItemPoolSource
{
    private const string EquipmentList = "equipment/equipment.lst";
    private const string StackableList = "stackable/stackable.lst";

    private static readonly PvfField<int> GradeSection = PvfField.Int32("[grade]", optional: true);
    private static readonly PvfField<int> RaritySection = PvfField.Int32("[rarity]", optional: true);
    private static readonly PvfField<int> CreationRateSection = PvfField.Int32("[creation rate]", optional: true);
    private static readonly PvfField<int> DurabilitySection = PvfField.Int32("[durability]", optional: true, minimum: 0, maximum: ushort.MaxValue);

    /// <remarks>
    /// <see cref="Lazy{T}"/> rather than the value directly, because a concurrent dictionary does
    /// not hold a lock while its factory runs: without this, N kills arriving on the first drop of
    /// the process would each walk 118479 archive files, and N-1 of those walks would be thrown
    /// away. The default thread-safety mode is exactly the one wanted -- one walk, and everybody
    /// else waits for it.
    /// </remarks>
    private readonly ConcurrentDictionary<DropItemFamily, Lazy<DropItemPool>> _pools = new();

    public DropItemPool Load(DropItemFamily family) =>
        _pools.GetOrAdd(family, key => new Lazy<DropItemPool>(() => this.Read(key))).Value;

    private DropItemPool Read(DropItemFamily family)
    {
        var (listPath, prefix) = family switch
        {
            DropItemFamily.Equipment => (EquipmentList, "equipment/"),
            DropItemFamily.Stackable => (StackableList, "stackable/"),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "No such family."),
        };

        var archive = provider.Archive;
        var buckets = new Dictionary<(int Grade, int Rarity), List<DropPoolEntry>>();

        foreach (var (itemId, itemPath) in ReadList(archive, listPath, prefix))
        {
            var fileIndex = archive.FindFile(itemPath);
            if (fileIndex < 0)
                continue;

            if (ReadCreatable(archive, itemPath) is not { } declared)
                continue;

            var (grade, rarity, creationRate, durability) = declared;

            // The gate, and the only one. An item that declares no creation rate is not creatable;
            // one that declares zero has been switched off on purpose, and 9505 pieces of
            // equipment do exactly that.
            if (creationRate <= 0 || grade < 0 || rarity < 0)
                continue;

            var key = (grade, rarity);
            if (!buckets.TryGetValue(key, out var bucket))
                buckets[key] = bucket = [];
            bucket.Add(new DropPoolEntry(itemId, creationRate, durability));
        }

        if (buckets.Count == 0)
        {
            throw new InvalidDataException(
                $"'{listPath}' yielded no creatable items; the drop pool would be empty.");
        }

        return new DropItemPool(
            family,
            buckets.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<DropPoolEntry>)pair.Value));
    }

    /// <summary>
    /// What one item declares of the three fields a pool needs, or nothing when it declares none.
    /// </summary>
    /// <remarks>
    /// The whole file is walked rather than stopped at the third find, because these sections are
    /// not in a fixed order and an early stop would depend on one. It is one pass over a few
    /// hundred tokens either way.
    /// </remarks>
    private static (int Grade, int Rarity, int CreationRate, ushort Durability)? ReadCreatable(
        PvfArchive archive, string path)
    {
        var document = PvfDocument.Open(archive, path).AtRoot();
        int Read(PvfField<int> field)
        {
            // Merged item definitions legally repeat these root declarations. Validate every
            // occurrence, then keep the original first nonnegative projection.
            var values = document.ReadAll(field);
            return values.FirstOrDefault(value => value >= 0, -1);
        }
        var grade = Read(GradeSection);
        var rarity = Read(RaritySection);
        var creationRate = Read(CreationRateSection);
        var durability = Read(DurabilitySection);
        return creationRate < 0 ? null : (grade, rarity, creationRate,
            (ushort)Math.Clamp(durability, 0, ushort.MaxValue));
    }

    /// <summary>
    /// One of the archive's list files, as identifier and path pairs.
    /// </summary>
    /// <remarks>
    /// The paths inside are relative to the list's own directory, which is why the caller supplies
    /// the prefix. The pairing is positional: an identifier followed by the string that names its
    /// file.
    /// </remarks>
    private static IEnumerable<(int ItemId, string Path)> ReadList(
        PvfArchive archive, string listPath, string prefix)
    {
        if (archive.FindFile(listPath) < 0)
            throw new InvalidDataException($"The archive declares no '{listPath}'.");

        var pairs = new List<(int, string)>();
        var reader = archive.OpenScript(listPath);
        int? pending = null;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Int32)
            {
                pending = token.Value;
            }
            else if (token.IsString && pending is { } itemId)
            {
                pairs.Add((itemId, prefix + archive.ResolveString(token.Value)));
                pending = null;
            }
        }

        return pairs;
    }
}
