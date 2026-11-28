using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemUseEffectDomainService(ICharacterItemUseDomainService itemUse,
    IItemUseEffectRuleService rules, IApplyEffectsDomainService effects,
    ICreatureInstanceDomainService creatures, TimeProvider clock,
    Dfo.Server.Abstractions.DomainEvents.IEventService events,
    IExperienceItemDomainService experienceItems, IEntryAllowanceItemDomainService entryItems,
    IDungeonParticipationDomainService participation, IReviveCoinDomainService reviveCoins,
    Dfo.Server.Abstractions.Rental.IAccountLuckyStarDomainService luckyStars,
    IOpeningInventoryDomainService openingInventory,
    IGuildDungeonDomainService guildDungeons, ITimedItemEffectTable itemEffects) : IItemUseEffectDomainService
{
    private enum UsePath { Ordinary, LuckyStarAction, GuildExtensionAction }

    public async ValueTask<ItemUseApplication> UseAsync(SelectedSessionIdentity session, BusinessSlot source,
        ItemReference item, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandActive(session);
        if (await reviveCoins.TryUseAsync(session, source, item, footprint, ct).ConfigureAwait(false) is { } coin)
            return coin;
        footprint.DemandActive(session);
        if (await entryItems.TryUseAsync(session, source, item, footprint, ct).ConfigureAwait(false) is { } allowance)
            return allowance;
        footprint.DemandActive(session);
        if (await experienceItems.TryUseAsync(session, source, item, footprint, ct).ConfigureAwait(false) is { } experience)
        {
            if (experience.Status == ItemUsability.Allowed)
            {
                footprint.DemandActive(session);
                await events.EnqueueAsync(new Dfo.Server.Abstractions.Achievements.ItemUsed(
                    session.CharacterId, item.ItemId, 1), ct).ConfigureAwait(false);
            }
            return experience;
        }

        if (await rules.OrdinaryUseRuleAsync(item.ItemId, ct).ConfigureAwait(false) is not { } rule)
            return new(ItemUsability.NotAUsableItem, item.ItemId, item.Count);
        return await UseWithRuleAsync(session, source, item, rule, clock.GetUtcNow(), footprint,
            UsePath.Ordinary, ct).ConfigureAwait(false);
    }

    public async ValueTask<OpeningInventoryReceipt> UseLuckyStarActionAsync(SelectedSessionIdentity session, BusinessSlot source,
        DateTimeOffset at, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var item = await openingInventory.SourceAsync(session, source, at, ct).ConfigureAwait(false);
        var rule = await rules.LuckyStarActionRuleAsync(item.ItemId, ct).ConfigureAwait(false)
            ?? throw new OpeningRejectedException(OpeningRejection.WrongItem, $"Item {item.ItemId} is not a supported lucky star action item.");
        var use = await UseWithRuleAsync(session, source, item, rule, at, footprint,
            UsePath.LuckyStarAction, ct).ConfigureAwait(false);
        return use.OpeningReceipt
            ?? throw new OpeningRejectedException(OpeningRejection.ItemUnusable, $"Lucky star item use refused: {use.Status}.");
    }

    public async ValueTask<OpeningInventoryReceipt> UseGuildDungeonExtensionActionAsync(SelectedSessionIdentity session,
        BusinessSlot source, DateTimeOffset at, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var item = await openingInventory.SourceAsync(session, source, at, ct).ConfigureAwait(false);
        if (await rules.GuildExtensionActionRuleAsync(item.ItemId, ct).ConfigureAwait(false) is not { } rule)
            throw new OpeningRejectedException(OpeningRejection.WrongItem, "The slot does not hold a guild dungeon extension ticket.");
        var use = await UseWithRuleAsync(session, source, item, rule, at, footprint, UsePath.GuildExtensionAction, ct)
            .ConfigureAwait(false);
        return use.OpeningReceipt
            ?? throw new OpeningRejectedException(OpeningRejection.ItemUnusable, $"Guild dungeon extension refused: {use.Status}.");
    }

    private async ValueTask<ItemUseApplication> UseWithRuleAsync(SelectedSessionIdentity session, BusinessSlot source,
        ItemReference item, ItemUseRule rule, DateTimeOffset at, IEffectFootprintRecorder footprint,
        UsePath path, CancellationToken ct)
    {
        if (rule.SummonsApc && participation.Find(session.CharacterId)?.Mode == DungeonParticipationMode.BossTower)
            return new(ItemUsability.PlaceNotAllowed, item.ItemId, item.Count);
        var facts = !rule.IsReadable ? null : await itemUse.FactsAsync(session.CharacterId,
            rules.CooldownFor(item.ItemId, rule), at, ct).ConfigureAwait(false);
        var pet = item.ItemId == 24 ? await creatures.Of(session.CharacterId).CurrentPetAsync(ct).ConfigureAwait(false) : null;
        var plan = rules.Build(rule, source, item, facts, pet?.ItemUid, at);
        if (plan.Status != ItemUsability.Allowed) return new(plan.Status, item.ItemId, item.Count);
        if (rule.ReinforcementPotion is not null &&
            (await itemEffects.ReadActiveAsync(session.CharacterId, at, ct).ConfigureAwait(false))
                .Any(row => row.ItemId == item.ItemId))
            return new(ItemUsability.OnCooldown, item.ItemId, item.Count);
        if (path == UsePath.GuildExtensionAction)
        {
            if (await rules.GuildExtensionCostAsync(item.ItemId, source, plan, ct).ConfigureAwait(false) is not { } cost)
                return new(ItemUsability.NotAUsableItem, item.ItemId, item.Count);
            footprint.DemandActive(session);
            if (!await guildDungeons.TryExtendAsync(session.CharacterId, cost, at, ct)
                .ConfigureAwait(false))
                return new(ItemUsability.PlaceNotAllowed, item.ItemId, item.Count);
        }
        if (rule.Action == ItemUseEffectRuleService.RentPointAction)
        {
            if (plan.Effects.OfType<ConsumeFromContainerEffect>().Count(x => x.Slot == source && x.ExpectedItemId == item.ItemId && x.Count == 1) != 1)
                return new(ItemUsability.NotAUsableItem, item.ItemId, item.Count);
            footprint.DemandActive(session);
            var granted = await luckyStars.GrantFromItemAsync(session.AccountId, item.ItemId, ct).ConfigureAwait(false);
            if (!granted.Succeeded) return new(ItemUsability.CharacterStateUnavailable, item.ItemId, item.Count);
            footprint.RecordRentalState(session);
        }
        footprint.DemandActive(session);
        var opening = path != UsePath.Ordinary
            ? await openingInventory.ApplyAsync(session, plan.Effects, at, footprint, ct).ConfigureAwait(false)
            : null;
        var applied = opening?.Applied ?? await effects.ApplyAsync(new(session.AccountId, session.CharacterId, at, footprint, session),
            plan.Effects, ct).ConfigureAwait(false);
        var remaining = applied.Changes<ContainerConsumptionChange>().LastOrDefault(x => x.Slot == source)?.RemainingCount ?? item.Count;
        footprint.DemandActive(session);
        await events.EnqueueAsync(new Dfo.Server.Abstractions.Achievements.ItemUsed(session.CharacterId, item.ItemId, 1), ct);
        return new(ItemUsability.Allowed, item.ItemId, remaining, applied) { OpeningReceipt = opening?.Inventory };
    }
}
