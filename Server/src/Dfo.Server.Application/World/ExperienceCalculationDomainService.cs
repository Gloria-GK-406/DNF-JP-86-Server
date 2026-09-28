using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class ExperienceCalculationDomainService(
    IExperienceValuationDomainService valuation, IExperienceBonusDomainService bonuses)
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
                // Preserve current rules: settlement modifiers will be enabled with their feature.
                return new(Base: calculated.Base, ClearRank: calculated.RankBonus);
            case QuestExperienceContext quest:
                return new(Base: quest.Base);
            default:
                throw new ArgumentOutOfRangeException(nameof(context));
        }
    }

    private static uint Floor(double value) => value <= 0 || double.IsNaN(value) ? 0
        : checked((uint)Math.Floor(value));
}
