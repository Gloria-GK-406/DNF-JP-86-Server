using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.UsageQuotas;

public enum UsageQuotaFamily { Consumable, Premium, MirrorDungeon, DimensionDungeon, DespairTower, AltarDungeon, TournamentDungeon }

/// <summary>Storage identity, independent of limits and reset rules owned by each domain.</summary>
public sealed record UsageQuotaKey(UsageQuotaFamily Family, string Name, DateOnly? Day = null)
{
    public static UsageQuotaKey Tournament(int dungeonId, DateOnly period) => new(UsageQuotaFamily.TournamentDungeon, dungeonId.ToString(System.Globalization.CultureInfo.InvariantCulture), period);
    public static UsageQuotaKey Altar(int dungeonId, DateOnly period) => new(UsageQuotaFamily.AltarDungeon, dungeonId.ToString(System.Globalization.CultureInfo.InvariantCulture), period);
    public static UsageQuotaKey Despair(string counter, DateOnly period) => new(UsageQuotaFamily.DespairTower, counter, period);
    public static UsageQuotaKey Dimension(DateOnly period) => new(UsageQuotaFamily.DimensionDungeon, "entries", period);
    public static UsageQuotaKey Consumable(int itemId) => new(UsageQuotaFamily.Consumable,
        itemId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static UsageQuotaKey Premium(string name) => new(UsageQuotaFamily.Premium, name);
    public static UsageQuotaKey Mirror(string name, DateOnly day) => new(UsageQuotaFamily.MirrorDungeon, name, day);
}
public sealed record UsageQuotaValue(int UsedCount, DateTimeOffset? LastUsedAt);
public sealed record UsageQuotaWrite(CharacterId CharacterId, UsageQuotaKey Key,
    UsageQuotaValue? Expected, UsageQuotaValue Value, AccountId? Owner = null);

/// <summary>Compatibility storage for existing quota rows; writes are CAS inside the ambient transaction.</summary>
public interface IUsageQuotaStore
{
    ValueTask<UsageQuotaValue?> ReadAsync(CharacterId character, UsageQuotaKey key, CancellationToken ct);
    ValueTask<IReadOnlyDictionary<int, UsageQuotaValue>> ReadConsumablesAsync(CharacterId character, CancellationToken ct);
    ValueTask<MirrorDailyQuota> ReadMirrorAsync(CharacterId character, DateOnly day, CancellationToken ct);
    ValueTask<bool> CompareExchangeAsync(UsageQuotaWrite write, CancellationToken ct);
}

/// <summary>Authoritative read surface. Quota mutations are handled only by direct business-fact subscriptions.</summary>
public interface IUsageQuotaDomainService
{
    ValueTask<UsageQuotaValue?> ReadAsync(CharacterId character, UsageQuotaKey key, CancellationToken ct);
    ValueTask<IReadOnlyDictionary<int, ConsumableUsageRow>> ReadConsumablesAsync(CharacterId character, CancellationToken ct);
    ValueTask<MirrorDailyQuota> ReadMirrorAsync(CharacterId character, DateOnly day, CancellationToken ct);
}

// These facts are runtime-only. Registering them as durable deductions would consume twice.
public sealed record ConsumableUsageRecorded(CharacterId CharacterId, int ItemId,
    ConsumableUsageRow? Expected, ConsumableUsageRow Value) : IDomainEvent;
public sealed record PremiumUsageUpdate(string Counter, PremiumServiceUsageRow? Expected,
    int UsedCount, DateTimeOffset At);
public sealed record PremiumUsageRecorded(CharacterId CharacterId,
    ImmutableArray<PremiumUsageUpdate> Updates) : IDomainEvent;
public sealed record MirrorEntryQuotaUpdate(AccountId AccountId, CharacterId CharacterId,
    DateOnly Day, int ExpectedEntries, int Entries);
public sealed record MirrorEntryRecorded(ImmutableArray<MirrorEntryQuotaUpdate> Participants) : IDomainEvent;
public sealed record MirrorRewardRecorded(AccountId AccountId, CharacterId CharacterId,
    DateOnly Day, MirrorDailyQuota Expected, MirrorDailyQuota Value) : IDomainEvent;

public sealed record DespairQuotaRecorded(CharacterId CharacterId, DateOnly Period, string Counter, UsageQuotaValue? Expected, int Used) : IDomainEvent;

public sealed record DimensionQuotaSnapshot(DateOnly Period, int Used, int Remaining, int ExtraRemaining);
public sealed record DimensionEntryQuotaUpdate(CharacterId CharacterId, DateOnly Period, UsageQuotaValue? Expected, int Used);
public sealed record DimensionEntryRecorded(ImmutableArray<DimensionEntryQuotaUpdate> Participants) : IDomainEvent;

/// <summary>Committed-counter refresh signal. Consumers read authoritative state rather than replaying a delta.</summary>
public sealed record UsageQuotaChanged(CharacterId CharacterId, UsageQuotaFamily Family) : IDomainEvent;

public static class UsageQuotaRows
{
    public static ConsumableUsageRow? Consumable(this UsageQuotaValue? value) =>
        value is null ? null : new(value.UsedCount, value.LastUsedAt);
    public static PremiumServiceUsageRow? Premium(this UsageQuotaValue? value) =>
        value is null ? null : new(value.UsedCount, value.LastUsedAt);
}

public sealed record AltarEntryRecorded(CharacterId CharacterId, int DungeonId, DateOnly Period, UsageQuotaValue? Expected, int Used) : IDomainEvent;
public sealed record TournamentEntryRecorded(CharacterId CharacterId, int DungeonId, DateOnly Period, UsageQuotaValue? Expected, int Used) : IDomainEvent;
