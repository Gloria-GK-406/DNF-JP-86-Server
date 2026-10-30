using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>What came of paying for a finished run.</summary>
/// <param name="Award">
/// What was awarded and where the character now stands, or nothing when there was no character to
/// award to. The same distinction a kill draws: an award of nothing still knows the character's
/// standing, and this does not.
/// </param>
/// <param name="ClearBase">What the clear itself was worth, for the result screen to report.</param>
/// <param name="RankBonus">What the run's rank added, likewise.</param>
public sealed record ClearExperienceResult(
    ExperienceAward? Award, uint ClearBase, uint RankBonus, bool AttendanceChanged = false,
    CharacterDungeonDifficulty? DifficultyUnlocked = null)
{
    public DespairTowerSettlement? Despair { get; init; }
}

/// <summary>Settles a finished run for one character.</summary>
public interface IDungeonClearSettlementDomainService
{
    /// <summary>Pays for the clear inside the caller's work unit.</summary>
    ValueTask<ClearExperienceResult> GrantAsync(AccountId accountId, CharacterId characterId, int dungeonId,
        ClearCircumstances clear, SelectedSessionIdentity? session, Guid runId, int settlementLevel,
        DateTimeOffset? settledAt, IEffectFootprintRecorder footprint, CancellationToken cancellationToken);
}
