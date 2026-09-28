using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>The earned reward and the independently updated progression state.</summary>
public sealed record ExperienceAward(ExperienceBreakdown Breakdown, byte Level, uint Total, bool LeveledUp)
{
    public uint Base => Breakdown.Base;
    public uint Gained => Breakdown.Total;
    public static ExperienceAward Nothing(byte level, uint total) => new(new(), level, total, false);
    public override string ToString() => $"{Gained} ({Breakdown}) -> level {Level} at {Total}";
}

public interface ICharacterExperienceDomainService
{
    uint? ThresholdForLevel(byte level);
    ValueTask<bool> TrySetProgressAsync(CharacterId characterId, byte level, uint experience,
        CancellationToken cancellationToken);

    /// <summary>Calculate using the stored level, then persist progression in the caller's transaction.
    /// The reward is never clipped to the level ceiling.</summary>
    ValueTask<ExperienceAward?> AwardAsync(CharacterId characterId,
        Func<byte, ValueTask<ExperienceBreakdown>> valuation, CancellationToken cancellationToken);
}
