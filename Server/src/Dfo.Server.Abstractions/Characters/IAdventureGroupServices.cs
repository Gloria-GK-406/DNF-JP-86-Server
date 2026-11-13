using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>An account's adventure group as the rules see it, worked out from its characters' levels.</summary>
/// <param name="Standing">The level and the capped point total.</param>
/// <param name="HighestCharacterLevel">The highest level among the characters counted; zero when none.</param>
public readonly record struct AdventureGroupEvaluation(AdventureGroupStanding Standing, int HighestCharacterLevel);

/// <summary>How character levels turn into an adventure group, as <c>charactermanage.etc</c> declares.</summary>
/// <remarks>
/// Follows the 70 server (<c>CharacManageScript</c>, <c>CUser::SetManagePoint</c>):
/// <list type="bullet">
/// <item>A character contributes the <c>[point bonus]</c> of every level up to and including its own.</item>
/// <item>The account's points are the sum, capped at the threshold of the highest level.</item>
/// <item>The level is the highest whose threshold the points reach, zero below the first.</item>
/// <item>The clear experience bonus is the level's <c>[exp bonus]</c>, except for a character
/// at or above the account's highest character level: a tie counts as highest.</item>
/// </list>
/// It is handed levels; it reads no character or account state.
/// </remarks>
public interface IAdventureGroupRuleService
{
    /// <summary>The adventure group of an account whose characters stand at these levels.</summary>
    AdventureGroupEvaluation Evaluate(IEnumerable<int> characterLevels);

    /// <summary>The share of clear experience a character at this level gains from the adventure group.</summary>
    double ClearExperienceRate(AdventureGroupEvaluation evaluation, int characterLevel);
}

/// <summary>An adventure group rising because one of its characters changed level.</summary>
public sealed record AdventureGroupLevelRise(AccountId AccountId, byte From, byte To)
{
    public int Levels => To - From;
}

/// <summary>
/// The one owner of an account's adventure group (冒险团).
/// </summary>
/// <remarks>
/// Nothing about the adventure group is stored. Every answer is worked out on the spot from the
/// current levels of the account's characters that have not been deleted, so creating, deleting
/// or levelling a character needs no synchronisation. Wire encoders and the experience pipeline
/// take their values from here rather than working them out themselves.
/// </remarks>
public interface IAdventureGroupDomainService
{
    /// <summary>The account's adventure-group level and points.</summary>
    ValueTask<AdventureGroupStanding> ReadAsync(AccountId accountId, CancellationToken cancellationToken);

    /// <summary>The share of clear experience this character gains from its account's adventure group.</summary>
    ValueTask<double> ClearExperienceRateAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a character's level change raised its account's adventure group, and by how much.
    /// </summary>
    /// <remarks>
    /// The character is counted at the event's own before and after levels, the rest of the account
    /// at their current ones, so consecutive changes add up to the account's whole rise. Nothing
    /// when the group did not rise -- including when it fell -- or the character is gone.
    /// </remarks>
    ValueTask<AdventureGroupLevelRise?> LevelRiseAsync(
        CharacterLevelChanged change, CancellationToken cancellationToken);
}
