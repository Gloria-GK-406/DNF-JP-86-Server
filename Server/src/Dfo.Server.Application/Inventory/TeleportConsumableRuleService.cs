using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class TeleportConsumableRuleService(ITeleportConsumableDefinitionSource definitions,
    IItemUseJudgementService judgement, ITownDefinitionSource towns, IEntryPlacementRuleService placement)
    : ITeleportConsumableRuleService
{
    public async ValueTask<TeleportConsumableRule?> RuleForAsync(int itemId, CancellationToken ct)
    {
        var destination = await definitions.GetAsync(itemId, ct).ConfigureAwait(false);
        var use = destination is null ? null : await judgement.RuleForAsync(itemId, ct).ConfigureAwait(false);
        return destination is not null && use is { IsReadable: true } ? new(destination, use) : null;
    }
    public async ValueTask<TeleportConsumablePlan> BuildAsync(TeleportConsumableRule rule, BusinessSlot source,
        ContainerSlotItem item, ItemUseCandidate facts, CharacterPositionState standing,
        byte townId, ChannelEntryIdentity channel, TownAreaKey sourceArea, DateTimeOffset at, CancellationToken ct)
    {
        var status = judgement.Judge(rule.Use, facts);
        if (status != ItemUsability.Allowed) return new(status, []);
        if (!item.IsPile || item.Count < 1 || source.Space is not (BusinessInventorySpace.Consumable or BusinessInventorySpace.QuickSlot)
            || !rule.Use.IsConsumedOnUse || item.Core.EquipmentLockId != 0
            || item.Core.ExpireTime is > 0 and < int.MaxValue && item.Core.ExpireTime <= at.ToUnixTimeSeconds())
            return new(ItemUsability.NotAUsableItem, []);
        if (!rule.Destination.TownIds.Contains(townId)
            || !await placement.CanEnterTownAsync(channel, townId, ct).ConfigureAwait(false))
            return new(ItemUsability.PlaceNotAllowed, []);
        var gate = (await towns.GetAsync(townId, ct).ConfigureAwait(false))?.Gate;
        if (gate is null) return new(ItemUsability.PlaceNotAllowed, []);
        if (facts.Level < gate.MinimumLevel) return new(ItemUsability.LevelTooLow, []);
        var destination = new CharacterPositionState(townId, checked((byte)gate.AreaId),
            checked((short)gate.Operand1), checked((short)gate.Operand2), 0);
        var effects = new List<DomainEffect> { new ConsumeFromContainerEffect(source, item.ItemId, 1),
            new MoveToTownEffect(standing, destination, !channel.IsTransient, sourceArea, channel.ChannelId) };
        if (rule.Use.CooldownMilliseconds > 0)
            effects.Add(new StartItemCooldownEffect(judgement.CooldownKeyFor(item.ItemId, rule.Use),
                TimeSpan.FromMilliseconds(rule.Use.CooldownMilliseconds)));
        return new(ItemUsability.Allowed, effects.AsReadOnly());
    }
}
