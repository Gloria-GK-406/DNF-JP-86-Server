using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// What a character has left to spend on skills, worked out from what is stored right now.
/// </summary>
/// <remarks>
/// There is no such column. A character's remaining points are the archive's total for its level
/// and job, plus whatever bonus is stored, less what walking its learned skills against the
/// archive's cost table comes to -- so the only way to know them is to work them out, and the only
/// way to be right after a level changes is to work them out again.
/// <para>
/// That is what this exists for. Entry used to be the one place that ever did the sum, and its
/// answer was kept on the session and re-read by every later frame; a character who levelled up
/// went on being told the figure they entered the world with until they logged out and back in.
/// The sum itself is the rule service's, unchanged and shared -- what is added here is a way to
/// ask it about a character by name, from anywhere, at any time.
/// </para>
/// </remarks>
public interface ICharacterSkillPointDomainService
{
    ValueTask<CharacterSkillPointLedger?> LedgerAsync(CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>
    /// What this character has left to spend on each page, or nothing when there is no such
    /// character.
    /// </summary>
    /// <remarks>
    /// Nothing rather than zero. A character who has spent everything has none left, and a
    /// character this server does not have has no ledger at all; reporting the second as the first
    /// would tell the client a figure about somebody who is not there, and every caller of this
    /// acts differently on the two.
    /// </remarks>
    ValueTask<CharacterSkillProtocolMirrorState?> RemainingAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}
