using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Quests;

internal sealed class MirrorAradQuestRuleService(IQuestContentRuleService quests,
    IMirrorDungeonRuleService mirror, IItemPlacementRuleService placements, IQuestMapClearRuleService mapClears) : IMirrorAradQuestRuleService
{
    public MirrorDungeonCatalog Catalog => mirror.Catalog;

    public async ValueTask<MirrorQuestObjective?> ResolveAsync(int dungeonId, int questId, CancellationToken ct)
    {
        if (!Catalog.Candidates.Contains(new(dungeonId, questId))) return null;
        var q = await quests.FindAsync(questId, ct).ConfigureAwait(false);
        if (q?.Grade != QuestGrade.Crack || !q.Rewards.Any(x => x.Kind == QuestRewardKind.CrackOfDimension)) return null;
        var a = q.ObjectiveArguments;
        var type = q.ObjectiveTypeCode.Trim().Trim('[', ']').ToLowerInvariant();
        if (type is "hunt monster" or "hunt enemy")
        {
            var stride = type == "hunt enemy" ? 5 : 4;
            if (a.Count == 0 || a.Count % stride != 0 || a.Count / stride > 3) return null;
            var targets = new List<MirrorHuntTarget>();
            for (var i = 0; i < a.Count; i += stride)
            {
                var count = a[i + stride - 1];
                var actor = a[i + 2];
                var enemy = stride == 5 ? a[i + 3] : HuntEnemyTargetTypes.Monster;
                if ((a[i] != -1 && a[i] != dungeonId) || a[i + 1] < -1 || count is <= 0 or > 511
                    || (actor <= 0 && actor is not (-3 or -5 or -11)) || enemy is < 0 or > 3) return null;
                targets.Add(new(a[i], a[i + 1], actor, enemy, count, i / stride));
            }
            return new(q, MirrorQuestKind.Hunt, targets);
        }
        if (type == "clear map")
            return (mapClears).TargetMapOf(q) is { } mapId
                ? new(q, MirrorQuestKind.ClearMap, [], MapId: mapId) : null;
        if (type != "condition under clear" || a.Count < 2 || a[0] != dungeonId || a[1] < -1) return null;
        return q.ObjectiveSubType switch
        {
            4 or 6 when a.Count == 2 => new(q, MirrorQuestKind.Clear, [], a[1], RequireNoRevive: q.ObjectiveSubType == 4),
            0 when a.Count == 3 && a[2] > 0 => new(q, MirrorQuestKind.TimedClear, [], a[1], TimeLimitSeconds: a[2]),
            11 when a.Count == 3 && a[2] is > 0 and <= 511 => new(q, MirrorQuestKind.Locations, [], a[1], LocationCount: a[2]),
            _ => null,
        };
    }

    public MirrorQuestState Apply(MirrorQuestObjective objective, MirrorQuestState state, DungeonRunState run, QuestProgressFact fact)
    {
        if (state.RewardClaimed || state.GivenUp) return state;
        if (fact is QuestCharacterRevived revive && revive.RunId == run.RunId && revive.CharacterId == state.CharacterId)
            state = state with { ReviveUsed = true };
        if (objective.Kind == MirrorQuestKind.Hunt && fact is QuestEnemyDefeated death
            && death.RunId == run.RunId && death.DungeonId == run.Layout.DungeonId && death.Actor is { } actor
            && actor.Cell == run.CurrentCell && !state.ObservedDeaths.Contains(actor))
        {
            var declared = run.CurrentCellState.FindActor(actor.Sequence);
            if (declared is null || declared.Actor.Code != death.EnemyCode) return state;
            var trigger = new QuestTrigger(state.TriggerValue);
            foreach (var target in objective.Hunt)
            {
                var type = declared.Actor.ActorType;
                var matchesActor = target.ActorSelector > 0 ? target.ActorSelector == death.EnemyCode
                    : target.ActorSelector switch
                    {
                        -5 => declared.Actor.IsHostile && declared.BlocksClear,
                        -3 => type is 3 or 8,
                        -11 => type is 1 or 2 or 3 or 6 or 7 or 8,
                        _ => false,
                    };
                if (matchesActor && (target.EnemyType == 0 || target.EnemyType == death.EnemyType)
                    && (target.Difficulty < 0 || (int)death.Difficulty >= target.Difficulty))
                    trigger = trigger.ReplaceChannel(target.Channel, trigger.GetChannel(target.Channel) - 1L);
            }
            state = state with { TriggerValue = trigger.PackedValue, ObservedDeaths = [.. state.ObservedDeaths, actor] };
        }
        if (fact is QuestDungeonCleared clear && clear.RunId == run.RunId && CanFinish(objective, state, run))
            state = state with { TriggerValue = 0 };
        return state;
    }

    public MirrorQuestState ApplyMapClear(MirrorQuestObjective objective, MirrorQuestState state, DungeonMapCleared clear)
    {
        if (state.GivenUp || state.RewardClaimed || state.RunId != clear.RunId || state.AcceptedAt > clear.ClearedAt
            || (objective.MinimumDifficulty >= 0 && (int)clear.Difficulty < objective.MinimumDifficulty)
            || state.ClearedCells.Contains(clear.Cell)) return state;
        var updated = state with { ClearedCells = [.. state.ClearedCells, clear.Cell],
            TargetMapCleared = state.TargetMapCleared || clear.MapId == objective.MapId };
        return objective.Kind switch
        {
            MirrorQuestKind.Locations => updated with { TriggerValue = (uint)Math.Max(0, objective.LocationCount - updated.ClearedCells.Count) },
            MirrorQuestKind.ClearMap when updated.TargetMapCleared => updated with { TriggerValue = 0 },
            _ => updated,
        };
    }

    public bool CanFinish(MirrorQuestObjective objective, MirrorQuestState state, DungeonRunState run)
    {
        if (!run.ClearOpen || run.ClearedAt is not { } cleared || state.GivenUp
            || (objective.MinimumDifficulty >= 0 && (int)run.Difficulty < objective.MinimumDifficulty)) return false;
        return objective.Kind switch
        {
            MirrorQuestKind.Hunt => state.TriggerValue == 0,
            MirrorQuestKind.Clear => !objective.RequireNoRevive || !state.ReviveUsed,
            MirrorQuestKind.ClearMap => state.TargetMapCleared,
            MirrorQuestKind.Locations => state.ClearedCells.Concat(run.ClearedCells).Distinct().Count() >= objective.LocationCount,
            MirrorQuestKind.TimedClear => cleared >= state.AcceptedAt && cleared - state.AcceptedAt <= TimeSpan.FromSeconds(objective.TimeLimitSeconds),
            _ => false,
        };
    }

    public async ValueTask<ImmutableArray<DomainEffect>?> RewardEffectsAsync(byte level, MirrorDailyQuota quota, CancellationToken ct)
    {
        if (Catalog.DailyClearLimit > 0 && quota.Clears >= Catalog.DailyClearLimit) return null;
        var band = Catalog.Rewards.FirstOrDefault(x => level >= x.MinimumLevel && level <= x.MaximumLevel);
        if (band is null || band.Items.Count == 0) return null;
        var items = band.Items.ToList();
        var daily = Catalog.DailyReward;
        if (!quota.DailyRewardClaimed && daily.RequiredClears > 0 && quota.Clears + 1 >= daily.RequiredClears)
            items.Add(new(daily.ItemId, daily.Count));
        var effects = ImmutableArray.CreateBuilder<DomainEffect>();
        foreach (var item in items)
        {
            var placement = await placements.PlacementForAsync(item.ItemId, ct).ConfigureAwait(false);
            if (item.ItemId <= 0 || item.Count <= 0 || placement is null) return null;
            effects.Add(new GrantBasicCharacterItemEffect(item.ItemId, item.Count, placement));
        }
        return effects.ToImmutable();
    }
}
