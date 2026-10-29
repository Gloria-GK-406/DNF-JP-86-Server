using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class LostLandConsumableRuleService(ILostLandConsumableDefinitionSource source)
    : ILostLandConsumableRuleService
{
    public ValueTask<LostLandConsumableDefinition?> DefinitionAsync(int itemId, CancellationToken token) =>
        source.GetAsync(itemId, token);
}

internal sealed class LostLandConsumableDomainService(ILostLandConsumableRuleService definitions,
    IDungeonRunDomainService runs, IDungeonVisitDomainService visits, IOpeningInventoryDomainService inventory, IItemUseEffectRuleService uses,
    ICharacterItemUseDomainService itemUse, TimeProvider clock) : ILostLandConsumableDomainService
{
    public async ValueTask<bool> SupportsAsync(int itemId, CancellationToken token) =>
        await definitions.DefinitionAsync(itemId, token).ConfigureAwait(false) is not null;
    public async ValueTask<LostLandConsumableResult> UseAsync(SelectedSessionIdentity session, BusinessSlot source,
        LostLandConsumableKind kind, MazeCell? origin, IEffectFootprintRecorder footprint, CancellationToken token)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var run = runs.Find(session.CharacterId);
        if (run?.Layout.Mode is not DungeonRunMode.LostLand || run.LostLandExploration?.HasExpired(clock.GetUtcNow()) == true
            || !run.ReadyParticipants.Contains(session.CharacterId))
            throw Reject("LostLand consumable requires a loaded exploration room.");
        if (kind == LostLandConsumableKind.Teleport && origin != run.CurrentCell
            || kind == LostLandConsumableKind.Transformation && origin is not null)
            throw Reject("LostLand consumable request does not belong to the current room.");
        var at = clock.GetUtcNow();
        var item = await inventory.SourceAsync(session, source, at, token).ConfigureAwait(false);
        var definition = await definitions.DefinitionAsync(item.ItemId, token).ConfigureAwait(false);
        if (definition is null || definition.Kind != kind || definition.DisabledMaps.Contains(run.CurrentRoomMapId))
            throw Reject("LostLand consumable does not match the action or room.");
        if (definition.Destination is { } target && (!run.Layout.Cells.TryGetValue(target, out var cell)
            || cell.MapCandidates.Count == 0)) throw Reject("LostLand capsule destination is unavailable.");
        var rule = await uses.RuleForAsync(item.ItemId, token).ConfigureAwait(false);
        if (rule?.Action != (kind == LostLandConsumableKind.Teleport ? "[move map]" : "[riding]"))
            throw Reject("LostLand consumable action declaration changed.");
        var facts = await itemUse.FactsAsync(session.CharacterId,
            uses.CooldownKeyFor(item.ItemId, rule), token).ConfigureAwait(false);
        var plan = uses.Build(rule, source, item, facts, null, at);
        if (plan.Status != ItemUsability.Allowed || plan.Effects.OfType<ConsumeFromContainerEffect>()
            .Count(effect => effect.Slot == source && effect.ExpectedItemId == item.ItemId && effect.Count == 1) != 1)
            throw Reject("LostLand consumable is unusable or cooling down.");
        // Transformation is client-owned; a capsule must also commit the destination room.
        var applied = await inventory.ApplyAsync(session, plan.Effects, at, footprint, token).ConfigureAwait(false);
        var movement = definition.Destination is { } destination
            ? await visits.MoveByLostLandItemAsync(session.CharacterId, run, destination, token).ConfigureAwait(false)
                ?? throw Reject("LostLand room changed before capsule acceptance.")
            : null;
        return new(applied.Inventory, movement);
    }
    private static OpeningRejectedException Reject(string message) => new(OpeningRejection.ItemUnusable, message);
}
