using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Raids;

public enum AntonRaidMapStatus : byte { Open, Closed, Hidden, Cleared }

public sealed record AntonRaidMapRule(int DungeonId, AntonRaidMapStatus InitialStatus,
    uint HitSymbol, uint RequiredClears, int PartyLimit, int CoinLimit, DungeonDifficulty Difficulty);

public enum AntonRaidConditionKind { Symbol, MapStatus, MapChanged, SymbolChanged, TimerEnded }

public readonly record struct AntonRaidCondition(AntonRaidConditionKind Kind, uint Key, uint Value = 0);

public enum AntonRaidActionKind { SetMap, ReserveMap, SetSymbol, SetTimer, ResetTimer, ClearPhase, OpenHatcheries }

public readonly record struct AntonRaidAction(AntonRaidActionKind Kind, uint Key = 0, uint Value = 0, int Seconds = 0);

public sealed record AntonRaidTrigger(IReadOnlyList<AntonRaidCondition> Conditions, IReadOnlyList<AntonRaidAction> Actions);

public sealed record AntonRaidPhaseRule(IReadOnlyList<AntonRaidMapRule> Maps, IReadOnlyList<AntonRaidTrigger> Triggers);

public sealed record AntonRaidPhaseParticipant(AccountId AccountId, CharacterId CharacterId, byte Squad, byte Role);

public sealed record AntonRaidPhaseOutcome(Guid Instance, uint Number, byte Phase, bool Succeeded, int DeathCount,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt, IReadOnlyList<AntonRaidPhaseParticipant> Eligible,
    IReadOnlyList<CharacterId> Audience);

public enum AntonRaidCombatDeadlineKind : byte { Attack, MapTimer, ReservedMap, Intermission, Rejoin, ShieldRecovery }

public readonly record struct AntonRaidCombatDeadline(Guid Instance, uint Number, byte Phase, long Generation,
    AntonRaidCombatDeadlineKind Kind, uint Key, byte TimerType, DateTimeOffset At, int Duration);

public sealed record AntonRaidCombatSnapshot(Guid Instance, uint Number, byte Phase, DateTimeOffset StartedAt,
    uint InfectedMap, IReadOnlyDictionary<int, AntonRaidMapStatus> Maps, IReadOnlyDictionary<uint, uint> Symbols,
    IReadOnlyList<AntonRaidCombatDeadline> Timers, IReadOnlyList<AntonRaidSortieSnapshot> Sorties)
{
    public IReadOnlyList<AntonRaidBuffGroup> Buffs { get; init; } = [];
}

public sealed record AntonRaidSortieSnapshot(Guid Run, int DungeonId, byte Squad,
    IReadOnlyList<CharacterId> Participants, bool Cleared, int UsedCoins, IReadOnlyList<uint> MonsterHitPoints);

public readonly record struct AntonRaidEntryIdentity(Guid Instance, uint Number, byte Phase, long Generation, byte Squad);

public sealed record AntonRaidBuffRule(byte Type, bool Party, int Cooldown, int Duration, int Effect);
/// <summary>A null squad applies the buff to the raid.</summary>
public sealed record AntonRaidBuffTarget(byte? Squad, CharacterId Character, DateTimeOffset ActiveUntil, DateTimeOffset CooldownUntil);
public sealed record AntonRaidBuffGroup(byte Type, IReadOnlyList<AntonRaidBuffTarget> Targets);
