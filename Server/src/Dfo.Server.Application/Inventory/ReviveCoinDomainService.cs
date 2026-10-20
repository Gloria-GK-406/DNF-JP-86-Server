using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class ReviveCoinRuleService(IDailyResetRuleService daily) : IReviveCoinRuleService
{
    public bool SupportsConsumable(int itemId) => itemId == 42;
    public DateOnly Period(DateTimeOffset at) => DateOnly.FromDateTime(daily.PeriodStart(at).ToOffset(TimeSpan.FromHours(8)).DateTime);
    public GrantOpeningVirtualEffect Grant() => new(1, 1, 0, new(1, 1));
    public bool CanClaim(int balance, int used) => balance == 0 && used == 0;
}

internal sealed class ReviveCoinQueryDomainService(IReviveCoinRuleService rules, IUsageQuotaDomainService quotas, TimeProvider clock) : IReviveCoinQueryDomainService
{
    public async ValueTask<bool> ClaimedAsync(CharacterId character, CancellationToken ct) =>
        (await quotas.ReadAsync(character, UsageQuotaKey.ReviveCoin(rules.Period(clock.GetUtcNow())), ct).ConfigureAwait(false))?.UsedCount > 0;
}

internal sealed class ReviveCoinDomainService(IReviveCoinRuleService rules, IItemUseEffectRuleService uses,
    ICharacterDomainService characters, IOpeningInventoryDomainService inventory,
    ICharacterContainerDomainService containers, IUsageQuotaDomainService quotas,
    ISelectedSessionDomainService sessions, IEventService events, TimeProvider clock, IReviveCoinQueryDomainService query) : IReviveCoinDomainService
{
    public async ValueTask<ItemUseApplication?> TryUseAsync(SelectedSessionIdentity session, BusinessSlot source, ItemReference item, CancellationToken ct)
    {
        if (!rules.SupportsConsumable(item.ItemId)) return null;
        var rule = await uses.RuleForAsync(item.ItemId, ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        var facts = rule is null || !rule.IsReadable ? null : await characters.Get(session.CharacterId)
            .ItemUseFactsAsync(uses.CooldownKeyFor(item.ItemId, rule), ct).ConfigureAwait(false);
        var plan = uses.Build(rule, source, item, facts, null, at);
        if (plan.Status != ItemUsability.Allowed) return new(plan.Status, item.ItemId, item.Count);
        if (plan.Effects.OfType<ConsumeFromContainerEffect>().Count(x => x.Slot == source && x.ExpectedItemId == item.ItemId && x.Count == 1) != 1)
            return new(ItemUsability.NotAUsableItem, item.ItemId, item.Count);
        var settled = await inventory.ApplyAsync(session, plan.Effects.Append(rules.Grant()).ToArray(), at, ct).ConfigureAwait(false);
        await events.EnqueueAsync(new Dfo.Server.Abstractions.Achievements.ItemUsed(session.CharacterId, item.ItemId, 1), ct).ConfigureAwait(false);
        return new(ItemUsability.Allowed, item.ItemId,
            settled.Applied.Changes<ContainerConsumptionChange>().Single(x => x.Slot == source).RemainingCount, settled.Applied)
            { OpeningReceipt = settled.Inventory };
    }

    public ValueTask<bool> ClaimedAsync(CharacterId character, CancellationToken ct) => query.ClaimedAsync(character, ct);

    public async ValueTask<DailyReviveCoinResult> ClaimAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (sessions.FindByCharacter(session.CharacterId) != session) return new(false, false);
        var at = clock.GetUtcNow();
        var period = rules.Period(at);
        var previous = await quotas.ReadAsync(session.CharacterId, UsageQuotaKey.ReviveCoin(period), ct).ConfigureAwait(false);
        var grant = rules.Grant();
        var bag = containers.FindContainer(session.CharacterId, InventoryContainerType.Main);
        var coin = bag is null ? null : await bag.GetAsync(grant.Slot, ct).ConfigureAwait(false);
        if (bag is null || coin is not null && (coin.ItemId != grant.CoreItemId || !coin.IsPile)) return new(false, previous?.UsedCount > 0);
        if (!rules.CanClaim(coin?.Count ?? 0, previous?.UsedCount ?? 0)) return new(false, previous?.UsedCount > 0,
            Wallet: coin?.Snapshot.ToCore() ?? StackableItemCore.Create(grant.ItemKind, grant.CoreItemId, 0), PreparedAt: at);
        var settled = await inventory.ApplyAsync(session, [grant], at, ct).ConfigureAwait(false);
        await events.DirectAsync(new ReviveCoinClaimRecorded(session.CharacterId, period, previous), ct).ConfigureAwait(false);
        return new(true, true, settled.Inventory);
    }
}
