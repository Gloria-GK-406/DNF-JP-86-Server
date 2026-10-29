using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.World;

internal sealed class LostLandConsumableRuleService(ILostLandConsumableDefinitionSource source,
    IItemUseEffectRuleService uses) : ILostLandConsumableRuleService
{
    public async ValueTask<bool> SupportsAsync(int itemId, CancellationToken token) =>
        await source.GetAsync(itemId, token).ConfigureAwait(false) is not null;

    public async ValueTask<ILostLandConsumableRule?> PrepareAsync(int itemId, LostLandConsumableKind kind,
        MazeCell? origin, LostLandConsumableRoomFacts room, CancellationToken token)
    {
        if (kind == LostLandConsumableKind.Teleport && origin != room.Cell
            || kind == LostLandConsumableKind.Transformation && origin is not null) return null;
        var definition = await source.GetAsync(itemId, token).ConfigureAwait(false);
        if (definition is null || definition.Kind != kind || definition.DisabledMaps.Contains(room.MapId)) return null;
        if (definition.Destination is { } target && (!room.Cells.TryGetValue(target, out var cell)
            || cell.MapCandidates.Count == 0)) return null;
        var rule = await uses.RuleForAsync(itemId, token).ConfigureAwait(false);
        if (rule?.Action != (kind == LostLandConsumableKind.Teleport ? "[move map]" : "[riding]")) return null;
        return new Rule(rule, uses.CooldownKeyFor(itemId, rule), definition.Destination, uses);
    }

    private sealed class Rule(ItemUseRule rule, ItemCooldownKey key, MazeCell? destination,
        IItemUseEffectRuleService uses) : ILostLandConsumableRule
    {
        public ItemCooldownKey CooldownKey => key;
        public MazeCell? Destination => destination;
        public ItemUsePlan? Build(BusinessSlot source, ItemReference item, ItemUseCandidate? character, DateTimeOffset at)
        {
            var plan = uses.Build(rule, source, item, character, null, at);
            return plan.Status == ItemUsability.Allowed && plan.Effects.OfType<ConsumeFromContainerEffect>()
                .Count(effect => effect.Slot == source && effect.ExpectedItemId == item.ItemId && effect.Count == 1) == 1
                    ? plan : null;
        }
    }
}
