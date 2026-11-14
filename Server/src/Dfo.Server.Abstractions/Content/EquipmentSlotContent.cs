using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// Which slot of the equipment container a piece is worn in.
/// </summary>
/// <remarks>
/// The archive answers this directly: every wearable declares <c>[equipment type]</c>, and the name
/// it gives -- <c>[shoulder]</c>, <c>[weapon]</c>, <c>[coat]</c> -- is the same vocabulary the
/// character-creation table uses to say which slot a starting piece fills. The slot numbers are
/// therefore one table, read twice, rather than two tables that agree until they do not.
/// </remarks>
public interface IEquipmentSlotDefinitionSource
{
    /// <summary>
    /// The slot this item is worn in, or null when the archive declares nothing wearable under
    /// that identifier.
    /// </summary>
    ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Where this weapon goes when a career carries it as its second weapon, and the weapon
    /// <c>[sub type]</c> that career's allowance is written in; null for anything that is not a
    /// <c>[weapon]</c> with a declared sub type.
    /// </summary>
    /// <remarks>
    /// The slot is the one <c>[support weapon]</c> names. Whether a particular character may use it
    /// is a career question (<see cref="ICharacterSupportWeaponDefinitionSource"/>); this only says
    /// what the weapon itself declares.
    /// </remarks>
    ValueTask<SupportWeaponCandidate?> GetSupportWeaponAsync(int itemId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<SupportWeaponCandidate?>(null);
}

/// <summary>A weapon's second-weapon slot and the sub type a career allowance is matched against.</summary>
/// <remarks>The sub type is job-relative in the archive: 5 is a beam sword for a swordman and a tonfa for a fighter.</remarks>
public sealed record SupportWeaponCandidate(BusinessSlot Slot, int SubType);
