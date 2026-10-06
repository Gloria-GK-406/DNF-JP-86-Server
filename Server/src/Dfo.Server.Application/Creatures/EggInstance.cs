using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Creatures;

internal sealed class EggInstance(ItemReference reference, CreatureInstanceContext context) : IEggInstance
{
    public CharacterId Owner => context.Owner;
    public int ItemUid => reference.ItemUid!.Value.Value;
    public async ValueTask<IPetInstance?> BirthAsync(CancellationToken ct)
    {
        var before = await context.ReadAsync(reference, ct).ConfigureAwait(false);
        var location = await context.LocateAsync(reference, ct).ConfigureAwait(false);
        if (location is null || location.Slot.Space != BusinessInventorySpace.CreatureInventory
            || location.Slot.Index is < 0 or > 139 || !context.IsLive(location, before)) return null;
        var source = await context.Rules.GetAsync(before.ItemTemplateId, ct).ConfigureAwait(false);
        if (source is not { IsEgg: true, HatchOutputTemplateId: > 0 }) return null;
        var target = await context.Rules.GetAsync(source.HatchOutputTemplateId.Value, ct).ConfigureAwait(false);
        if (target is null || target.IsEgg || target.CreatureId != source.CreatureId) return null;
        var after = before with
        {
            ItemTemplateId = target.TemplateId, Stomach = 100, ExactStomach = 100,
            Experience = 0, Level = 1, NameBytes = [], ModeFlag = 0,
            Mode1Field0A = 0, Mode1Field0B = 0, TailFlag = 0
        };
        var updated = await context.StageAsync(reference, after, ct, restartClock: true,
            expiration: await context.Rules.BirthExpirationAsync(target.TemplateId, context.Now, ct).ConfigureAwait(false))
            .ConfigureAwait(false);
        return new PetInstance(updated, context)
        {
            BirthReceipt = new(before, after, location.Slot, updated.Snapshot) { CreatureId = target.CreatureId }
        };
    }
}
