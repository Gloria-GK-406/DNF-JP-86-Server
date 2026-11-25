using System.Collections.Immutable;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

/// <summary>One paid challenge per ordinary character or per total-match account.</summary>
internal sealed class PvpColosseumDomainService(
    IPvpColosseumStore store, PvpColosseumRuleService rules, IPvpSettlementRuleService settlement, TimeProvider clock,
    PvpDuelDomainService duel, PvpTraditionalDuelDomainService traditional,
    PvpVictoryEmblems emblems,
    IItemDeliveryDomainService delivery,
    PvpMatchmakingRuleService matchmakingRules) : IPvpColosseumDomainService
{
    public async ValueTask<PvpColosseumLobbyState> ReadLobbyAsync(SelectedSessionIdentity actor, CancellationToken ct)
    {
        var individual = Owner(actor, PvpColosseumKind.Individual);
        var total = Owner(actor, PvpColosseumKind.TotalMatch);
        var day = settlement.GameDayOf(clock.GetUtcNow());
        var individualRun = await store.ReadAsync(individual, ct).ConfigureAwait(false);
        var totalRun = await store.ReadAsync(total, ct).ConfigureAwait(false);
        return new(individualRun, totalRun?.Recipient == actor.CharacterId ? totalRun : null,
            await store.EntriesAsync(individual, day, ct).ConfigureAwait(false),
            await store.EntriesAsync(total, day, ct).ConfigureAwait(false));
    }

    public async ValueTask<PvpColosseumEntry?> EnterAsync(SelectedSessionIdentity actor, ChannelEntryIdentity channel,
        PvpColosseumKind kind, CancellationToken ct)
    {
        if (rules.KindOf(channel) != kind) return null;
        var owner = Owner(actor, kind);
        var terms = rules.EntryTerms();
        var now = clock.GetUtcNow();
        var day = settlement.GameDayOf(now);
        var current = await store.ReadAsync(owner, ct).ConfigureAwait(false);
        if (current is not null)
            return rules.CanResume(current, actor.CharacterId) ? new(current, terms.Item, terms.Cost, null) : null;

        var qualified = kind == PvpColosseumKind.Individual
            ? await duel.CanEnterColosseumAsync(actor.CharacterId, terms.MinimumRp, ct).ConfigureAwait(false)
            : await traditional.CanEnterColosseumAsync(actor, now, ct).ConfigureAwait(false);
        if (!qualified || !rules.CanStart(await store.EntriesAsync(owner, day, ct).ConfigureAwait(false)))
            return null;
        var balance = await emblems.ChangeAsync(actor.CharacterId, -terms.Cost, ct).ConfigureAwait(false);
        if (balance is null) return null;
        var run = new PvpColosseumRun(Guid.NewGuid(), owner, actor.CharacterId, day, 0, 0, []);
        await store.InsertAsync(run, ct).ConfigureAwait(false);
        return new(run, terms.Item, terms.Cost, balance);
    }

    internal async ValueTask<Guid?> ReadQueueRunAsync(SelectedSessionIdentity actor, ChannelEntryIdentity channel,
        PvpBattleMode mode, CancellationToken ct)
    {
        if (rules.KindOf(channel) is not { } kind) return null;
        var run = await store.ReadAsync(Owner(actor, kind), ct).ConfigureAwait(false);
        return run is not null && rules.CanQueue(run, actor.CharacterId, mode) ? run.Id : null;
    }

    internal async ValueTask<HashSet<int>> SettleAsync(PvpMatchConclusion battle, CancellationToken ct)
    {
        var winners = new HashSet<int>();
        if (battle.ColosseumRuns.IsEmpty || battle.Terminal.Kind != PvpMatchOutcomeKind.Decided
            || !(matchmakingRules.AppliesTo(battle) || matchmakingRules.AppliesToTotalMatch(battle))
            || rules.KindOf(battle.Channel) is not { } kind)
            return winners;

        foreach (var fighter in battle.Fighters)
        {
            if (!battle.ColosseumRuns.TryGetValue(fighter.Seat, out var id)) continue;
            var run = await store.ReadAsync(Owner(fighter.Session, kind), ct).ConfigureAwait(false);
            if (run is null || run.Id != id || run.Complete || run.Recipient != fighter.CharacterId)
                throw new InvalidOperationException($"Duel {battle.MatchId.Value} has a stale Colosseum run.");
            var won = settlement.OutcomeOf(battle, fighter) == PvpOutcome.Win;
            var updated = rules.Advance(run, won);
            await store.UpdateAsync(updated, ct).ConfigureAwait(false);
            if (updated.Complete && fighter.Fled && fighter.Departed)
                _ = await DeliverAsync(updated, fighter.Session, ItemDeliveryMode.MailOnly, ct).ConfigureAwait(false);
            if (won) winners.Add(fighter.Seat);
        }
        return winners;
    }

    public async ValueTask<PvpColosseumDelivery?> CollectAsync(SelectedSessionIdentity actor, ChannelEntryIdentity channel,
        CancellationToken ct)
    {
        if (rules.KindOf(channel) is not { } kind) return null;
        var run = await store.ReadAsync(Owner(actor, kind), ct).ConfigureAwait(false);
        if (run is not { Complete: true } || run.Recipient != actor.CharacterId) return null;
        return await DeliverAsync(run, actor, ItemDeliveryMode.InventoryThenMail, ct).ConfigureAwait(false);
    }

    private async ValueTask<PvpColosseumDelivery> DeliverAsync(PvpColosseumRun run, SelectedSessionIdentity actor,
        ItemDeliveryMode mode, CancellationToken ct)
    {
        // Delivery and closing the challenge belong to the caller's write unit.
        var emblemCount = run.Prizes.Where(prize => prize.ItemId == VictoryEmblemResource.ItemId).Sum(prize => prize.Count);
        var balance = emblemCount == 0 ? null
            : await emblems.ChangeAsync(actor.CharacterId, emblemCount, ct).ConfigureAwait(false);
        var grants = run.Prizes.Where(prize => prize.ItemId != VictoryEmblemResource.ItemId)
            .Select(prize => new ItemDeliveryGrant(prize.ItemId, prize.Count)).ToArray();
        var items = await delivery.DeliverAsync(actor.AccountId, actor.CharacterId, grants, "Colosseum", ct,
            mode, new(null, $"pvp-colosseum-{run.Id:N}")).ConfigureAwait(false);
        await store.CloseAsync(run.Id, ct).ConfigureAwait(false);
        return new(balance, items);
    }

    private static PvpColosseumOwner Owner(SelectedSessionIdentity actor, PvpColosseumKind kind) =>
        PvpColosseumOwner.Of(kind, actor.CharacterId, actor.AccountId);
}
