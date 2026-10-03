using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class ExperienceCalculationDomainService(
    IExperienceValuationDomainService valuation, IExperienceBonusDomainService bonuses,
    Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRewardRuleService? tower = null)
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
                    kill.Difficulty, kill.DungeonWeight, kill.PartyMemberCount));
                var result = new ExperienceBreakdown(Base: worth);
                if (worth == 0) return result;
                foreach (var modifier in await bonuses.CollectAsync(kill.Occasion, cancellationToken)
                    .ConfigureAwait(false))
                    result = result.Add(modifier.Kind, Floor(worth * modifier.Rate));
                _ = result.Total;
                return result;
            case ClearExperienceContext clear:
                var calculated = valuation.WorthOfClear(clear.Clear with { CharacterLevel = characterLevel });
                var settlement = new ExperienceBreakdown(Base: calculated.Base, ClearRank: calculated.RankBonus);
                foreach (var modifier in await bonuses.CollectAsync(clear.Occasion, cancellationToken)
                    .ConfigureAwait(false))
                {
                    // Black diamond includes rank, but never compounds other premium bonuses.
                    var basis = modifier.Kind == ExperienceBonusKind.BlackDiamond
                        ? (double)calculated.Base + calculated.RankBonus : calculated.Base;
                    settlement = settlement.Add(modifier.Kind, Floor(basis * modifier.Rate));
                }
                _ = settlement.Total;
                return settlement;
            case TowerKillExperienceContext kill:
                return new(Base: await (tower ?? throw new InvalidOperationException("Tower reward rules unavailable."))
                    .KillExperienceAsync(characterLevel, kill.ActorType, cancellationToken).ConfigureAwait(false));
            case TowerClearExperienceContext clear:
                return new(Base: await (tower ?? throw new InvalidOperationException("Tower reward rules unavailable."))
                    .ClearExperienceAsync(characterLevel, clear.RewardLevel, cancellationToken).ConfigureAwait(false));
            case QuestExperienceContext quest:
                return new(Base: quest.Base);
            default:
                throw new ArgumentOutOfRangeException(nameof(context));
        }
    }

    private static uint Floor(double value) => value <= 0 || double.IsNaN(value) ? 0
        : checked((uint)Math.Floor(value));
}
