using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Protocol.Game.DungeonEntry;

/// <summary>
/// One piece lying on a room's floor, as the room's description announces it.
/// </summary>
/// <param name="WireValue">
/// The one field of the announcement whose meaning depends on what the piece is. See
/// <see cref="FloorPieceValues"/>.
/// </param>
internal sealed record FloorPiece(ushort Number, DungeonRoomDrop Drop, uint WireValue);

/// <summary>
/// Works out what to announce in the drop record's dual-purpose field.
/// </summary>
/// <remarks>
/// The room's description gives each piece on the floor a 32-bit value, and the client reads it as
/// two different things depending on the item: a stack's count for anything that stacks, and the
/// discriminator that tells one piece of equipment from another otherwise. That is not this
/// server's invention -- it is the same double meaning the <c>value</c> column carries in the item
/// tables, where it is the count for a pile and the instance value for a piece of gear.
/// <para>
/// <b>Announcing the discriminator for everything was a real defect.</b> A player picked up a
/// material on 2026-08-29 whose count was drawn as several hundred thousand, because what the
/// floor carried was a 31-bit pseudo-random number. Equipment was right by accident: it is the
/// only kind for which that field <i>is</i> the discriminator.
/// </para>
/// <para>
/// The discriminator is still derived rather than allocated, and still has to match what the
/// pick-up banks -- a room re-described mid-run has to name the same piece the same way, and the
/// piece in the bag has to be recognisably the piece that was on the floor.
/// </para>
/// </remarks>
internal static class FloorPieceValues
{
    /// <summary>The kind whose value field is a discriminator rather than a count.</summary>
    private const byte EquipmentItemKind = 1;

    /// <summary>
    /// Describes each remaining piece, asking the archive what kind it is.
    /// </summary>
    /// <remarks>
    /// An item the archive does not declare is announced with its declared count rather than
    /// refused. The room is being described; a piece nobody can look up is a piece the client will
    /// draw anyway, and a count is the reading that cannot put an absurd number on the floor.
    /// </remarks>
    public static async ValueTask<IReadOnlyList<FloorPiece>> DescribeAsync(
        DungeonRunState run,
        IReadOnlyList<GroundDrop> drops,
        IItemPlacementDefinitionSource placements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(drops);
        ArgumentNullException.ThrowIfNull(placements);

        if (drops.Count == 0)
            return [];

        var pieces = new List<FloorPiece>(drops.Count);
        foreach (var drop in drops)
        {
            var placement = await placements.GetAsync(drop.Drop.ItemId, cancellationToken)
                .ConfigureAwait(false);
            var isEquipment = placement?.ItemKind == EquipmentItemKind;
            pieces.Add(new FloorPiece(
                drop.Number,
                drop.Drop,
                isEquipment
                    ? DungeonEntryPayloadCodec.DropInstance(run, drop.Drop.ObjectIndex)
                    : checked((uint)Math.Max(1, drop.Drop.Count))));
        }

        return pieces;
    }
}
