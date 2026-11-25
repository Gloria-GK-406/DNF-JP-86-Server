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
    ICharacterIdentityQueryDomainService identities,
    PvpMatchmakingRuleService matchmakingRules) : IPvpDailyMissionQueryDomainService
{
    private readonly PvpVictoryEmblems emblems = new(transactions, inventory);

    public async ValueTask<IReadOnlyList<PvpDailyMission>> CurrentAsync(SelectedSessionIdentity session, ChannelEntryIdentity channel,
        CancellationToken cancellationToken)
    {
        if (session.ListenerPort != channel.ListenerPort || missionRules.PoolOf(channel) is not { } pool) return [];
        var day = rules.GameDayOf(clock.GetUtcNow());
        var progress = await missions.ReadDayAsync(session.CharacterId, day, cancellationToken).ConfigureAwait(false);
        var result = new List<PvpDailyMission>();
        foreach (var mission in missionRules.Assigned(session.CharacterId, day, pool, progress))
        {
            var goal = missionRules.TargetFor(mission);
            var value = progress.GetValueOrDefault(mission.Id);
            if (missionRules.DisplaysCurrentWinRun(mission, value))
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
        if (missionRules.PoolOf(battle.Channel) is not { } pool || (pool == PvpDailyMissionPool.TotalMatch
                ? !matchmakingRules.AppliesToTotalMatch(battle) : !matchmakingRules.AppliesTo(battle)))
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
                var (amount, metric) = missionRules.ProgressFor(mission, before, battle, fighter, won,
                    colosseumWinners?.Contains(fighter.Seat) == true, profiles);
                if (metric is { } update)
                {
                    var count = await missions.RecordMetricAsync(character, day, mission.Id, update.Bucket, update.Increment,
                        cancellationToken).ConfigureAwait(false);
                    var buckets = update.ReadAllBuckets
                        ? await missions.ReadMetricsAsync(character, day, mission.Id, cancellationToken).ConfigureAwait(false)
                        : null;
                    amount = missionRules.ProgressFromMetric(mission, before, count, buckets);
                }
                if (amount > 0 && await missions.AdvanceAsync(character, day, mission.Id, missionRules.TargetFor(mission), battle.MatchId,
                        cancellationToken, amount).ConfigureAwait(false))
                    reward = checked(reward + mission.RewardCount);
            }
            if (reward > 0)
                awarded.Add(character, (await emblems.ChangeAsync(character, reward, cancellationToken).ConfigureAwait(false))!.Value);
        }
        return awarded;
    }
}
