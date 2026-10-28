using System.Collections.Immutable;

namespace Dfo.Server.Abstractions.World;

public enum GuardianAltarDifficulty : byte { Easy, Medium, Hard }
public enum GuardianAltarShopKind : byte { Unit, Skill, Tower }

public sealed record GuardianAltarReward(string Kind, int ItemId, int Count);
public sealed record GuardianAltarStageMap(GuardianAltarDifficulty Difficulty, int MapId, int BasisLevel,
    ImmutableArray<GuardianAltarReward> Rewards);
public sealed record GuardianAltarSurvivalReward(int FirstRound, int LastRound, ImmutableArray<GuardianAltarReward> Rewards);
public sealed record GuardianAltarStage(int DungeonId, bool Survival,
    ImmutableArray<GuardianAltarStageMap> Maps, ImmutableArray<int> SurvivalMaps,
    ImmutableArray<GuardianAltarSurvivalReward> SurvivalRewards);
public sealed record GuardianAltarGageLevel(int Level, int Capacity, int Rate);
public sealed record GuardianAltarConfiguration(double UpgradeCostPercent, int DailyRefillTickets,
    ImmutableArray<GuardianAltarGageLevel> GageLevels);
public sealed record GuardianAltarSummon(int Id, string Team, string Rank, int MonsterCode,
    int MinimumLevel, int MaximumLevel, int X, int Y, int Z);
public sealed record GuardianAltarSpawnAction(int SummonId, int DelayMilliseconds, int IntervalMilliseconds, int CountLimit);
public sealed record GuardianAltarAction(int Id, string Kind, ImmutableArray<GuardianAltarSpawnAction> Spawns);
public sealed record GuardianAltarActionAssignment(int ActionId, int MinimumAliveMonsters, int MaximumAliveMonsters);
public sealed record GuardianAltarPhase(int DurationMilliseconds, string Kind,
    ImmutableArray<GuardianAltarActionAssignment> Assignments);
public sealed record GuardianAltarMap(int MapId, int CenterMonsterCode, ImmutableArray<GuardianAltarPhase> Phases);
public sealed record GuardianAltarShopLevel(int Level, int ResourceId, int Price, int Cooltime,
    int? NeedPoint, int? SetSlot, ImmutableDictionary<string, int> Attributes)
{
    public int SummonCooldownMilliseconds { get; init; }
}
public sealed record GuardianAltarShopItem(int RidableId, GuardianAltarShopKind Kind, int Id, int UnlockStageId,
    ImmutableArray<GuardianAltarShopLevel> Levels)
{
    public bool Settable { get; init; } = true;
}
public sealed record GuardianAltarAchievement(int Id, GuardianAltarDifficulty Difficulty, int ClearCount,
    ImmutableArray<GuardianAltarReward> Rewards);

/// <summary>Declared content only; presence here does not activate a native client entrance.</summary>
public sealed record GuardianAltarCatalog(
    ImmutableArray<GuardianAltarStage> Stages,
    GuardianAltarConfiguration Configuration,
    ImmutableDictionary<int, GuardianAltarMap> Maps,
    ImmutableDictionary<int, GuardianAltarSummon> Summons,
    ImmutableArray<GuardianAltarAction> Actions,
    ImmutableArray<GuardianAltarShopItem> Shop,
    ImmutableDictionary<int, int> StarResetGold,
    ImmutableArray<GuardianAltarAchievement> Achievements);

public interface IGuardianAltarContentSource
{
    GuardianAltarCatalog Catalog { get; }
}
