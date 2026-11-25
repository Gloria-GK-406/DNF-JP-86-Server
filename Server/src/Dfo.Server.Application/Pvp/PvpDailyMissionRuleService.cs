using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

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

    public int TargetFor(PvpDailyMissionDefinition mission) =>
        mission.Condition == PvpDailyMissionCondition.WinStreak && mission.Parameter > 1 ? mission.Parameter : mission.Goal;

    public bool DisplaysCurrentWinRun(PvpDailyMissionDefinition mission, int progress) =>
        mission.Condition == PvpDailyMissionCondition.WinStreak && mission.Parameter == 1 && progress < TargetFor(mission);

    public (int Amount, MetricUpdate? Metric) ProgressFor(PvpDailyMissionDefinition mission, int before,
        PvpMatchConclusion battle, PvpFighter fighter, bool won, bool colosseumWin,
        IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>? profiles)
    {
        if (before >= TargetFor(mission) || fighter.Fled && mission.Condition != PvpDailyMissionCondition.WinStreak)
            return default;

        return mission.Condition switch
        {
            PvpDailyMissionCondition.Participate => (1, null),
            PvpDailyMissionCondition.Win when won => (1, null),
            PvpDailyMissionCondition.ColosseumWin when won && colosseumWin => (1, null),
            PvpDailyMissionCondition.DefeatJob when won && profiles is not null
                && battle.Fighters.Any(opponent => opponent.Team != fighter.Team && !opponent.Fled
                    && profiles.TryGetValue(opponent.CharacterId, out var profile) && profile.Job == mission.Parameter) => (1, null),
            PvpDailyMissionCondition.Knockouts => (fighter.Kills, null),
            PvpDailyMissionCondition.ColorlessSkills => (battle.DailyColorlessSkills.GetValueOrDefault(fighter.Seat), null),
            PvpDailyMissionCondition.ReduceHealth or PvpDailyMissionCondition.RemainingHealth
                when (won || mission.Condition == PvpDailyMissionCondition.ReduceHealth)
                    && battle.DailyHealthReports.TryGetValue(fighter.Seat, out var reports)
                    => (reports.GetValueOrDefault(mission.Id), null),
            PvpDailyMissionCondition.TotalMatchSurvivors when won && battle.Fighters.All(opponent => !opponent.Fled)
                && PvpTotalMatchTeam.MemberCount - battle.TotalMatchAppearances.Count(role => role.Seat == fighter.Seat && role.Deaths > 0)
                    >= mission.Parameter => (1, null),
            PvpDailyMissionCondition.TotalMatchSweep when won
                && battle.TotalMatchAppearances.Any(role => role.Seat == fighter.Seat && role.Kills >= PvpTotalMatchTeam.MemberCount)
                => (1, null),
            PvpDailyMissionCondition.WinStreak => (0, new(0, won, ReadAllBuckets: false)),
            PvpDailyMissionCondition.WinPose when won && battle.DailyWinPoses.TryGetValue(fighter.Seat, out var pose)
                && pose < mission.Parameter => (0, new(pose, Increment: true, ReadAllBuckets: true)),
            _ => default,
        };
    }

    public int ProgressFromMetric(PvpDailyMissionDefinition mission, int before, int count,
        IReadOnlyDictionary<int, int>? buckets)
    {
        if (buckets is not null)
            return Enumerable.Range(0, mission.Parameter).Min(index => buckets.GetValueOrDefault(index)) - before;
        if (mission.Parameter == 1) return count - before;
        return count > 0 && count % mission.Goal == 0 ? 1 : 0;
    }

    internal readonly record struct MetricUpdate(int Bucket, bool Increment, bool ReadAllBuckets);

    private static bool CanAssign(PvpDailyMissionDefinition mission) => mission.Condition switch
    {
        PvpDailyMissionCondition.ColosseumWin => true,
        PvpDailyMissionCondition.WinStreak or PvpDailyMissionCondition.WinPose => mission.RewardItemId == VictoryEmblemResource.ItemId,
        PvpDailyMissionCondition.Participate or PvpDailyMissionCondition.Win or PvpDailyMissionCondition.Knockouts
            or PvpDailyMissionCondition.DefeatJob or PvpDailyMissionCondition.ColorlessSkills
            or PvpDailyMissionCondition.ReduceHealth or PvpDailyMissionCondition.RemainingHealth
            or PvpDailyMissionCondition.TotalMatchSurvivors or PvpDailyMissionCondition.TotalMatchSweep
            => mission.RewardItemId == VictoryEmblemResource.ItemId,
        _ => false,
    };
}
