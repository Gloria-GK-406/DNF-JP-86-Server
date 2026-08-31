using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>
/// Works a session's offer list out again after the character has changed what it is computed from.
/// </summary>
/// <remarks>
/// The list is resolved at entry from level, job, grow type, cleared quests and carried quests. Two
/// things move those inside a session -- taking a quest on and handing one in -- and each has to be
/// followed by a fresh answer, not an edited one.
/// <para>
/// It was an edit at first, and that was wrong in a way only the second half of the tutorial shows.
/// Striking the finished quest out is right as far as it goes and cannot do the other half: quest
/// 1776 declares <c>[pre required quest] 101</c>, so handing 101 in is exactly what makes 1776
/// offerable, and a list that can only shrink never names it. Seen live on 2026-08-27 -- the player
/// finished 青之守护者塔娜, was offered nothing further, could not reach a dungeon, and was stuck
/// until they logged out and the list was computed afresh.
/// </para>
/// </remarks>
public interface IQuestOfferRefreshDomainService
{
    /// <summary>
    /// The session with its offer list, and the inputs behind it, brought up to date.
    /// </summary>
    /// <param name="level">
    /// Where the character stands now, which a completion can have moved. The subject's own copy
    /// is the entry-time one.
    /// </param>
    /// <returns>
    /// The updated session, or the one handed in when the entry that computed it is not available
    /// -- a session prepared before this was recorded has no subject to recompute from, and
    /// answering with a stale list beats answering with an empty one.
    /// </returns>
    ValueTask<SelectCharacterResult> RefreshAsync(
        SelectCharacterResult result,
        AccountId accountId,
        CharacterId characterId,
        int level,
        CancellationToken cancellationToken);
}
