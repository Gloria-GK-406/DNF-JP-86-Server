using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Tests.Pvp;

[Trait("Category", "Unit")]
public sealed class PvpDailyMissionRuleServiceTests
{
    private static readonly ChannelEntryIdentity Ordinary = new(60, 10060, ChannelEntryKind.MatchmakingDuel);
    private static readonly ChannelEntryIdentity Peak = new(81, 10081, ChannelEntryKind.TotalMatchDuel);

    private static readonly PvpDailyMissionRuleService Rules = new(new Definitions(
    [
        new(201, PvpDailyMissionPool.Casual, 0, 1, PvpDailyMissionCondition.ReduceHealth, 5, 30, 2, 40),
        new(202, PvpDailyMissionPool.Casual, 1, 1, PvpDailyMissionCondition.RemainingHealth, 3, 50, 2, 60),
        new(203, PvpDailyMissionPool.Casual, 2, 1, PvpDailyMissionCondition.WinPose, 2, 4, 2, 100),
        new(204, PvpDailyMissionPool.TotalMatch, 0, 1, PvpDailyMissionCondition.ReduceHealth, 8, 30, 2, 80),
    ]));

    [Fact]
    public void Health_reports_match_the_mission_pool_and_condition_with_one_entry_per_id()
    {
        Assert.Equal(new (ushort, int)[] { (201, 3), (202, 1) },
            Rules.HealthReportLimits(Ordinary, PvpBattleMode.Relay, 3, [201, 202, 201, 203, 204]));
        Assert.Equal(new (ushort, int)[] { (204, 3) },
            Rules.HealthReportLimits(Peak, PvpBattleMode.TotalMatch, 1, [201, 204]));
    }

    [Fact]
    public void The_win_pose_limit_comes_from_the_mission_parameter()
    {
        Assert.True(Rules.CanReportWinPose(Ordinary, 203, 3));
        Assert.False(Rules.CanReportWinPose(Ordinary, 203, 4));
        Assert.False(Rules.CanReportWinPose(Ordinary, 201, 0));
        Assert.False(Rules.CanReportWinPose(Peak, 203, 0));
    }

    [Fact]
    public void Streak_blocks_and_pose_sets_advance_at_their_content_defined_boundaries()
    {
        var streak = new PvpDailyMissionDefinition(205, PvpDailyMissionPool.Casual, 3, 1,
            PvpDailyMissionCondition.WinStreak, 3, 2, 2, 100);
        var pose = new PvpDailyMissionDefinition(206, PvpDailyMissionPool.Casual, 3, 1,
            PvpDailyMissionCondition.WinPose, 2, 4, 2, 100);

        Assert.Equal(2, Rules.TargetFor(streak));
        Assert.Equal(0, Rules.ProgressFromMetric(streak, before: 1, count: 5, buckets: null));
        Assert.Equal(1, Rules.ProgressFromMetric(streak, before: 1, count: 6, buckets: null));
        Assert.Equal(0, Rules.ProgressFromMetric(pose, before: 1, count: 2,
            new Dictionary<int, int> { [0] = 2, [1] = 2, [2] = 2, [3] = 1 }));
        Assert.Equal(1, Rules.ProgressFromMetric(pose, before: 1, count: 2,
            new Dictionary<int, int> { [0] = 2, [1] = 2, [2] = 2, [3] = 2 }));
    }

    private sealed record Definitions(IReadOnlyList<PvpDailyMissionDefinition> Missions) : IPvpDailyMissionDefinitionSource
    {
        public IReadOnlyList<PvpDailyMissionDefinition> Load() => Missions;
    }
}
