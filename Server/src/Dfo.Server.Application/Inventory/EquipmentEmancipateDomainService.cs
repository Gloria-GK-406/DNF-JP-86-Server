using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

/// <summary>Other-world equipment that has not been purified carries amplify type 0x80 (see the outworld rules).</summary>
internal static class EquipmentAmplification
{
    public const byte UnpurifiedType = 0x80;
    public static bool IsUnpurified(ItemSnapshot item) => item.AmplifyType == UnpurifiedType;
}

internal sealed class EquipmentEmancipateRuleService(IEquipmentEmancipateDefinitionSource content,
    IEquipmentDefinitionSource equipment, IEnumerable<IEquipmentEmancipatePolicy> policies) : IEquipmentEmancipateRuleService
{
    private readonly IReadOnlyList<IEquipmentEmancipatePolicy> registered = policies.ToArray();

    public async ValueTask<EquipmentEmancipateDecision> DecideAsync(int ticketItemId, ItemSnapshot target, CancellationToken ct)
    {
        var ticket = await content.GetTicketAsync(ticketItemId, ct).ConfigureAwait(false);
        var policy = ticket is null ? null : registered.SingleOrDefault(x => x.TicketTypes.Contains(ticket.TicketType));
        if (ticket is null || policy is null) return new(EquipmentEmancipateRejection.NotApplicable);
        var block = policy.Select(ticket, await content.GetEquipmentAsync(target.ItemId, ct).ConfigureAwait(false));
        if (block is not { Outputs: [{ Count: 1 } output] } || block.Inputs.Any(x => x.Count <= 0))
            return new(EquipmentEmancipateRejection.NotApplicable);
        var current = await equipment.GetEquipmentAsync(target.ItemId, ct).ConfigureAwait(false);
        var next = await equipment.GetEquipmentAsync(output.ItemId, ct).ConfigureAwait(false);
        if (current is null || next is null) return new(EquipmentEmancipateRejection.NotApplicable);
        // A21 refuses an unrepaired piece locally (0x8A28) and has a server code for it (0x07).
        if (current.MaxDurability is { } maximum && target.Durability < maximum)
            return new(EquipmentEmancipateRejection.Durability);
        if (EquipmentAmplification.IsUnpurified(target)) return new(EquipmentEmancipateRejection.Unpurified);
        var admitted = policy.Check(ticket, target);
        if (admitted != EquipmentEmancipateRejection.None) return new(admitted);
        return new(EquipmentEmancipateRejection.None, new(output.ItemId, block.Inputs, transformed =>
        {
            var kept = policy.Retain(ticket, target, transformed);
            // The new template's own durability ceiling; the source was required to be fully repaired.
            return next.MaxDurability is { } ceiling ? kept with { Durability = ceiling } : kept;
        }));
    }
}

/// <summary>
/// Ticket 19, the requiem conversion book (10150749): a requiem weapon becomes its liberated counterpart.
/// </summary>
/// <remarks>
/// The block is the one whose <c>[emancipate type]</c> equals the ticket. The piece keeps everything it carries:
/// the A21 confirmation (dstr 0xBA66) and the book's description promise reinforcement, amplification,
/// forging and enchantment; 70's <c>copyItemOption</c> keeps the same set. Fields neither source names
/// (seal, trade restriction, sockets) stay as they were rather than being reset on guesswork.
/// </remarks>
internal sealed class RequiemEmancipatePolicy : IEquipmentEmancipatePolicy
{
    public const int Ticket = 19;
    public IReadOnlySet<int> TicketTypes { get; } = new HashSet<int> { Ticket };

    public EquipmentEmancipateDefinition? Select(EmancipateTicketDefinition ticket, IReadOnlyList<EquipmentEmancipateDefinition> blocks)
    {
        var matching = blocks.Where(x => x.Type == ticket.TicketType).ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    public ItemSnapshot Retain(EmancipateTicketDefinition ticket, ItemSnapshot before, ItemSnapshot transformed) => transformed;
}

internal sealed class EquipmentEmancipateDomainService(
    ISelectedSessionDomainService sessions,
    IBusinessInventorySlotDomainService slots,
    ICharacterBusinessInventoryDomainService bags,
    IEquipmentDomainService equipments,
    IEquipmentEmancipateRuleService rules,
    IOpeningEffectPlanRuleService plans,
    IMaterialConsumptionDomainService materials,
    IDungeonRunQueryDomainService runs,
    TimeProvider clock) : IEquipmentEmancipateDomainService
{
    // Requiem stones (10000204) are quest-bag items; colorless and elemental crystals live in the account bag.
    private static readonly ImmutableArray<BusinessInventorySpace> MaterialSpaces =
    [
        BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable, BusinessInventorySpace.Material,
        BusinessInventorySpace.Quest, BusinessInventorySpace.ExpertMaterial, BusinessInventorySpace.SharedMaterial,
    ];

    public async ValueTask<EquipmentEmancipateResult> EmancipateAsync(SelectedSessionIdentity session,
        BusinessSlot ticketSlot, int expectedTicketItemId, BusinessSlot targetSlot, int expectedTargetItemId,
        IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        if (!sessions.IsInWorld(session)) return new(EquipmentEmancipateRejection.NoSession);
        if (expectedTicketItemId <= 0 || expectedTargetItemId <= 0
            || ticketSlot.Space is not (BusinessInventorySpace.Consumable or BusinessInventorySpace.QuickSlot))
            return new(EquipmentEmancipateRejection.InvalidRequest);
        if (targetSlot.Space is BusinessInventorySpace.WornEquipment or BusinessInventorySpace.WornEquipmentExtension
            or BusinessInventorySpace.QuickSlot)
            return new(EquipmentEmancipateRejection.InUse);
        if (targetSlot.Space != BusinessInventorySpace.CarriedEquipment) return new(EquipmentEmancipateRejection.InvalidRequest);
        if (runs.Find(session.CharacterId) is not null) return new(EquipmentEmancipateRejection.NotInTown);
        var at = clock.GetUtcNow();

        var ticketBound = slots.Find(session.CharacterId, ticketSlot);
        var ticket = ticketBound is null ? null : await ticketBound.Container.GetAsync(ticketSlot.Index, ct).ConfigureAwait(false);
        if (ticket is null || !ticket.IsPile || ticket.Count <= 0 || ticket.ItemId != expectedTicketItemId)
            return new(EquipmentEmancipateRejection.ItemUnavailable);
        if (ticket.Snapshot.EquipmentLockId != 0) return new(EquipmentEmancipateRejection.Locked);
        if (ticket.Snapshot.Lifetime.IsExpired(at)) return new(EquipmentEmancipateRejection.Expired);

        var instance = await equipments.Of(session.CharacterId).FindAtAsync(targetSlot, ct).ConfigureAwait(false);
        var target = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        if (instance is null || target is null || target.Snapshot.IsMirror || target.ItemId != expectedTargetItemId)
            return new(EquipmentEmancipateRejection.ItemUnavailable);
        if (target.Snapshot.EquipmentLockId != 0 || target.Snapshot.SortLockFlag != 0) return new(EquipmentEmancipateRejection.Locked);
        if (target.Snapshot.Lifetime.IsExpired(at)) return new(EquipmentEmancipateRejection.Expired);

        var decision = await rules.DecideAsync(ticket.ItemId, target.Snapshot, ct).ConfigureAwait(false);
        if (decision.Plan is not { } plan) return new(decision.Rejection);

        // The selected ticket goes first so the book the client pointed at is the one spent.
        var held = new List<OpeningHeldItem> { new(ticketSlot, ticket) };
        foreach (var space in MaterialSpaces)
        {
            var bag = bags.GetStore(session.CharacterId, BusinessInventoryOwnership.StoreOf(space)).FindContainer(space);
            if (bag is null) continue;
            held.AddRange((await bag.GetAllAsync(ct).ConfigureAwait(false))
                .Where(x => new BusinessSlot(space, x.SlotIndex) != ticketSlot)
                .Select(x => new OpeningHeldItem(new BusinessSlot(space, x.SlotIndex), x)));
        }
        RecipeEffectCosts costs;
        try
        {
            costs = plans.MaterialCosts(plan.Inputs.Select(x => new RecipeAmount(x.ItemId, x.Count))
                .Prepend(new RecipeAmount(ticket.ItemId, 1)), 1, held, at);
        }
        catch (OpeningRejectedException rejected) when (rejected.Reason == OpeningRejection.MissingMaterial)
        {
            return new(EquipmentEmancipateRejection.MaterialUnavailable);
        }
        var consumed = await materials.ConsumeAsync(session,
            costs.Items.Select(x => new MaterialCost(x.Slot, x.ItemId, x.Count)).ToArray(), ct).ConfigureAwait(false);
        if (!consumed.Success) return new(EquipmentEmancipateRejection.MaterialUnavailable);
        // Every check passed and materials are staged in the caller's transaction; a failure from here on is
        // a system failure that rolls the whole unit back.
        var after = await instance.TransformAsync(plan.OutputItemId, plan.Retain, ct).ConfigureAwait(false);
        return new(EquipmentEmancipateRejection.None, new(session, ticketSlot, ticket.ItemId, targetSlot,
            target.Snapshot, after.Snapshot, costs.Items, at));
    }
}
