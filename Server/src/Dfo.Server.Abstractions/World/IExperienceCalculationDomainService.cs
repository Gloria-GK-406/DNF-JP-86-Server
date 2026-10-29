namespace Dfo.Server.Abstractions.World;

public abstract record ExperienceCalculationContext(ExperienceOccasion Occasion);
public sealed record KillExperienceContext(ExperienceOccasion Occasion, int MonsterLevel,
    MonsterRank Rank, Dfo.Server.Data.World.DungeonDifficulty Difficulty, double DungeonWeight,
    int PartyMemberCount, bool IsNamedMonster = false) : ExperienceCalculationContext(Occasion);
public sealed record ClearExperienceContext(ExperienceOccasion Occasion, ClearCircumstances Clear)
    : ExperienceCalculationContext(Occasion);
public sealed record QuestExperienceContext(ExperienceOccasion Occasion, uint Base)
    : ExperienceCalculationContext(Occasion);

/// <param name="Tower">Which tower runtime paid the kill; each has its own reward table.</param>
public sealed record TowerKillExperienceContext(ExperienceOccasion Occasion, DungeonParticipationMode Tower, byte ActorType)
    : ExperienceCalculationContext(Occasion);
/// <param name="Tower">Which tower runtime paid the clear; each has its own reward table.</param>
public sealed record TowerClearExperienceContext(ExperienceOccasion Occasion, DungeonParticipationMode Tower, int RewardLevel)
    : ExperienceCalculationContext(Occasion);

public interface IExperienceCalculationDomainService
{
    ValueTask<ExperienceBreakdown> CalculateAsync(ExperienceCalculationContext context,
        byte characterLevel, CancellationToken cancellationToken);
}
