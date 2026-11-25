using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

/// <summary>Daily assignment and rewards shared by the two duel business services.</summary>
internal sealed class PvpDailyMissions(
    PvpDailyMissionRuleService missionRules,
    IPvpDailyMissionStore missions,
    IPvpSettlementRuleService rules,
    TimeProvider clock,
    IDomainTransactionAccessor transactions,
    IItemDomainStore inventory,
    ICharacterIdentityQueryDomainService identities) : IPvpDailyMissionQueryDomainService
{

    private readonly PvpVictoryEmblems emblems = new(transactions, inventory);

    public async ValueTask<IReadOnlyList<PvpDailyMission>> CurrentAsync(SelectedSessionIdentity session, ChannelEntryIdentity channel,
        CancellationToken cancellationToken)
    {
        if (session.ListenerPort != channel.ListenerPort || PoolOf(channel) is not { } pool) return [];
        var day = rules.GameDayOf(clock.GetUtcNow());
        var progress = await missions.ReadDayAsync(session.CharacterId, day, cancellationToken).ConfigureAwait(false);
        var result = new List<PvpDailyMission>();
        foreach (var mission in missionRules.Assigned(session.CharacterId, day, pool, progress))
        {
            var goal = TargetFor(mission);
            var value = progress.GetValueOrDefault(mission.Id);
            if (mission.Condition == PvpDailyMissionCondition.WinStreak && mission.Parameter == 1 && value < goal)
            {
                var metrics = await missions.ReadMetricsAsync(session.CharacterId, day, mission.Id, cancellationToken)
                    .ConfigureAwait(false);
                value = metrics.GetValueOrDefault(0);
            }
            result.Add(new(mission.Id, Math.Min(goal, value), goal));
        }
        return result;
    }

    /// <summary>Runs within the first successful write of the match journal.</summary>
    public async ValueTask<IReadOnlyDictionary<CharacterId, int>> AdvanceAsync(PvpMatchConclusion battle, DateOnly day,
        CancellationToken cancellationToken, IReadOnlySet<int>? colosseumWinners = null)
    {
        if (PoolOf(battle.Channel) is not { } pool || (pool == PvpDailyMissionPool.TotalMatch
                ? !PvpRatingRules.AppliesToTotalMatch(battle) : !PvpRatingRules.AppliesTo(battle)))
            return ImmutableDictionary<CharacterId, int>.Empty;

        var awarded = new Dictionary<CharacterId, int>();
        var profiles = pool == PvpDailyMissionPool.Casual
            ? await identities.ReadAsync(battle.Fighters.Select(fighter => fighter.CharacterId).ToArray(), cancellationToken)
                .ConfigureAwait(false)
            : null;
        foreach (var fighter in battle.Fighters)
        {
            var character = fighter.CharacterId;
            var progress = await missions.ReadDayAsync(character, day, cancellationToken).ConfigureAwait(false);
            var won = !fighter.Fled && PvpSettlementDomainService.OutcomeOf(battle, fighter) == PvpOutcome.Win;
            var reward = 0;
            foreach (var mission in missionRules.Assigned(character, day, pool, progress))
            {
                var before = progress.GetValueOrDefault(mission.Id);
                var goal = TargetFor(mission);
                if (before >= goal || fighter.Fled && mission.Condition != PvpDailyMissionCondition.WinStreak) continue;
                var amount = mission.Condition switch
                {
                    PvpDailyMissionCondition.Participate => 1,
                    PvpDailyMissionCondition.Win when won => 1,
                    PvpDailyMissionCondition.ColosseumWin when won && colosseumWinners?.Contains(fighter.Seat) == true => 1,
                    PvpDailyMissionCondition.DefeatJob when won && profiles is not null
                        && battle.Fighters.Any(opponent => opponent.Team != fighter.Team && !opponent.Fled
                            && profiles.TryGetValue(opponent.CharacterId, out var profile) && profile.Job == mission.Parameter) => 1,
                    PvpDailyMissionCondition.Knockouts => fighter.Kills,
                    PvpDailyMissionCondition.ColorlessSkills => battle.DailyColorlessSkills.GetValueOrDefault(fighter.Seat),
                    PvpDailyMissionCondition.ReduceHealth or PvpDailyMissionCondition.RemainingHealth
                        when (won || mission.Condition == PvpDailyMissionCondition.ReduceHealth)
                            && battle.DailyHealthReports.TryGetValue(fighter.Seat, out var reports)
                            => reports.GetValueOrDefault(mission.Id),
                    PvpDailyMissionCondition.TotalMatchSurvivors when won && battle.Fighters.All(opponent => !opponent.Fled)
                        && 3 - battle.TotalMatchAppearances.Count(role => role.Seat == fighter.Seat && role.Deaths > 0) >= mission.Parameter => 1,
                    PvpDailyMissionCondition.TotalMatchSweep when won
                        && battle.TotalMatchAppearances.Any(role => role.Seat == fighter.Seat && role.Kills >= 3) => 1,
                    PvpDailyMissionCondition.WinStreak => await RecordStreakAsync(character, day, mission, won, before,
                        cancellationToken).ConfigureAwait(false),
                    PvpDailyMissionCondition.WinPose when won && battle.DailyWinPoses.TryGetValue(fighter.Seat, out var pose)
                        => await RecordPoseAsync(character, day, mission, pose, before, cancellationToken).ConfigureAwait(false),
                    _ => 0,
                };
                if (amount > 0 && await missions.AdvanceAsync(character, day, mission.Id, goal, battle.MatchId,
                        cancellationToken, amount).ConfigureAwait(false))
                    reward = checked(reward + mission.RewardCount);
            }
            if (reward > 0)
                awarded.Add(character, (await emblems.ChangeAsync(character, reward, cancellationToken).ConfigureAwait(false))!.Value);
        }
        return awarded;
    }

    private static int TargetFor(PvpDailyMissionDefinition mission) =>
        mission.Condition == PvpDailyMissionCondition.WinStreak && mission.Parameter > 1 ? mission.Parameter : mission.Goal;

    private async ValueTask<int> RecordStreakAsync(CharacterId character, DateOnly day, PvpDailyMissionDefinition mission,
        bool won, int before, CancellationToken cancellationToken)
    {
        var run = await missions.RecordMetricAsync(character, day, mission.Id, 0, won, cancellationToken).ConfigureAwait(false);
        return mission.Parameter == 1 ? run - before : run > 0 && run % mission.Goal == 0 ? 1 : 0;
    }

    private async ValueTask<int> RecordPoseAsync(CharacterId character, DateOnly day, PvpDailyMissionDefinition mission,
        byte pose, int before, CancellationToken cancellationToken)
    {
        if (pose >= mission.Parameter) return 0;
        await missions.RecordMetricAsync(character, day, mission.Id, pose, true, cancellationToken).ConfigureAwait(false);
        var counts = await missions.ReadMetricsAsync(character, day, mission.Id, cancellationToken).ConfigureAwait(false);
        return Enumerable.Range(0, mission.Parameter).Min(index => counts.GetValueOrDefault(index)) - before;
    }

    private static PvpDailyMissionPool? PoolOf(ChannelEntryIdentity channel) => channel.Kind switch
    {
        ChannelEntryKind.MatchmakingDuel => PvpDailyMissionPool.Casual,
        ChannelEntryKind.TotalMatchDuel => PvpDailyMissionPool.TotalMatch,
        _ => null,
    };
}
