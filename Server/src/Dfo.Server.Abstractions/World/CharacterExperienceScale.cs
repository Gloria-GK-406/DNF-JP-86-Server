using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>The playable levels within the archive's cumulative experience table.</summary>
public sealed class CharacterExperienceScale
{
    public const byte ServerTopLevel = 86;
    private readonly uint[] thresholds;
    public byte TopLevel { get; }
    public uint MaximumExperience => thresholds[TopLevel - 2];
    public IReadOnlyList<uint> Thresholds => Array.AsReadOnly(thresholds);

    public CharacterExperienceScale(IReadOnlyList<uint> thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        TopLevel = checked((byte)Math.Min(ServerTopLevel, thresholds.Count + 1));
        if (TopLevel < 2) throw new InvalidDataException("Experience table is empty.");
        this.thresholds = thresholds.Take(TopLevel - 1).ToArray();
        uint previous = 0;
        foreach (var threshold in this.thresholds)
        {
            if (threshold <= previous) throw new InvalidDataException("Experience thresholds must increase.");
            previous = threshold;
        }
    }

    public uint ExperienceForLevel(byte level)
    {
        if (level < 1 || level > TopLevel) throw new ArgumentOutOfRangeException(nameof(level));
        return level == 1 ? 0 : thresholds[level - 2];
    }

    public CharacterProgressionState ProgressFor(long experience)
    {
        var total = checked((uint)Math.Clamp(experience, 0, MaximumExperience));
        byte level = 1;
        while (level < TopLevel && total >= thresholds[level - 1]) level++;
        return new(level, total);
    }
}
