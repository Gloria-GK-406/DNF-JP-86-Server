using Dfo.Server.Abstractions.Creatures;

namespace Dfo.Server.Application.Creatures;

public readonly record struct CreatureExperienceResult(int Experience, int Level, bool LevelChanged);

/// <summary>Pure 70-reference arithmetic used by creature instances to implement their transitions.</summary>
public static class CreatureRules
{
    public static double AdvanceStomach(
        double current, TimeSpan elapsed, bool combat, bool hungerEnabled = true)
    {
        if (!double.IsFinite(current))
            throw new ArgumentOutOfRangeException(nameof(current));
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (!hungerEnabled)
            return 100;

        var rateSeconds = combat ? 60d : 360d;
        var delta = elapsed.TotalSeconds / rateSeconds;
        var next = Math.Clamp(current + (combat ? -delta : delta), 0, 100);
        // Accounting the same whole minute in two calls can leave a sub-ULP residue.
        if (next < 1e-9) return 0;
        return next > 100 - 1e-9 ? 100 : next;
    }

    public static double Feed(double current, int amount = 30)
    {
        if (!double.IsFinite(current))
            throw new ArgumentOutOfRangeException(nameof(current));
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        return Math.Min(100, current + amount);
    }

    /// <summary>Apply the three eligible artifact percentage values to one fatigue cost.</summary>
    public static int CalculateExperienceGain(int fatigueCost, params float[] artifactPercents)
    {
        if (fatigueCost < 0)
            throw new ArgumentOutOfRangeException(nameof(fatigueCost));
        ArgumentNullException.ThrowIfNull(artifactPercents);
        if (artifactPercents.Length > 3 || artifactPercents.Any(value => !float.IsFinite(value)))
            throw new ArgumentOutOfRangeException(nameof(artifactPercents));

        var percentage = artifactPercents.Sum(value => (double)value);
        var bonus = checked((int)Math.Truncate(fatigueCost * percentage / 100d));
        return checked(fatigueCost + bonus);
    }

    public static CreatureExperienceResult GainExperience(CreatureRule rule, int currentExperience, int gain)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (currentExperience < 0)
            throw new ArgumentOutOfRangeException(nameof(currentExperience));
        if (gain < 0)
            throw new ArgumentOutOfRangeException(nameof(gain));
        if (rule.MaxLevel < 1 || rule.ExperienceThresholds.Count == 0)
            throw new ArgumentException("Creature rule has no usable level progression.", nameof(rule));

        var previousLevel = LevelFor(rule, currentExperience);
        if (previousLevel >= rule.MaxLevel || gain == 0)
            return new CreatureExperienceResult(currentExperience, previousLevel, false);

        var experience = checked(currentExperience + gain);
        var level = LevelFor(rule, experience);
        return new CreatureExperienceResult(experience, level, level > previousLevel);
    }

    public static int LevelFor(CreatureRule rule, int experience)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (experience < 0)
            throw new ArgumentOutOfRangeException(nameof(experience));
        if (rule.MaxLevel < 1)
            throw new ArgumentException("Creature rule has no maximum level.", nameof(rule));

        var level = 1;
        foreach (var threshold in rule.ExperienceThresholds)
        {
            if (level >= rule.MaxLevel || experience < threshold)
                break;
            level++;
        }
        return level;
    }
}
