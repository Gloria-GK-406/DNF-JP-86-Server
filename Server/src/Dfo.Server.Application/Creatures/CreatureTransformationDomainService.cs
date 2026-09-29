using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Creatures;

/// <summary>Stages a template change on the existing creature item identity.</summary>
internal sealed class CreatureTransformationDomainService(
    ICreatureDomainService creatures,
    ICreatureRuleSource rules,
    ICreatureEquipmentLifetimeSource lifetimes,
    ISelectedSessionDomainService sessions,
    IDomainTransactionAccessor transactions,
    TimeProvider clock) : ICreatureTransformationDomainService
{
    public async ValueTask<CreatureTransformationReceipt?> HatchAsync(
        SelectedSessionIdentity session, BusinessSlot eggSlot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (transactions.Current is null
            || sessions.FindByCharacter(session.CharacterId) != session
            || eggSlot.Space != BusinessInventorySpace.CreatureInventory
            || eggSlot.Index is < 0 or > 139)
            return null;

        var store = creatures.GetStore(session.CharacterId);
        var container = store.FindContainer(eggSlot.Space);
        if (container is null) return null;
        var item = await container.ReadSlotAsync(eggSlot.Index, ct).ConfigureAwait(false);
        if (item is null || item.ItemKind != 5 || item.Core.Value <= 0
            || item.Core.ExpireTime > 0 && item.Core.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds()) return null;
        var before = await store.FindAsync(item.Core.Value, ct).ConfigureAwait(false);
        if (before is null || before.ItemTemplateId != item.ItemId) return null;
        var source = await rules.GetAsync(before.ItemTemplateId, ct).ConfigureAwait(false);
        if (source is not { IsEgg: true, HatchOutputTemplateId: > 0 }) return null;
        var target = await rules.GetAsync(source.HatchOutputTemplateId.Value, ct).ConfigureAwait(false);
        if (target is null || target.IsEgg || target.CreatureId != source.CreatureId) return null;

        // 70 CCreatureItem::Set copies the egg UID and slot, then new CCreature starts at
        // level 1 / exp 0 and Hatch explicitly fills the stomach to 100.
        var after = before with
        {
            ItemTemplateId = target.TemplateId,
            Stomach = 100,
            ExactStomach = 100,
            Experience = 0,
            Level = 1,
            NameBytes = [],
            ModeFlag = 0,
            Mode1Field0A = 0,
            Mode1Field0B = 0,
            TailFlag = 0
        };
        var lifetime = await lifetimes.GetAsync(target.TemplateId, clock.GetUtcNow(), ct)
            .ConfigureAwait(false);
        var remaining = lifetime.DirectRemainingSeconds > 0
            ? lifetime.DirectRemainingSeconds : lifetime.DetailFallbackRemainingSeconds;
        var expiresAt = remaining == 0 ? 0
            : checked((int)Math.Min(int.MaxValue, clock.GetUtcNow().ToUnixTimeSeconds() + remaining));
        var core = item.Core with { ItemId = target.TemplateId, ExpireTime = expiresAt };
        await store.SaveAsync(after, ct).ConfigureAwait(false);
        await container.PutAsync(eggSlot.Index, core, ct).ConfigureAwait(false);
        return new CreatureTransformationReceipt(before, after, eggSlot, core)
        {
            CreatureId = target.CreatureId
        };
    }

    public async ValueTask<CreatureTransformationReceipt?> EvolveAsync(
        SelectedSessionIdentity session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (transactions.Current is null || sessions.FindByCharacter(session.CharacterId) != session)
            return null;

        var store = creatures.GetStore(session.CharacterId);
        var container = store.FindContainer(BusinessInventorySpace.WornCreature);
        if (container is null) return null;
        var item = await container.ReadSlotAsync(0, ct).ConfigureAwait(false);
        var before = await store.CurrentAsync(ct).ConfigureAwait(false);
        if (before is null || item is null || item.ItemKind != 5
            || item.Core.ExpireTime > 0 && item.Core.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds()
            || item.Core.Value != before.ItemUid || item.ItemId != before.ItemTemplateId)
            return null;
        var source = await rules.GetAsync(before.ItemTemplateId, ct).ConfigureAwait(false);
        if (source is null || source.IsEgg || source.OutputTemplateId is not > 0
            || source.Evolutions.Count != 1)
            return null;
        var step = source.Evolutions[0];
        if (step.RequiredLevel is not > 0 || before.Level < step.RequiredLevel
            || step.RequiredQuestId is > 0)
            return null;
        var target = await rules.GetAsync(source.OutputTemplateId.Value, ct).ConfigureAwait(false);
        if (target is null || target.IsEgg || target.CreatureId != step.EvolvesIntoCreatureId)
            return null;

        // 70 CCreature::Evolute calls SetItemId on this same CCreature; the existing
        // stomach, experience, level, name and UID stay on the object.
        var after = before with { ItemTemplateId = target.TemplateId };
        var core = item.Core with { ItemId = target.TemplateId };
        await store.SaveAsync(after, ct).ConfigureAwait(false);
        await container.PutAsync(0, core, ct).ConfigureAwait(false);
        return new CreatureTransformationReceipt(before, after,
            new BusinessSlot(BusinessInventorySpace.WornCreature, 0), core)
        {
            CreatureId = target.CreatureId
        };
    }
}
