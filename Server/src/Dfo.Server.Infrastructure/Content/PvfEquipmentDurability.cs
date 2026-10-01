using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// What a piece of equipment's own definition declares its durability to be.
/// </summary>
/// <remarks>
/// Shared because it is one fact with two readers: the pieces a character is created wearing and
/// the ones a room's breakable object yields both reach the wire carrying it, and a piece created
/// at zero durability is a broken one whichever way it arrived.
/// </remarks>
internal static class PvfEquipmentDurability
{
    private static class Fields
    {
        internal static readonly PvfField<int> DurabilityField = PvfField.Int32("[durability]", optional: true, minimum: 0, maximum: ushort.MaxValue);
    }

    private const string EquipmentDirectory = "equipment";

    public static ushort Read(
        PvfArchive archive, IReadOnlyDictionary<int, string> equipmentPaths, int itemId)
    {
        if (!equipmentPaths.TryGetValue(itemId, out var relativePath))
            return 0;

        var fileIndex = archive.FindFile($"{EquipmentDirectory}/{relativePath}");
        if (fileIndex < 0)
            return 0;

        var document = PvfDocument.Open(archive, $"{EquipmentDirectory}/{relativePath}").AtRoot();
        return document.TryRead(Fields.DurabilityField, out var value) ? checked((ushort)value) : (ushort)0;
    }
}
