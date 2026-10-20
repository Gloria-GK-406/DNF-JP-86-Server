using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemUseEffectDomainService(ICharacterDomainService characters,
    IItemUseEffectRuleService rules, IApplyEffectsDomainService effects,
    ICreatureInstanceDomainService creatures, TimeProvider clock,
    Dfo.Server.Abstractions.DomainEvents.IEventService? events = null,
    IExperienceItemDomainService? experienceItems = null, IEntryAllowanceItemDomainService? entryItems = null,
    IDungeonParticipationDomainService? participation = null, IReviveCoinDomainService? reviveCoins = null,
    Dfo.Server.Abstractions.Rental.IAccountLuckyStarDomainService? luckyStars = null,
    IRequestReplayProjectionDomainService? replay = null) : IItemUseEffectDomainService
{
    public async ValueTask<ItemUseApplication> UseAsync(SelectedSessionIdentity session, BusinessSlot source,
        ItemReference item, CancellationToken ct)
    {
        if (reviveCoins is not null && await reviveCoins.TryUseAsync(session, source, item, ct).ConfigureAwait(false) is { } coin)
            return coin;
        if (entryItems is not null && await entryItems.TryUseAsync(session, source, item, ct).ConfigureAwait(false) is { } allowance)
            return allowance;
        if (experienceItems is not null
            && await experienceItems.TryUseAsync(session, source, item, ct).ConfigureAwait(false) is { } experience)
        {
            if (experience.Status == ItemUsability.Allowed && events is not null)
                await events.EnqueueAsync(new Dfo.Server.Abstractions.Achievements.ItemUsed(
                    session.CharacterId, item.ItemId, 1), ct).ConfigureAwait(false);
            return experience;
        }

        var rule = await rules.RuleForAsync(item.ItemId, ct).ConfigureAwait(false);
        if (rule?.SummonsApc == true && participation?.Find(session.CharacterId)?.Mode == DungeonParticipationMode.BossTower)
            return new(ItemUsability.PlaceNotAllowed, item.ItemId, item.Count);
        var facts = rule is null || !rule.IsReadable ? null : await characters.Get(session.CharacterId)
            .ItemUseFactsAsync(rules.CooldownKeyFor(item.ItemId, rule), ct).ConfigureAwait(false);
        var pet = item.ItemId == 24 ? await creatures.Of(session.CharacterId).CurrentPetAsync(ct).ConfigureAwait(false) : null;
        var at = clock.GetUtcNow();
        var plan = rules.Build(rule, source, item, facts, pet?.ItemUid, at);
        if (plan.Status != ItemUsability.Allowed) return new(plan.Status, item.ItemId, item.Count);
        if (rule?.Action == "[rent point]")
        {
            if (luckyStars is null || plan.Effects.OfType<ConsumeFromContainerEffect>().Count(x => x.Slot == source && x.ExpectedItemId == item.ItemId && x.Count == 1) != 1)
                return new(ItemUsability.NotAUsableItem, item.ItemId, item.Count);
            var granted = await luckyStars.GrantFromItemAsync(session.AccountId, item.ItemId, ct).ConfigureAwait(false);
            if (!granted.Succeeded) return new(ItemUsability.CharacterStateUnavailable, item.ItemId, item.Count);
            replay?.RecordRentalState(session);
        }
        var applied = await effects.ApplyAsync(new(session.AccountId, session.CharacterId, at, session), plan.Effects, ct)
            .ConfigureAwait(false);
        var remaining = applied.Changes<ContainerConsumptionChange>().LastOrDefault(x => x.Slot == source)?.RemainingCount ?? item.Count;
        if (events is not null)
            await events.EnqueueAsync(new Dfo.Server.Abstractions.Achievements.ItemUsed(session.CharacterId, item.ItemId, 1), ct);
        return new(ItemUsability.Allowed, item.ItemId, remaining, applied);
    }
}
