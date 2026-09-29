using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads which slot a wearable is worn in, out of its own definition.
/// </summary>
/// <remarks>
/// <c>[equipment type]</c> is a quoted name and then a number: <c>[shoulder] 13</c>. The name is
/// the slot; the number after it is a sub-type the client uses to pick an icon and a sprite, and
/// nothing here reads it.
/// <para>
/// This is what makes an equip checkable. The client works the slot out for itself and will not
/// offer a drop it believes is illegal, so a request naming the wrong slot cannot be produced from
/// a stock client -- which is exactly why the server has to know the answer rather than take the
/// request's word for it.
/// </para>
/// </remarks>
internal sealed class PvfEquipmentSlotDefinitionSource : IEquipmentSlotDefinitionSource
{
    private const string EquipmentListPath = "equipment/equipment.lst";
    private const string EquipmentDirectory = "equipment";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, BusinessSlot?> _cache = [];
    private IReadOnlyDictionary<int, string>? _paths;

    public PvfEquipmentSlotDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_cache.TryGetValue(itemId, out var cached))
                return ValueTask.FromResult(cached);

            var slot = Read(_provider.Archive, itemId);
            _cache[itemId] = slot;
            return ValueTask.FromResult(slot);
        }
    }

    private BusinessSlot? Read(PvfArchive archive, int itemId)
    {
        if (_paths is null)
        {
            var paths = new Dictionary<int, string>();
            foreach (var (id, relativePath) in PvfListFile.Read(archive, EquipmentListPath))
                paths.TryAdd(id, relativePath);
            _paths = paths;
        }

        if (!_paths.TryGetValue(itemId, out var path))
            return null;

        var fileIndex = archive.FindFile($"{EquipmentDirectory}/{path}");
        if (fileIndex < 0)
            return null;

        var tag = archive.GetTag("[equipment type]");
        if (!tag.IsResolved)
            return null;

        var reader = archive.OpenScript(fileIndex);
        if (!reader.TryMovePast(tag)
            || !reader.TryReadSectionValue(out var value)
            || !value.IsString)
        {
            return null;
        }

        return PvfEquipmentSlots.BusinessByName.TryGetValue(archive.ResolveString(value.Value), out var slot)
            ? slot
            : null;
    }
}
