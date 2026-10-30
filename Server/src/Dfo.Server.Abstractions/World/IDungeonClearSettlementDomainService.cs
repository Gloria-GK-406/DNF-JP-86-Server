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

/// <summary>How the attempt to begin a settlement ended.</summary>
public enum ClearSettlementStartKind
{
    /// <summary>The run has not met its completion rule.</summary>
    NotReady,
    /// <summary>The session is no longer in the world.</summary>
    NotInWorld,
    /// <summary>No run in progress, or one already settled.</summary>
    AlreadySettled,
    /// <summary>The claim was taken and released: the character or its cards had nothing to settle.</summary>
    Abandoned,
    /// <summary>The claim was released because no skill point ledger could report the award; the cards stand.</summary>
    Unpayable,
    /// <summary>The claim is held and the attempt must end in Finish or Abandon.</summary>
    Claimed,
}

/// <summary>One seat's fixed cards.</summary>
public sealed record ClearSettlementMemberCards(byte Seat, ClearRewardSettlementResult Cards);

/// <summary>A settlement under way: its claim and everything fixed before anything was paid.</summary>
public sealed record ClearSettlementAttempt(
    DungeonSettlementClaim Claim,
    int SettlementLevel,
    ClearRewardSettlementResult Cards,
    IReadOnlyList<ClearSettlementMemberCards>? MemberCards,
    CharacterSkillProtocolMirrorState? SkillMirror);

/// <summary>What beginning a settlement came to.</summary>
public sealed record ClearSettlementStart(
    ClearSettlementStartKind Kind, DungeonSettlementRules Rules, ClearSettlementAttempt? Attempt = null);

/// <summary>Settles a finished run for one character.</summary>
public interface IDungeonClearSettlementDomainService
{
    /// <summary>Claims the run's settlement and fixes what the result screen reports.</summary>
    /// <remarks>Only a <see cref="ClearSettlementStartKind.Claimed"/> start leaves a claim held.</remarks>
    ValueTask<ClearSettlementStart> BeginAsync(SelectedSessionIdentity session, DungeonRunState run, CancellationToken cancellationToken);

    /// <summary>Spends the claim once the award has committed; throws when it is no longer current.</summary>
    void Finish(ClearSettlementAttempt attempt);

    /// <summary>Releases the claim so the report can be retried.</summary>
    void Abandon(ClearSettlementAttempt attempt);

    /// <summary>Pays for the clear inside the caller's work unit.</summary>
    ValueTask<ClearExperienceResult> GrantAsync(AccountId accountId, CharacterId characterId, int dungeonId,
        ClearCircumstances clear, SelectedSessionIdentity? session, Guid runId, int settlementLevel,
        DateTimeOffset? settledAt, IEffectFootprintRecorder footprint, CancellationToken cancellationToken);
}
