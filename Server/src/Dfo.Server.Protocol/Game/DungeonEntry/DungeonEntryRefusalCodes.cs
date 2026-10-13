using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Protocol.Game.DungeonEntry;

/// <summary>Only quota failures reach the native quota message; domain reasons remain distinct.</summary>
internal static class DungeonEntryRefusalCodes
{
    // CMD 02B6 has its own error switch. In particular 0x16 reads a member index;
    // it does not mean ordinary-entry fatigue failure. Never send 0x12 (auto retry).
    public static byte ForMirror(DungeonEntryFailure failure) => failure switch
    {
        DungeonEntryFailure.AlreadyInDungeon or DungeonEntryFailure.MissingPrerequisite
            or DungeonEntryFailure.HellQuestNotCleared => 0x13,
        _ => 0x01,
    };

    public static byte For(DungeonEntryFailure failure, bool manualHell) => failure switch
    {
        DungeonEntryFailure.DungeonNotFound or DungeonEntryFailure.DifficultyNotUnlocked => SelectDungeonRefusal.InvalidDungeonOrDifficulty,
        DungeonEntryFailure.MissingRequiredItem => SelectDungeonRefusal.MissingRequiredItem,
        DungeonEntryFailure.MissingHellTicket => SelectDungeonRefusal.MissingHellTicket,
        DungeonEntryFailure.LevelTooLow => manualHell ? SelectDungeonRefusal.HellLevelTooLow : SelectDungeonRefusal.LevelTooLow,
        DungeonEntryFailure.HellQuestNotCleared or DungeonEntryFailure.MissingPrerequisite => SelectDungeonRefusal.MissingPrerequisite,
        DungeonEntryFailure.InsufficientFatigue => SelectDungeonRefusal.InsufficientFatigue,
        // A21 switch D0B68C / D0B948; the latter differs from the 70 server's F4.
        DungeonEntryFailure.InsufficientGold => 0x0A,
        DungeonEntryFailure.OutsideOpeningHours => 0xF5,
        DungeonEntryFailure.DailyEntryLimitReached => SelectDungeonRefusal.DailyEntryLimitReached,
        DungeonEntryFailure.MemberEntryLimitReached => SelectDungeonRefusal.MemberEntryLimitReached,
        DungeonEntryFailure.AlreadyInDungeon => SelectDungeonRefusal.InvalidSelectionState,
        DungeonEntryFailure.ConfigurationInvalid or DungeonEntryFailure.HellUnavailable or DungeonEntryFailure.HellConfigurationInvalid => SelectDungeonRefusal.DungeonUnavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unmapped dungeon entry failure."),
    };
}
