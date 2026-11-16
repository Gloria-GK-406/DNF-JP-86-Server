using System.Collections.Immutable;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Chat;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.ServerBroadcast;

/// <summary>Decides what a committed fact announces and who in the world should hear it.</summary>
internal sealed class ServerBroadcastSubscriber(
    IServerBroadcastRuleService rules,
    ICharacterDomainService characters,
    IItemCatalogRuleService items,
    IChatScopeDomainService scopes,
    IEventService events)
    : IDomainEventSubscriber<CharacterLevelChanged>,
      IDomainEventSubscriber<CharacterGrowTypeChanged>,
      IDomainEventSubscriber<EquipmentUpgradeAnnounced>,
      IDomainEventSubscriber<VendingPrizeAnnounced>,
      IDomainEventSubscriber<BoxRewardAnnounced>,
      IDomainEventSubscriber<EquipmentForgingAnnounced>,
      IDomainEventSubscriber<RecipeCraftAnnounced>,
      IDomainEventSubscriber<DespairTowerFloorAnnounced>,
      IDomainEventSubscriber<AncientDungeonItemAnnounced>
{
    public ValueTask HandleAsync(DomainEventEnvelope<CharacterLevelChanged> message, CancellationToken ct)
    {
        var change = message.Payload;
        return rules.TopLevelReached(change.Before, change.After) is { } top
            ? PostAsync(ServerBroadcastKind.MaxLevel, change.CharacterId, new ServerBroadcastContent.MaxLevel(top), ct)
            : ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(DomainEventEnvelope<CharacterGrowTypeChanged> message, CancellationToken ct)
    {
        var change = message.Payload;
        return rules.IsSecondAwakening(change.GrowType)
            ? PostAsync(ServerBroadcastKind.SecondAwakening, change.CharacterId, new ServerBroadcastContent.SecondAwakening(), ct)
            : ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<EquipmentUpgradeAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        var kind = fact.Kind == EquipmentUpgradeKind.Amplification ? ServerBroadcastKind.Amplification : ServerBroadcastKind.Reinforcement;
        if (!rules.Announces(kind)) return;
        var item = await ItemNameAsync(fact.ItemId, ct).ConfigureAwait(false);
        await PostAsync(kind, fact.Actor, new ServerBroadcastContent.Upgrade(fact.Kind, fact.Level, item, fact.Succeeded), ct)
            .ConfigureAwait(false);
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<VendingPrizeAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        if (!rules.Announces(ServerBroadcastKind.VendingPrize)) return;
        var item = await ItemNameAsync(fact.ItemId, ct).ConfigureAwait(false);
        await PostAsync(ServerBroadcastKind.VendingPrize, fact.Actor, new ServerBroadcastContent.VendingPrize(item, fact.Count), ct)
            .ConfigureAwait(false);
    }

    public ValueTask HandleAsync(DomainEventEnvelope<BoxRewardAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        return PostAsync(ServerBroadcastKind.BoxReward, fact.Actor, new ServerBroadcastContent.BoxReward(fact.BoxItemId, fact.ItemId), ct);
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<EquipmentForgingAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        if (!rules.Announces(ServerBroadcastKind.Forging)) return;
        var item = await ItemNameAsync(fact.ItemId, ct).ConfigureAwait(false);
        await PostAsync(ServerBroadcastKind.Forging, fact.Actor,
            new ServerBroadcastContent.Forging(fact.ReinforcementLevel, item, fact.Stage, fact.Succeeded), ct).ConfigureAwait(false);
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<RecipeCraftAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        if (!rules.Announces(ServerBroadcastKind.RecipeCraft)) return;
        var item = await ItemNameAsync(fact.ItemId, ct).ConfigureAwait(false);
        await PostAsync(ServerBroadcastKind.RecipeCraft, fact.Actor,
            new ServerBroadcastContent.RecipeCraft(fact.ReinforcementLevel, item), ct).ConfigureAwait(false);
    }

    public ValueTask HandleAsync(DomainEventEnvelope<DespairTowerFloorAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        return PostAsync(ServerBroadcastKind.DespairTower, fact.Actor, new ServerBroadcastContent.DespairTower(fact.Floor), ct);
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<AncientDungeonItemAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        if (!rules.Announces(ServerBroadcastKind.AncientDungeonItem)) return;
        var item = await ItemNameAsync(fact.ItemId, ct).ConfigureAwait(false);
        await PostAsync(ServerBroadcastKind.AncientDungeonItem, fact.Actor, new ServerBroadcastContent.AncientDungeonItem(item), ct)
            .ConfigureAwait(false);
    }

    private async ValueTask PostAsync(ServerBroadcastKind kind, CharacterId actor, ServerBroadcastContent content, CancellationToken ct)
    {
        if (!rules.Announces(kind)) return;
        var name = (await characters.Get(actor).IdentityAsync(ct).ConfigureAwait(false))?.Name;
        if (name is not { Length: > 0 }) return;
        var members = scopes.MembersOf(new ChatScope.Server());
        if (members.Count == 0) return;
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters(members),
            new ServerBroadcastPosted(name.ToImmutableArray(), content), ct).ConfigureAwait(false);
    }

    // An item the catalog cannot name is still announced, under its identifier.
    private async ValueTask<string> ItemNameAsync(int itemId, CancellationToken ct) =>
        await items.ItemNameAsync(itemId, ct).ConfigureAwait(false) is { Length: > 0 } name ? name : itemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
