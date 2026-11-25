using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Accounts;

public sealed record HonorLevelThreshold(uint Level, uint Grade, uint ExperienceToReach);

/// <summary>The account honor scale. The wire value is progress within one level.</summary>
public sealed class HonorExperienceScale
{
    private readonly HonorLevelThreshold[] levels;
    private readonly uint maximumLevelExperience;

    public HonorExperienceScale(IReadOnlyList<HonorLevelThreshold> levels, uint maximumLevelExperience)
    {
        if (levels.Count == 0 || levels[0] != new HonorLevelThreshold(1, 1, 0))
            throw new InvalidDataException("Honor must start at level 1, grade 1, with no required experience.");

        this.levels = levels.ToArray();
        this.maximumLevelExperience = maximumLevelExperience;
        ulong total = maximumLevelExperience;
        for (var index = 1; index < this.levels.Length; index++)
        {
            var level = this.levels[index];
            if (level.Level != index + 1 || level.Grade < this.levels[index - 1].Grade
                || level.ExperienceToReach == 0)
                throw new InvalidDataException($"Invalid honor threshold for level {level.Level}.");
            total += level.ExperienceToReach;
        }
        MaximumExperience = total;
    }

    public ulong MaximumExperience { get; }

    public AccountHonorLevel ProgressFor(ulong totalExperience)
    {
        var remaining = Math.Min(totalExperience, MaximumExperience);
        var current = levels[0];
        foreach (var next in levels.AsSpan(1))
        {
            if (remaining < next.ExperienceToReach)
                return new(current.Level, checked((uint)remaining)) { Grade = current.Grade };
            remaining -= next.ExperienceToReach;
            current = next;
        }
        return new(current.Level, (uint)Math.Min(remaining, maximumLevelExperience)) { Grade = current.Grade };
    }
}
