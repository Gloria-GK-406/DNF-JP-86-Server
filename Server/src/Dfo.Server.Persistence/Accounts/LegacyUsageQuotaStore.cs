using System.Globalization;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Persistence.Accounts;

/// <summary>Preserves the existing tables, counter keys, timestamps, and mirror per-game-day buckets.</summary>
internal sealed class LegacyUsageQuotaStore(IConsumableUsageTable consumables,
    IPremiumServiceUsageTable premiums, IMirrorAradQuestDomainStore mirror, SqliteDatabase database) : IUsageQuotaStore
{
    public async ValueTask<UsageQuotaValue?> ReadAsync(CharacterId character, UsageQuotaKey key, CancellationToken ct)
    {
        switch (key.Family)
        {
            case UsageQuotaFamily.GiantDragonCrystalExchange:
                return (await database.ReadAllAsync("""
                    SELECT value FROM character_daily_counters WHERE character_id=@id
                    AND counter_key='giant-dragon.quest.8557' AND period=@period
                    """, p => { p.AddWithValue("@id", character.Value); p.AddWithValue("@period", key.Name); },
                    r => new UsageQuotaValue(r.GetInt32(0), null), ct).ConfigureAwait(false)).SingleOrDefault();
            case UsageQuotaFamily.ReviveCoin:
                // Legacy claims used one key with an explicit period; retain only that day's claim.
                return (await database.ReadAllAsync("""
                    SELECT value FROM character_daily_counters WHERE character_id=@id
                    AND (counter_key=@key OR (counter_key='revive_coin_daily_claim' AND period=@period AND value>0))
                    ORDER BY value DESC LIMIT 1
                    """, p => { p.AddWithValue("@id", character.Value); p.AddWithValue("@key", DimensionKey(key));
                        p.AddWithValue("@period", Day(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); },
                    r => new UsageQuotaValue(r.GetInt32(0), null), ct).ConfigureAwait(false)).SingleOrDefault();
            case UsageQuotaFamily.GoldSilverHorn:
            case UsageQuotaFamily.TournamentDungeon:
            case UsageQuotaFamily.LostLand:
            case UsageQuotaFamily.HerasDungeon:
            case UsageQuotaFamily.AltarDungeon:
            case UsageQuotaFamily.DespairTower:
            case UsageQuotaFamily.DimensionDungeon:
                return (await database.ReadAllAsync("SELECT value FROM character_daily_counters WHERE character_id=@id AND counter_key=@key",
                    p => { p.AddWithValue("@id", character.Value); p.AddWithValue("@key", DimensionKey(key)); },
                    r => new UsageQuotaValue(r.GetInt32(0), null), ct).ConfigureAwait(false)).SingleOrDefault();
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
            case UsageQuotaFamily.GiantDragonCrystalExchange:
                // Retain the fixed legacy key and its Unix-period field. A stale null claim
                // may replace an older period, but cannot overwrite this period's winning claim.
                return await database.ExecuteAsync("""
                    INSERT INTO character_daily_counters(character_id,counter_key,period,value,last_consumed_at)
                    SELECT @id,'giant-dragon.quest.8557',@period,@value,NULL
                    WHERE @exists=0 OR EXISTS(SELECT 1 FROM character_daily_counters
                        WHERE character_id=@id AND counter_key='giant-dragon.quest.8557'
                        AND period=@period AND value=@old)
                    ON CONFLICT(character_id,counter_key) DO UPDATE SET period=excluded.period,value=excluded.value,last_consumed_at=NULL
                    WHERE (@exists=0 AND (period IS NULL OR period<>@period)) OR (@exists=1 AND period=@period AND value=@old)
                    """, p => { p.AddWithValue("@id", character.Value); p.AddWithValue("@period", key.Name);
                        p.AddWithValue("@value", value.UsedCount); p.AddWithValue("@exists", expected is null ? 0 : 1);
                        p.AddWithValue("@old", expected?.UsedCount ?? 0); }, ct).ConfigureAwait(false) == 1;
            case UsageQuotaFamily.ReviveCoin:
            case UsageQuotaFamily.GoldSilverHorn:
            case UsageQuotaFamily.TournamentDungeon:
            case UsageQuotaFamily.LostLand:
            case UsageQuotaFamily.HerasDungeon:
            case UsageQuotaFamily.AltarDungeon:
            case UsageQuotaFamily.DespairTower:
            case UsageQuotaFamily.DimensionDungeon:
                return await database.ExecuteAsync("""
                    INSERT INTO character_daily_counters(character_id,counter_key,period,value,last_consumed_at)
                    SELECT @id,@key,@period,@value,NULL
                    WHERE @exists=0 OR EXISTS(SELECT 1 FROM character_daily_counters
                        WHERE character_id=@id AND counter_key=@key AND value=@old)
                    ON CONFLICT(character_id,counter_key) DO UPDATE SET value=excluded.value
                    WHERE @exists=1 AND value=@old
                    """, p => { p.AddWithValue("@id", character.Value); p.AddWithValue("@key", DimensionKey(key));
                        p.AddWithValue("@period", Day(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        p.AddWithValue("@value", value.UsedCount); p.AddWithValue("@exists", expected is null ? 0 : 1);
                        p.AddWithValue("@old", expected?.UsedCount ?? 0); }, ct).ConfigureAwait(false) == 1;
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
    private static string DimensionKey(UsageQuotaKey key) => key.Family == UsageQuotaFamily.ReviveCoin
        ? key.Name == "claim" ? "revive_coin_daily_claim:" + Day(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new InvalidDataException("Invalid revive coin quota key.")
        : key.Family == UsageQuotaFamily.GoldSilverHorn
        ? key.Name == "entries" ? "gold-silver-horn.4123.entries:" + Day(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new InvalidDataException("Invalid gold/silver horn quota key.")
        : key.Family == UsageQuotaFamily.LostLand
        ? key.Name == "entries" ? "lost-land.4000.entries:" + Day(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new InvalidDataException("Invalid LostLand quota key.")
        : key.Family == UsageQuotaFamily.HerasDungeon
        ? key.Name is "entries" or "bonus" ? "heras.3900." + key.Name + ":" + Day(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new InvalidDataException("Invalid Heras quota key.")
        : key.Family == UsageQuotaFamily.TournamentDungeon
        ? key.Name is "120" or "121" or "122" ? "tournament." + key.Name + ":" + Day(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new InvalidDataException("Invalid tournament quota key.")
        : key.Family == UsageQuotaFamily.AltarDungeon
        ? key.Name is "11006" or "11007" ? "altar." + key.Name + ":" + Day(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new InvalidDataException("Invalid altar quota key.")
        : key.Family == UsageQuotaFamily.DespairTower
        ? key.Name is "entries" or "clears" ? "despair." + key.Name + ":" + Day(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new InvalidDataException("Invalid despair quota key.")
        : key.Name == "entries"
        ? "dimension.entries:" + Day(key).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : throw new InvalidDataException("Invalid dimension quota key.");
    private static int ItemId(UsageQuotaKey key) => int.TryParse(key.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
        ? id : throw new InvalidDataException("Invalid consumable quota key.");
    private static DateOnly Day(UsageQuotaKey key) => key.Day ?? throw new InvalidDataException("Mirror quota requires a day.");
    private static int MirrorValue(MirrorDailyQuota quota, string name) => name switch
    {
        "entries" => quota.Entries, "clears" => quota.Clears, "daily-reward" => quota.DailyRewardClaimed ? 1 : 0,
        _ => throw new InvalidDataException("Unknown mirror quota field."),
    };
}
