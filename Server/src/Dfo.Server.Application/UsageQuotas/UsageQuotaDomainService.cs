using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.UsageQuotas;

/// <summary>The sole quota mutation authority; domains retain eligibility, limits, and period interpretation.</summary>
internal sealed class UsageQuotaDomainService(IUsageQuotaStore store, IDomainTransactionAccessor transactions,
    IEventService events) : IUsageQuotaDomainService,
    IDomainEventSubscriber<ConsumableUsageRecorded>, IDomainEventSubscriber<PremiumUsageRecorded>,
    IDomainEventSubscriber<MirrorEntryRecorded>, IDomainEventSubscriber<MirrorRewardRecorded>,
    IDomainEventSubscriber<AltarEntryRecorded>, IDomainEventSubscriber<DimensionEntryRecorded>, IDomainEventSubscriber<DespairQuotaRecorded>
{
    public ValueTask<UsageQuotaValue?> ReadAsync(CharacterId character, UsageQuotaKey key, CancellationToken ct) =>
        store.ReadAsync(character, key, ct);
    public async ValueTask<IReadOnlyDictionary<int, ConsumableUsageRow>> ReadConsumablesAsync(CharacterId character, CancellationToken ct) =>
        (await store.ReadConsumablesAsync(character, ct).ConfigureAwait(false))
            .ToDictionary(x => x.Key, x => new ConsumableUsageRow(x.Value.UsedCount, x.Value.LastUsedAt));
    public async ValueTask<MirrorDailyQuota> ReadMirrorAsync(CharacterId character, DateOnly day, CancellationToken ct)
    {
        var value = await store.ReadMirrorAsync(character, day, ct).ConfigureAwait(false);
        if (value.Entries < 0 || value.Clears < 0) throw new InvalidDataException("Invalid mirror quota count.");
        return value;
    }

    public ValueTask HandleAsync(DomainEventEnvelope<ConsumableUsageRecorded> message, CancellationToken ct)
    {
        var p = message.Payload;
        return ApplyAsync([new(p.CharacterId, UsageQuotaKey.Consumable(p.ItemId),
            p.Expected is null ? null : new(p.Expected.UsedCount, p.Expected.LastUsedAt),
            new(p.Value.UsedCount, p.Value.LastUsedAt))], ct);
    }
    public ValueTask HandleAsync(DomainEventEnvelope<PremiumUsageRecorded> message, CancellationToken ct)
    {
        var p = message.Payload;
        return ApplyAsync(p.Updates.Select(x => new UsageQuotaWrite(p.CharacterId, UsageQuotaKey.Premium(x.Counter),
            x.Expected is null ? null : new(x.Expected.UsedCount, x.Expected.LastConsumedAt), new(x.UsedCount, x.At))).ToArray(), ct);
    }
    public ValueTask HandleAsync(DomainEventEnvelope<MirrorEntryRecorded> message, CancellationToken ct) =>
        ApplyAsync(message.Payload.Participants.Select(x => new UsageQuotaWrite(x.CharacterId,
            UsageQuotaKey.Mirror("entries", x.Day), new(x.ExpectedEntries, null), new(x.Entries, null), x.AccountId)).ToArray(), ct);
    public ValueTask HandleAsync(DomainEventEnvelope<MirrorRewardRecorded> message, CancellationToken ct)
    {
        var p = message.Payload;
        return ApplyAsync([
            new(p.CharacterId, UsageQuotaKey.Mirror("clears", p.Day), new(p.Expected.Clears, null), new(p.Value.Clears, null), p.AccountId),
            new(p.CharacterId, UsageQuotaKey.Mirror("daily-reward", p.Day), new(p.Expected.DailyRewardClaimed ? 1 : 0, null),
                new(p.Value.DailyRewardClaimed ? 1 : 0, null), p.AccountId)], ct);
    }
    public ValueTask HandleAsync(DomainEventEnvelope<DimensionEntryRecorded> message, CancellationToken ct) =>
        ApplyAsync(message.Payload.Participants.Select(x => new UsageQuotaWrite(x.CharacterId, UsageQuotaKey.Dimension(x.Period),
            x.Expected, new(x.Used, null))).ToArray(), ct);

    public ValueTask HandleAsync(DomainEventEnvelope<DespairQuotaRecorded> message, CancellationToken ct)
    {
        var p = message.Payload;
        return ApplyAsync([new(p.CharacterId, UsageQuotaKey.Despair(p.Counter, p.Period), p.Expected, new(p.Used, null))], ct);
    }

    public ValueTask HandleAsync(DomainEventEnvelope<AltarEntryRecorded> message, CancellationToken ct)
    {
        var p = message.Payload;
        return ApplyAsync([new(p.CharacterId, UsageQuotaKey.Altar(p.DungeonId, p.Period), p.Expected, new(p.Used, null))], ct);
    }

    private async ValueTask ApplyAsync(IReadOnlyList<UsageQuotaWrite> writes, CancellationToken ct)
    {
        if (transactions.Current is not { IsReadOnly: false })
            throw new InvalidOperationException("Quota mutations require the current writable business transaction.");
        ct.ThrowIfCancellationRequested();
        var identities = new HashSet<(CharacterId, UsageQuotaKey)>();
        // Validate the entire decided batch before any write. Failed CAS remains an exception
        // so an already-written sibling and the business inventory both roll back together.
        foreach (var write in writes)
        {
            if (!identities.Add((write.CharacterId, write.Key)) || write.Value.UsedCount < 0
                || write.Expected?.UsedCount < 0 || string.IsNullOrWhiteSpace(write.Key.Name)
                || !Enum.IsDefined(write.Key.Family)
                || write.Key.Family is not (UsageQuotaFamily.MirrorDungeon or UsageQuotaFamily.DimensionDungeon or UsageQuotaFamily.DespairTower or UsageQuotaFamily.AltarDungeon) && (write.Key.Day is not null || write.Value.LastUsedAt is null)
                || write.Key.Family == UsageQuotaFamily.AltarDungeon && (write.Key.Day is null || write.Value.LastUsedAt is not null
                    || write.Key.Name is not ("11006" or "11007"))
                || write.Key.Family == UsageQuotaFamily.DimensionDungeon && (write.Key.Day is null
                    || write.Key.Name != "entries" || write.Value.LastUsedAt is not null)
                || write.Key.Family == UsageQuotaFamily.DespairTower && (write.Key.Day is null || write.Value.LastUsedAt is not null
                    || write.Key.Name is not ("entries" or "clears"))
                || write.Key.Family == UsageQuotaFamily.Premium && write.Value.UsedCount > byte.MaxValue
                || write.Key.Family == UsageQuotaFamily.MirrorDungeon && (write.Key.Day is null || write.Value.LastUsedAt is not null
                    || write.Key.Name is not ("entries" or "clears" or "daily-reward")
                    || write.Key.Name == "daily-reward" && write.Value.UsedCount > 1))
                throw new InvalidDataException("Invalid or duplicate quota change.");
            if (await store.ReadAsync(write.CharacterId, write.Key, ct).ConfigureAwait(false) != write.Expected)
                throw new InvalidOperationException("Quota changed since the business decision.");
        }
        foreach (var write in writes)
            if (!await store.CompareExchangeAsync(write, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Quota changed during settlement.");
        foreach (var identity in writes.Select(x => (x.CharacterId, x.Key.Family)).Distinct())
            _ = await events.EnqueueAsync(new UsageQuotaChanged(identity.CharacterId, identity.Family), ct).ConfigureAwait(false);
    }
}
