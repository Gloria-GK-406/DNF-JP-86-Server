using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class LostLandConsumableDomainService(ILostLandConsumableRuleService rules,
    IDungeonRunQueryDomainService runs, IDungeonVisitDomainService visits, IOpeningInventoryDomainService inventory,
    ICharacterItemUseDomainService itemUse, TimeProvider clock) : ILostLandConsumableDomainService
{
    public ValueTask<bool> SupportsAsync(int itemId, CancellationToken token) => rules.SupportsAsync(itemId, token);
    public async ValueTask<LostLandConsumableResult> UseAsync(SelectedSessionIdentity session, BusinessSlot source,
        LostLandConsumableKind kind, MazeCell? origin, IEffectFootprintRecorder footprint, CancellationToken token)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var run = runs.Find(session.CharacterId);
        if (run?.Layout.Mode is not DungeonRunMode.LostLand || run.LostLandExploration?.HasExpired(clock.GetUtcNow()) == true
            || !run.ReadyParticipants.Contains(session.CharacterId))
            throw Reject("LostLand consumable requires a loaded exploration room.");
        var at = clock.GetUtcNow();
        var item = await inventory.SourceAsync(session, source, at, token).ConfigureAwait(false);
        var rule = await rules.PrepareAsync(item.ItemId, kind, origin,
            new(run.CurrentRoomMapId, run.CurrentCell, run.Layout.Cells), token).ConfigureAwait(false)
                ?? throw Reject("LostLand consumable does not match the action or room.");
        var facts = await itemUse.FactsAsync(session.CharacterId, rule.CooldownKey, token).ConfigureAwait(false);
        var plan = rule.Build(source, item, facts, at)
            ?? throw Reject("LostLand consumable is unusable or cooling down.");
        // Transformation is client-owned; a capsule must also commit the destination room.
        var applied = await inventory.ApplyAsync(session, plan.Effects, at, footprint, token).ConfigureAwait(false);
        var movement = rule.Destination is { } destination
            ? await visits.MoveByLostLandItemAsync(session.CharacterId, run, destination, token).ConfigureAwait(false)
                ?? throw Reject("LostLand room changed before capsule acceptance.")
            : null;
        return new(applied.Inventory, movement);
    }
    private static OpeningRejectedException Reject(string message) => new(OpeningRejection.ItemUnusable, message);
}
