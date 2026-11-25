using System.Collections.Immutable;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

/// <summary>One paid challenge per ordinary character or per total-match account.</summary>
internal sealed class PvpColosseumDomainService(
    IPvpColosseumStore store, PvpColosseumRuleService rules, IPvpSettlementRuleService days, TimeProvider clock,
    PvpDuelDomainService duel, PvpTraditionalDuelDomainService traditional,
    IDomainTransactionAccessor transactions, IItemDomainStore inventory,
    IItemDeliveryDomainService delivery,
    PvpMatchmakingRuleService matchmakingRules) : IPvpColosseumDomainService
{
    private readonly PvpVictoryEmblems emblems = new(transactions, inventory);

    public async ValueTask<PvpColosseumLobbyState> ReadLobbyAsync(SelectedSessionIdentity actor, CancellationToken ct)
    {
        var individual = Owner(actor, PvpColosseumKind.Individual);
        var total = Owner(actor, PvpColosseumKind.TotalMatch);
        var day = days.GameDayOf(clock.GetUtcNow());
        var individualRun = await store.ReadAsync(individual, ct).ConfigureAwait(false);
        var totalRun = await store.ReadAsync(total, ct).ConfigureAwait(false);
        return new(individualRun, totalRun?.Recipient == actor.CharacterId ? totalRun : null,
            await store.EntriesAsync(individual, day, ct).ConfigureAwait(false),
            await store.EntriesAsync(total, day, ct).ConfigureAwait(false));
    }

    public async ValueTask<PvpColosseumEntry?> EnterAsync(SelectedSessionIdentity actor, ChannelEntryIdentity channel,
        PvpColosseumKind kind, CancellationToken ct)
    {
        if (ChannelKind(channel) != kind) return null;
        var owner = Owner(actor, kind);
        var policy = rules.Load();
        var now = clock.GetUtcNow();
        var day = days.GameDayOf(now);
        var current = await store.ReadAsync(owner, ct).ConfigureAwait(false);
        if (current is not null)
            return !current.Complete && current.Recipient == actor.CharacterId
                ? new(current, policy.EntranceItem, policy.EntranceCost, null) : null;

        var qualified = kind == PvpColosseumKind.Individual
            ? await duel.CanEnterColosseumAsync(actor.CharacterId, policy.MinimumRp, ct).ConfigureAwait(false)
            : await traditional.CanEnterColosseumAsync(actor, now, ct).ConfigureAwait(false);
        if (!qualified || await store.EntriesAsync(owner, day, ct).ConfigureAwait(false) >= policy.DailyLimit)
            return null;
        var balance = await emblems.ChangeAsync(actor.CharacterId, -policy.EntranceCost, ct).ConfigureAwait(false);
        if (balance is null) return null;
        var run = new PvpColosseumRun(Guid.NewGuid(), owner, actor.CharacterId, day, 0, 0, []);
        await store.InsertAsync(run, ct).ConfigureAwait(false);
        return new(run, policy.EntranceItem, policy.EntranceCost, balance);
    }

    internal ValueTask<PvpColosseumRun?> ReadForQueueAsync(SelectedSessionIdentity actor, ChannelEntryIdentity channel,
        CancellationToken ct) => ChannelKind(channel) is { } kind
        ? store.ReadAsync(Owner(actor, kind), ct) : ValueTask.FromResult<PvpColosseumRun?>(null);

    internal async ValueTask<HashSet<int>> SettleAsync(PvpMatchConclusion battle, CancellationToken ct)
    {
        var winners = new HashSet<int>();
        if (battle.ColosseumRuns.IsEmpty || battle.Terminal.Kind != PvpMatchOutcomeKind.Decided
            || !(matchmakingRules.AppliesTo(battle) || matchmakingRules.AppliesToTotalMatch(battle))
            || ChannelKind(battle.Channel) is not { } kind)
            return winners;

        var policy = rules.Load();
        foreach (var fighter in battle.Fighters)
        {
            if (!battle.ColosseumRuns.TryGetValue(fighter.Seat, out var id)) continue;
            var run = await store.ReadAsync(Owner(fighter.Session, kind), ct).ConfigureAwait(false);
            if (run is null || run.Id != id || run.Complete || run.Recipient != fighter.CharacterId)
                throw new InvalidOperationException($"Duel {battle.MatchId.Value} has a stale Colosseum run.");
            var won = !fighter.Fled && PvpSettlementDomainService.OutcomeOf(battle, fighter) == PvpOutcome.Win;
            var updated = run with { Wins = run.Wins + (won ? 1 : 0), Losses = run.Losses + (won ? 0 : 1) };
            if (updated.Wins >= policy.MaxWins || updated.Losses >= policy.MaxLosses)
                updated = updated with { Prizes = rules.DrawPrizes(updated.Wins, policy) };
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
        if (ChannelKind(channel) is not { } kind) return null;
        var run = await store.ReadAsync(Owner(actor, kind), ct).ConfigureAwait(false);
        if (run is not { Complete: true } || run.Recipient != actor.CharacterId) return null;
        return await DeliverAsync(run, actor, ItemDeliveryMode.InventoryThenMail, ct).ConfigureAwait(false);
    }

    private async ValueTask<PvpColosseumDelivery> DeliverAsync(PvpColosseumRun run, SelectedSessionIdentity actor,
        ItemDeliveryMode mode, CancellationToken ct)
    {
        // Delivery and closing the challenge belong to the caller's write unit.
        var emblemCount = run.Prizes.Where(prize => prize.ItemId == PvpVictoryEmblems.ItemId).Sum(prize => prize.Count);
        var balance = emblemCount == 0 ? null
            : await emblems.ChangeAsync(actor.CharacterId, emblemCount, ct).ConfigureAwait(false);
        var grants = run.Prizes.Where(prize => prize.ItemId != PvpVictoryEmblems.ItemId)
            .Select(prize => new ItemDeliveryGrant(prize.ItemId, prize.Count)).ToArray();
        var items = await delivery.DeliverAsync(actor.AccountId, actor.CharacterId, grants, "Colosseum", ct,
            mode, new(null, $"pvp-colosseum-{run.Id:N}")).ConfigureAwait(false);
        await store.CloseAsync(run.Id, ct).ConfigureAwait(false);
        return new(balance, items);
    }

    internal static PvpColosseumKind? ChannelKind(ChannelEntryIdentity channel) => channel.Kind switch
    {
        ChannelEntryKind.MatchmakingDuel => PvpColosseumKind.Individual,
        ChannelEntryKind.TotalMatchDuel => PvpColosseumKind.TotalMatch,
        _ => null,
    };

    private static PvpColosseumOwner Owner(SelectedSessionIdentity actor, PvpColosseumKind kind) =>
        PvpColosseumOwner.Of(kind, actor.CharacterId, actor.AccountId);
}
