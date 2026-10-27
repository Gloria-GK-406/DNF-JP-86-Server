using Dfo.Pvf.Schema;
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
internal sealed class PvfEquipmentSlotDefinitionSource : IEquipmentSlotDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<string> EquipmentTypeField = PvfField.Custom<string>("[equipment type]", (archive, values, source) =>
        {
            if (values.Count is < 1 or > 2 || !values[0].IsString ||
                values.Count == 2 && (values[1].Kind != PvfTokenKind.Int32 || values[1].Value < 0))
                throw new InvalidDataException($"PVF '{source}', field '[equipment type]': expected type name and optional nonnegative Int32 subtype.");
            return archive.ResolveString(values[0].Value);
        }, optional: true);
    }

    private const string EquipmentListPath = "equipment/equipment.lst";
    private const string EquipmentDirectory = "equipment";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, BusinessSlot?> _cache = [];
    private IReadOnlyDictionary<int, string>? _paths;

    public string Name => "equipment-slot-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            PathsIndex(_provider.Archive);
            return ValueTask.FromResult(_paths.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_paths))]
    private void PathsIndex(Dfo.Pvf.PvfArchive archive)
    {
        if (_paths is null)
        {
            var paths = new Dictionary<int, string>();
            foreach (var (id, relativePath) in PvfListFile.Read(archive, EquipmentListPath))
                paths.TryAdd(id, relativePath);
            _paths = paths;
        }
    }

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
        PathsIndex(archive);

        if (!_paths.TryGetValue(itemId, out var path))
            return null;

        var fileIndex = archive.FindFile($"{EquipmentDirectory}/{path}");
        if (fileIndex < 0)
            return null;

        try
        {
            var document = PvfDocument.Open(archive, $"{EquipmentDirectory}/{path}").AtRoot();
            return document.TryRead(Fields.EquipmentTypeField, out var type)
                && PvfEquipmentSlots.BusinessByName.TryGetValue(type, out var slot) ? slot : null;
        }
        catch (InvalidDataException) { return null; }

    }
}
