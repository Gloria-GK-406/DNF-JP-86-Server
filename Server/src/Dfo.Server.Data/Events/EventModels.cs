using System.Text.Json.Serialization;
namespace Dfo.Server.Data.Events;

public sealed record AttendanceReward(int AttendanceNo, int ItemId, int Count);
public sealed record AttendanceRewardView(int AttendanceNo, int ItemId, string? ItemName, int Count);
public sealed record WeeklyAttendanceDefinition(string SourcePath, int RequiredClears, int BonusClears, int TotalWeeks,
    AttendanceReward[] WeeklyRewards, AttendanceReward[] CumulativeRewards);
public sealed record EventConfiguration(long Revision, string SeasonId, bool Enabled, string Title, string ShortName,
    string Description, string StartNotice, string EndNotice, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    DateTimeOffset? UpdatedAt = null, string? UpdatedBy = null, string? RuleDefinition = null);
public sealed record ManagedEventEntry(int EventId, string Type, long Revision, string SeasonId, bool Enabled, string Status,
    string Title, string ShortName, string Description, string StartNotice, string EndNotice,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool Available, string? UnavailableReason,
    DateTimeOffset? UpdatedAt, string? UpdatedBy);
public sealed record ManagedEventList(ManagedEventEntry[] Entries);
public sealed record WeeklyAttendanceSettings(int EventId, long Revision, string SeasonId, string SourcePath,
    bool Available, string? UnavailableReason, int? RequiredClears, int? TotalWeeks,
    AttendanceRewardView[] WeeklyRewards, AttendanceRewardView[] CumulativeRewards);
public sealed record EventChange([property: JsonRequired] int EventId, [property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] string Action, string? Title = null, string? ShortName = null, string? Description = null,
    string? StartNotice = null, string? EndNotice = null, DateTimeOffset? StartsAt = null, DateTimeOffset? EndsAt = null);
public sealed record EventInfo(int EventId, string Title, string ShortName, string Description, string StartNotice,
    string EndNotice, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string LinkKey)
{
    public bool HasDetail { get; init; } = true;
}
public sealed record AttendanceProgress(int Count, int Clears, bool Claimed, int[] ClaimedAttendance);
public sealed record WeeklyAttendanceState(bool Active, int AttendanceCount, int RecommendedClears, int CurrentAttendanceNo,
    bool CanClaim, int[] ClaimedAttendance)
{
    public static WeeklyAttendanceState Empty { get; } = new(false, 0, 0, 0, false, []);
}
public sealed record EventPlayerSnapshot(EventInfo[] Information, WeeklyAttendanceState Attendance, JoustState? Joust = null)
{
    public ScheduledDungeonState[] ScheduledDungeons { get; init; } = [];
    public ScheduledQuestState[] ScheduledQuests { get; init; } = [];
    public ScheduledActivityState[] ScheduledActivities { get; init; } = [];
    public static EventPlayerSnapshot Empty { get; } = new([], WeeklyAttendanceState.Empty);
}
public sealed record ScheduledDungeonState(int DungeonId, bool Open);
public sealed record ScheduledActivityState(int Category, int ContentId, bool Open);
public sealed record ScheduledQuestState(int QuestId, bool Open);
public sealed record AttendanceClaimResult(bool Succeeded, WeeklyAttendanceState State);
