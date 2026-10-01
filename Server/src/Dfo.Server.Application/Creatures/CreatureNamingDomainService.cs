using System.Collections.Immutable;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Creatures;

internal sealed class CreatureNamingDomainService(ICreatureDomainService creatures,
    ISelectedSessionDomainService sessions, TimeProvider clock, ISpecialConsumableEffectPlanRuleService rules,
    IApplyEffectsDomainService effects) : ICreatureNamingDomainService
{
    public async ValueTask<CreatureRenameReceipt?> RenameAsync(SelectedSessionIdentity session,
        BusinessSlot sourceSlot, byte[] nameBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(nameBytes);
        var name = ImmutableArray.CreateRange(nameBytes);
        if (sessions.FindByCharacter(session.CharacterId) != session
            || sourceSlot.Space != BusinessInventorySpace.CreatureConsumable || sourceSlot.Index is < 0 or > 50) return null;
        var store = creatures.GetStore(session.CharacterId);
        var cards = store.FindContainer(BusinessInventorySpace.CreatureConsumable);
        var worn = store.FindContainer(BusinessInventorySpace.WornCreature);
        if (cards is null || worn is null) return null;
        var at = clock.GetUtcNow();
        var plan = rules.Rename(new(sourceSlot, await cards.ReadSlotAsync(sourceSlot.Index, ct).ConfigureAwait(false),
            await worn.ReadSlotAsync(0, ct).ConfigureAwait(false), await store.CurrentAsync(ct).ConfigureAwait(false), name, at));
        if (plan.Length == 0) return null;
        var pet = await creatures.Of(session.CharacterId).CurrentPetAsync(ct).ConfigureAwait(false);
        if (pet is null) return null;
        var applied = await effects.ApplyAsync(new(session.AccountId, session.CharacterId, at, session), plan, ct).ConfigureAwait(false);
        var consumed = applied.Changes<ContainerConsumptionChange>().Single();
        return new(applied.Changes<PetNameChange>().Single().Creature, sourceSlot,
            consumed.RemainingCount == 0 ? null : (await cards.ReadSlotAsync(sourceSlot.Index, ct).ConfigureAwait(false))!.Core);
    }
}
