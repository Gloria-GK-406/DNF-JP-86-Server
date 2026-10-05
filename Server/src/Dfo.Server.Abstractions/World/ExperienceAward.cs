using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>The earned reward and the independently updated progression state.</summary>
public sealed record ExperienceAward(ExperienceBreakdown Breakdown, byte Level, uint Total, bool LeveledUp)
{
    /// <summary>Temporary client cumulative value. Never persist this value.</summary>
    public uint NotificationTotal { get; init; } = Total;
    public uint Overflow { get; init; }
    public uint Base => Breakdown.Base;
    public uint Gained => Breakdown.Total;
    public static ExperienceAward Nothing(byte level, uint total) => new(new(), level, total, false);
    public override string ToString() => $"{Gained} ({Breakdown}) -> level {Level} at {Total}";
}
