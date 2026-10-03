namespace Dfo.Server.Abstractions.World;

public abstract record ExperienceCalculationContext(ExperienceOccasion Occasion);
public sealed record KillExperienceContext(ExperienceOccasion Occasion, int MonsterLevel,
    MonsterRank Rank, Dfo.Server.Data.World.DungeonDifficulty Difficulty, double DungeonWeight,
    int PartyMemberCount) : ExperienceCalculationContext(Occasion);
public sealed record ClearExperienceContext(ExperienceOccasion Occasion, ClearCircumstances Clear)
    : ExperienceCalculationContext(Occasion);
public sealed record QuestExperienceContext(ExperienceOccasion Occasion, uint Base)
    : ExperienceCalculationContext(Occasion);

public sealed record TowerKillExperienceContext(ExperienceOccasion Occasion, byte ActorType)
    : ExperienceCalculationContext(Occasion);
public sealed record TowerClearExperienceContext(ExperienceOccasion Occasion, int RewardLevel)
    : ExperienceCalculationContext(Occasion);

public interface IExperienceCalculationDomainService
{
    ValueTask<ExperienceBreakdown> CalculateAsync(ExperienceCalculationContext context,
        byte characterLevel, CancellationToken cancellationToken);
}
