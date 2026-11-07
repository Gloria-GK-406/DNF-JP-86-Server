using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Why a ticket conversion of one equipment piece was refused; nothing is consumed on any of them.</summary>
public enum EquipmentEmancipateRejection
{
    None,
    NoSession,
    InvalidRequest,
    /// <summary>The ticket or the target is missing from its slot, or is another template than the client named.</summary>
    ItemUnavailable,
    /// <summary>The ticket cannot act on this template: no matching <c>[emancipate]</c> block or no policy for the ticket kind.</summary>
    NotApplicable,
    /// <summary>The target is worn or on the quick bar; A21 only converts pieces in the equipment bag.</summary>
    InUse,
    NotInTown,
    Locked,
    Expired,
    Durability,
    /// <summary>Unpurified other-world equipment (amplify type 0x80).</summary>
    Unpurified,
    MaterialUnavailable,
}

/// <summary>What one ticket kind does to a matching piece: block selection, extra checks and what the new template keeps.</summary>
/// <remarks>
/// Ticket kinds with no registered policy are refused, so a kind is only enabled once its semantics have
/// evidence. The domain service still owns ownership, slot, lock, durability and material checks.
/// </remarks>
public interface IEquipmentEmancipatePolicy
{
    IReadOnlySet<int> TicketTypes { get; }

    /// <summary>The block this ticket acts on, or null when none of the template's blocks belongs to it.</summary>
    EquipmentEmancipateDefinition? Select(EmancipateTicketDefinition ticket, IReadOnlyList<EquipmentEmancipateDefinition> blocks);

    /// <summary>Kind-specific admission on the current piece, after the common checks passed.</summary>
    EquipmentEmancipateRejection Check(EmancipateTicketDefinition ticket, ItemSnapshot target) => EquipmentEmancipateRejection.None;

    /// <summary>
    /// The final piece: <paramref name="transformed"/> is the current piece with only its template replaced;
    /// <paramref name="before"/> is the piece as it was.
    /// </summary>
    ItemSnapshot Retain(EmancipateTicketDefinition ticket, ItemSnapshot before, ItemSnapshot transformed);
}

/// <summary>An admitted conversion: the single produced template, its materials and how the piece is rewritten.</summary>
public sealed record EquipmentEmancipatePlan(int OutputItemId, IReadOnlyList<EmancipateItemCount> Inputs,
    Func<ItemSnapshot, ItemSnapshot> Retain);

public sealed record EquipmentEmancipateDecision(EquipmentEmancipateRejection Rejection, EquipmentEmancipatePlan? Plan = null);

/// <summary>Reads content only; never touches a player's assets.</summary>
public interface IEquipmentEmancipateRuleService
{
    ValueTask<EquipmentEmancipateDecision> DecideAsync(int ticketItemId, ItemSnapshot target, CancellationToken cancellationToken);
}

public sealed record EquipmentEmancipateReceipt(SelectedSessionIdentity Session, BusinessSlot TicketSlot, int TicketItemId,
    BusinessSlot TargetSlot, ItemSnapshot Before, ItemSnapshot After,
    IReadOnlyList<OpeningSlotConsumption> Consumed, DateTimeOffset CommittedAt);

public sealed record EquipmentEmancipateResult(EquipmentEmancipateRejection Rejection, EquipmentEmancipateReceipt? Receipt = null);

public interface IEquipmentEmancipateDomainService
{
    /// <summary>
    /// Converts the piece at <paramref name="targetSlot"/> with the ticket at <paramref name="ticketSlot"/>: one ticket
    /// and the block's inputs are consumed and the piece changes template in place, all in the caller's transaction.
    /// </summary>
    ValueTask<EquipmentEmancipateResult> EmancipateAsync(SelectedSessionIdentity session,
        BusinessSlot ticketSlot, int expectedTicketItemId, BusinessSlot targetSlot, int expectedTargetItemId,
        IEffectFootprintRecorder footprint, CancellationToken cancellationToken);
}
