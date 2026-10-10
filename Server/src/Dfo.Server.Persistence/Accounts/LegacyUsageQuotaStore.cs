using System.Globalization;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Persistence.Accounts;

/// <summary>Preserves the existing tables, counter keys, timestamps, and mirror midnight buckets.</summary>
internal sealed class LegacyUsageQuotaStore(IConsumableUsageTable consumables,
    IPremiumServiceUsageTable premiums, IMirrorAradQuestDomainStore mirror) : IUsageQuotaStore
{
    public async ValueTask<UsageQuotaValue?> ReadAsync(CharacterId character, UsageQuotaKey key, CancellationToken ct)
    {
        switch (key.Family)
        {
            case UsageQuotaFamily.Consumable:
                var item = await consumables.ReadAsync(character, ItemId(key), ct).ConfigureAwait(false);
                return item is null ? null : new(item.UsedCount, item.LastUsedAt);
            case UsageQuotaFamily.Premium:
                var premium = await premiums.ReadAsync(character, key.Name, ct).ConfigureAwait(false);
                return premium is null ? null : new(premium.UsedCount, premium.LastConsumedAt);
            case UsageQuotaFamily.MirrorDungeon:
                var row = await mirror.ReadQuotaAsync(character, Day(key), ct).ConfigureAwait(false);
                return new(MirrorValue(row, key.Name), null);
            default: throw new ArgumentOutOfRangeException(nameof(key));
        }
    }
    public async ValueTask<IReadOnlyDictionary<int, UsageQuotaValue>> ReadConsumablesAsync(CharacterId character, CancellationToken ct) =>
        (await consumables.ReadAllAsync(character, ct).ConfigureAwait(false))
            .ToDictionary(x => x.Key, x => new UsageQuotaValue(x.Value.UsedCount, x.Value.LastUsedAt));

    public ValueTask<MirrorDailyQuota> ReadMirrorAsync(CharacterId character, DateOnly day, CancellationToken ct) =>
        mirror.ReadQuotaAsync(character, day, ct);

    public async ValueTask<bool> CompareExchangeAsync(UsageQuotaWrite write, CancellationToken ct)
    {
        var (character, key, expected, value, owner) = write;
        switch (key.Family)
        {
            case UsageQuotaFamily.Consumable:
                return await consumables.TryWriteAsync(character, ItemId(key), expected.Consumable(),
                    value.UsedCount, value.LastUsedAt!.Value, ct).ConfigureAwait(false) == 1;
            case UsageQuotaFamily.Premium:
                return await premiums.TryWriteAsync(character, key.Name, expected.Premium(),
                    value.UsedCount, value.LastUsedAt!.Value, ct).ConfigureAwait(false) == 1;
            case UsageQuotaFamily.MirrorDungeon:
                // All writes are serialized by the existing business transaction admission.
                // Read-check-write joins that same SQLite transaction, preserving other fields.
                var day = Day(key);
                var current = await mirror.ReadQuotaAsync(character, day, ct).ConfigureAwait(false);
                if (expected != new UsageQuotaValue(MirrorValue(current, key.Name), null)) return false;
                var next = key.Name switch
                {
                    "entries" => current with { Entries = value.UsedCount },
                    "clears" => current with { Clears = value.UsedCount },
                    "daily-reward" when value.UsedCount is 0 or 1 => current with { DailyRewardClaimed = value.UsedCount == 1 },
                    _ => throw new InvalidDataException("Unknown mirror quota field."),
                };
                await mirror.WriteQuotaAsync(owner ?? throw new InvalidOperationException("Mirror quota requires its owner."),
                    character, day, next, ct).ConfigureAwait(false);
                return true;
            default: throw new ArgumentOutOfRangeException(nameof(write));
        }
    }
    private static int ItemId(UsageQuotaKey key) => int.TryParse(key.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
        ? id : throw new InvalidDataException("Invalid consumable quota key.");
    private static DateOnly Day(UsageQuotaKey key) => key.Day ?? throw new InvalidDataException("Mirror quota requires a day.");
    private static int MirrorValue(MirrorDailyQuota quota, string name) => name switch
    {
        "entries" => quota.Entries, "clears" => quota.Clears, "daily-reward" => quota.DailyRewardClaimed ? 1 : 0,
        _ => throw new InvalidDataException("Unknown mirror quota field."),
    };
}
