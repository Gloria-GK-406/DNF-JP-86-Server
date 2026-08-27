namespace Dfo.Server.Abstractions.Content;

/// <summary>One piece of equipment a job's characters are created wearing.</summary>
/// <param name="SlotIndex">
/// The equipment container slot the piece occupies. The archive names the slot rather than
/// numbering it -- <c>[weapon]</c>, <c>[coat]</c>, <c>[pants]</c> -- so the number is this
/// repository's, resolved from the name; see <c>protocol/game/server-to-client/NOTI-0x0002.md</c>
/// for the slot table the client reads.
/// </param>
/// <param name="Durability">
/// What the item's own definition declares. Carried with the grant because a piece created at
/// zero durability is a broken one: the client draws it, and refuses to let it act.
/// </param>
public readonly record struct InitialEquipmentGrant(byte SlotIndex, int ItemId, ushort Durability);

/// <summary>The equipment a job's characters are created wearing.</summary>
public interface IInitialEquipmentDefinitionSource
{
    /// <summary>
    /// The grants for this job in the order the archive declares them, or an empty list when the
    /// archive declares no such job or no starting equipment for it.
    /// </summary>
    ValueTask<IReadOnlyList<InitialEquipmentGrant>> GetAsync(
        byte job, CancellationToken cancellationToken);
}
