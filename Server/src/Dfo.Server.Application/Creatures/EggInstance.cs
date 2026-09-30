using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Creatures;

internal sealed class EggInstance(int itemUid, CreatureInstanceContext context) : IEggInstance
{
    public CharacterId Owner => context.Owner;
    public int ItemUid => itemUid;

    public async ValueTask<IPetInstance?> BirthAsync(CancellationToken ct)
    {
        var before = await context.ReadAsync(ItemUid, ct).ConfigureAwait(false);
        if (before is null) return null;
        var location = await context.LocateAsync(ItemUid, ct).ConfigureAwait(false);
        if (location is null || location.Slot.Space != BusinessInventorySpace.CreatureInventory
            || location.Slot.Index is < 0 or > 139 || !context.IsLive(location, before)) return null;
        var source = await context.Rules.GetAsync(before.ItemTemplateId, ct).ConfigureAwait(false);
        if (source is not { IsEgg: true, HatchOutputTemplateId: > 0 }) return null;
        var target = await context.Rules.GetAsync(source.HatchOutputTemplateId.Value, ct).ConfigureAwait(false);
        if (target is null || target.IsEgg || target.CreatureId != source.CreatureId) return null;
        // Preserve the egg's identity and position; initialize only the new creature's life.
        var after = before with
        {
            ItemTemplateId = target.TemplateId, Stomach = 100, ExactStomach = 100,
            Experience = 0, Level = 1, NameBytes = [], ModeFlag = 0,
            Mode1Field0A = 0, Mode1Field0B = 0, TailFlag = 0
        };
        var core = location.Core with
        {
            ItemId = target.TemplateId,
            ExpireTime = await context.Rules.BirthExpirationAsync(target.TemplateId, context.Now, ct).ConfigureAwait(false)
        };
        context.Stage(after, restartClock: true);
        await location.Container.PutAsync(location.Slot.Index, core, ct).ConfigureAwait(false);
        return new PetInstance(ItemUid, context)
        {
            BirthReceipt = new(before, after, location.Slot, core) { CreatureId = target.CreatureId }
        };
    }
}
