using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class ExperienceCalculationDomainService(
    IExperienceValuationDomainService valuation, IExperienceBonusDomainService bonuses,
    Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService tower,
    Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService bossTower)
    : IExperienceCalculationDomainService
{
    public async ValueTask<ExperienceBreakdown> CalculateAsync(ExperienceCalculationContext context,
        byte characterLevel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var expectedEvent = context switch
        {
            KillExperienceContext => ExperienceEvent.MonsterKill,
            ClearExperienceContext => ExperienceEvent.DungeonClear,
            TowerKillExperienceContext => ExperienceEvent.TowerKill,
            TowerClearExperienceContext => ExperienceEvent.TowerClear,
            QuestExperienceContext => ExperienceEvent.QuestCompletion,
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        };
        if (context.Occasion.Event != expectedEvent)
            throw new ArgumentException("Experience occasion does not match its context.", nameof(context));
        switch (context)
        {
            case KillExperienceContext kill:
                var worth = valuation.WorthOfKill(new(kill.MonsterLevel, kill.Rank, characterLevel,
                    kill.Difficulty, kill.DungeonWeight, kill.PartyMemberCount, kill.IsNamedMonster));
                var result = new ExperienceBreakdown(Base: worth);
                if (worth == 0) return result;
                return ApplyDungeonBonuses(result, await bonuses.CollectAsync(kill.Occasion, cancellationToken)
                    .ConfigureAwait(false));
            case ClearExperienceContext clear:
                var calculated = valuation.WorthOfClear(clear.Clear with { CharacterLevel = characterLevel });
                var settlement = new ExperienceBreakdown(Base: calculated.Base, ClearRank: calculated.RankBonus);
                return ApplyDungeonBonuses(settlement, await bonuses.CollectAsync(clear.Occasion, cancellationToken)
                    .ConfigureAwait(false));
            case TowerKillExperienceContext { Tower: DungeonParticipationMode.BossTower } kill:
                var bossKillBase = await bossTower
                    .KillExperienceAsync(characterLevel, kill.ActorType, cancellationToken).ConfigureAwait(false);
                var bossKill = new ExperienceBreakdown(Base: bossKillBase);
                if (bossKillBase == 0) return bossKill;
                return ApplyBaseBonuses(bossKill, await bonuses.CollectAsync(kill.Occasion with
                    { Event = ExperienceEvent.MonsterKill, AllowGuildExperience = false }, cancellationToken).ConfigureAwait(false));
            case TowerClearExperienceContext { Tower: DungeonParticipationMode.BossTower } clear:
                var bossBase = await bossTower
                    .ClearExperienceAsync(characterLevel, clear.RewardLevel, cancellationToken).ConfigureAwait(false);
                var remaining = 1_100_000u - Math.Min(1_100_000u, bossBase);
                var bossResult = new ExperienceBreakdown(Base: Math.Min(1_100_000u, bossBase));
                foreach (var modifier in await bonuses.CollectAsync(clear.Occasion with { Event = ExperienceEvent.DungeonClear }, cancellationToken).ConfigureAwait(false))
                {
                    var amount = Math.Min(remaining, Floor(bossBase * modifier.Rate));
                    bossResult = bossResult.Add(modifier.Kind, amount);
                    remaining -= amount;
                }
                return bossResult;
            case TowerKillExperienceContext { Tower: DungeonParticipationMode.IllusionTower } kill:
                return new(Base: await tower
                    .KillExperienceAsync(characterLevel, kill.ActorType, cancellationToken, kill.Occasion.DungeonId).ConfigureAwait(false));
            case TowerClearExperienceContext { Tower: DungeonParticipationMode.IllusionTower } clear:
                return new(Base: await tower
                    .ClearExperienceAsync(characterLevel, clear.RewardLevel, cancellationToken, clear.Occasion.DungeonId).ConfigureAwait(false));
            case QuestExperienceContext quest:
                return new(Base: quest.Base);
            default:
                throw new ArgumentOutOfRangeException(nameof(context));
        }
    }

    private static ExperienceBreakdown ApplyDungeonBonuses(ExperienceBreakdown award, IReadOnlyList<ExperienceModifier> modifiers)
    {
        var burnRate = 0.0;
        foreach (var modifier in modifiers)
        {
            if (modifier.Kind == ExperienceBonusKind.FatigueBurn)
            {
                burnRate += modifier.Rate;
                continue;
            }
            // Black diamond includes rank. Other bonuses use the base before any bonus.
            var basis = modifier.Kind == ExperienceBonusKind.BlackDiamond
                ? (double)award.Base + award.ClearRank : award.Base;
            award = award.Add(modifier.Kind, Floor(basis * modifier.Rate));
        }
        if (burnRate > 0)
            award = award.Add(ExperienceBonusKind.FatigueBurn, Floor(award.Total * burnRate));
        _ = award.Total;
        return award;
    }

    internal static ExperienceBreakdown ApplyBaseBonuses(ExperienceBreakdown award, IEnumerable<ExperienceModifier> modifiers)
    {
        foreach (var modifier in modifiers)
            award = award.Add(modifier.Kind, Floor(award.Base * modifier.Rate));
        _ = award.Total;
        return award;
    }

    private static uint Floor(double value) => value <= 0 || double.IsNaN(value) ? 0
        : checked((uint)Math.Floor(value));
}
