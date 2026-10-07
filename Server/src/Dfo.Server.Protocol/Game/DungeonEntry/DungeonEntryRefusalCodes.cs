using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Protocol.Game.DungeonEntry;

/// <summary>Only quota failures reach the native quota message; domain reasons remain distinct.</summary>
internal static class DungeonEntryRefusalCodes
{
    public static byte For(DungeonEntryFailure failure, bool manualHell) => failure switch
    {
        DungeonEntryFailure.DungeonNotFound or DungeonEntryFailure.DifficultyNotUnlocked => SelectDungeonRefusal.InvalidDungeonOrDifficulty,
        DungeonEntryFailure.MissingRequiredItem => SelectDungeonRefusal.MissingRequiredItem,
        DungeonEntryFailure.MissingHellTicket => SelectDungeonRefusal.MissingHellTicket,
        DungeonEntryFailure.LevelTooLow => manualHell ? SelectDungeonRefusal.HellLevelTooLow : SelectDungeonRefusal.LevelTooLow,
        DungeonEntryFailure.HellQuestNotCleared or DungeonEntryFailure.MissingPrerequisite => SelectDungeonRefusal.MissingPrerequisite,
        DungeonEntryFailure.InsufficientFatigue => SelectDungeonRefusal.InsufficientFatigue,
        DungeonEntryFailure.DailyEntryLimitReached => SelectDungeonRefusal.DailyEntryLimitReached,
        DungeonEntryFailure.MemberEntryLimitReached => SelectDungeonRefusal.MemberEntryLimitReached,
        DungeonEntryFailure.AlreadyInDungeon => SelectDungeonRefusal.InvalidSelectionState,
        DungeonEntryFailure.HellUnavailable or DungeonEntryFailure.HellConfigurationInvalid => SelectDungeonRefusal.DungeonUnavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unmapped dungeon entry failure."),
    };
}
