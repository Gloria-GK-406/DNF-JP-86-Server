namespace Dfo.Server.Abstractions.Pvp;

public enum PvpDailyMissionPool
{
    Casual,
    TotalMatch,
}

public enum PvpDailyMissionCondition
{
    Participate,
    Win,
    ColorlessSkills,
    ReduceHealth,
    RemainingHealth,
    DefeatJob,
    Knockouts,
    WinStreak,
    WinPose,
    ColosseumWin,
    TotalMatchSurvivors,
    TotalMatchSweep,
}

public sealed record PvpDailyMissionDefinition(
    ushort Id, PvpDailyMissionPool Pool, byte Group, int Weight,
    PvpDailyMissionCondition Condition, int Goal, int Parameter,
    int RewardItemId, int RewardCount);

public interface IPvpDailyMissionDefinitionSource
{
    IReadOnlyList<PvpDailyMissionDefinition> Load();
}
