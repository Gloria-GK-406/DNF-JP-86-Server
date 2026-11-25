using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpDailyMissionRuleService(IPvpDailyMissionDefinitionSource definitions)
{
    public IEnumerable<PvpDailyMissionDefinition> Assigned(CharacterId character, DateOnly day, PvpDailyMissionPool pool,
        IReadOnlyDictionary<ushort, int> progress)
    {
        foreach (var group in definitions.Load().Where(mission => mission.Pool == pool && CanAssign(mission))
                     .GroupBy(mission => mission.Group).OrderBy(group => group.Key))
        {
            var pinned = group.FirstOrDefault(mission => progress.ContainsKey(mission.Id));
            if (pinned is not null) { yield return pinned; continue; }
            var seed = Encoding.UTF8.GetBytes(FormattableString.Invariant($"{character.Value}/{day.DayNumber}/{pool}/{group.Key}"));
            var selected = BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(seed)) % checked((uint)group.Sum(mission => mission.Weight));
            foreach (var mission in group.OrderBy(mission => mission.Id))
            {
                if (selected < mission.Weight) { yield return mission; break; }
                selected -= checked((uint)mission.Weight);
            }
        }
    }

    public PvpDailyMissionPool? PoolOf(ChannelEntryIdentity channel) => channel.Kind switch
    {
        ChannelEntryKind.MatchmakingDuel => PvpDailyMissionPool.Casual,
        ChannelEntryKind.TotalMatchDuel => PvpDailyMissionPool.TotalMatch,
        _ => null,
    };

    public IEnumerable<(ushort MissionId, int Limit)> HealthReportLimits(ChannelEntryIdentity channel,
        PvpBattleMode mode, int opposingFighters, IReadOnlyList<ushort> missionIds)
    {
        var pool = mode == PvpBattleMode.TotalMatch ? PvpDailyMissionPool.TotalMatch : PvpDailyMissionPool.Casual;
        if (PoolOf(channel) != pool) yield break;
        var missions = definitions.Load();
        var opponents = mode == PvpBattleMode.TotalMatch ? PvpTotalMatchTeam.MemberCount : opposingFighters;
        foreach (var missionId in missionIds.Distinct())
        {
            var mission = missions.FirstOrDefault(value => value.Id == missionId && value.Pool == pool);
            var limit = mission?.Condition switch
            {
                PvpDailyMissionCondition.ReduceHealth => opponents,
                PvpDailyMissionCondition.RemainingHealth => 1,
                _ => 0,
            };
            if (limit > 0) yield return (missionId, limit);
        }
    }

    public bool CanReportWinPose(ChannelEntryIdentity channel, ushort missionId, byte ordinal) =>
        PoolOf(channel) == PvpDailyMissionPool.Casual && definitions.Load().Any(mission =>
            mission.Id == missionId && mission.Pool == PvpDailyMissionPool.Casual
            && mission.Condition == PvpDailyMissionCondition.WinPose && ordinal < mission.Parameter);

    private static bool CanAssign(PvpDailyMissionDefinition mission) => mission.Condition switch
    {
        PvpDailyMissionCondition.ColosseumWin => true,
        PvpDailyMissionCondition.WinStreak or PvpDailyMissionCondition.WinPose => mission.RewardItemId == PvpVictoryEmblems.ItemId,
        PvpDailyMissionCondition.Participate or PvpDailyMissionCondition.Win or PvpDailyMissionCondition.Knockouts
            or PvpDailyMissionCondition.DefeatJob or PvpDailyMissionCondition.ColorlessSkills
            or PvpDailyMissionCondition.ReduceHealth or PvpDailyMissionCondition.RemainingHealth
            or PvpDailyMissionCondition.TotalMatchSurvivors or PvpDailyMissionCondition.TotalMatchSweep
            => mission.RewardItemId == PvpVictoryEmblems.ItemId,
        _ => false,
    };
}
