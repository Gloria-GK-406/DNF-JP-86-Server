using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Quests;

/// <summary>
/// Works the offer list out again after the character has changed what it is computed from.
/// </summary>
/// <remarks>
/// The list is resolved at entry from level, job, grow type, cleared quests and carried quests.
/// Two things move those inside a session -- taking a quest on and handing one in -- and each has
/// to be followed by a fresh answer, not an edited one.
/// <para>
/// It was an edit at first, and that was wrong in a way only the second half of the tutorial shows.
/// Striking the finished quest out is right as far as it goes and cannot do the other half: quest
/// 1776 declares <c>[pre required quest] 101</c>, so handing 101 in is exactly what makes 1776
/// offerable, and a list that can only shrink never names it. Seen live on 2026-08-27 -- the player
/// finished 青之守护者塔娜, was offered nothing further, could not reach a dungeon, and was stuck
/// until they logged out and the list was computed afresh.
/// </para>
/// The character's own state is read back rather than passed in, because the two callers know
/// different halves of it and the log knows all of it.
/// </remarks>
internal sealed class QuestOfferRefreshDomainService(
    IWorldContentEntryRuleService worldContent,
    ICharacterQuestLogDomainService logs, ICharacterDomainService characters,
    Dfo.Server.Abstractions.Creatures.ICreatureDomainService? creatures = null, IQuestQueryDomainService? query = null) : IQuestOfferRefreshDomainService
{
    public async ValueTask<AvailableQuestEntryState> ReadAvailableAsync(
        AccountId accountId, CharacterId characterId, int level, CancellationToken ct)
    {
        if (query is not null) return await query.ReadAvailableAsync(accountId, characterId, ct).ConfigureAwait(false);
        var identity = await characters.Get(characterId).IdentityAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Character identity is unavailable while refreshing quest offers.");
        var log = logs.LogOf(accountId, characterId);
        var carried = await log.CarriedAsync(ct).ConfigureAwait(false);
        var cleared = await log.ClearedAsync(ct).ConfigureAwait(false);
        var creature = creatures is null ? null : await creatures.GetStore(characterId).CurrentAsync(ct).ConfigureAwait(false);
        var subject = new WorldContentSubject(level, identity.Job, identity.GrowType,
            cleared.ToDictionary(x => x.QuestId, x => (int)x.Mark), [.. carried.Select(x => x.QuestId)], [], [],
            creature?.ItemTemplateId ?? 0, creature?.Level ?? 0);
        return await worldContent.ReadOffersAsync(subject, ct).ConfigureAwait(false);
    }

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
    public async ValueTask<SelectCharacterResult> RefreshAsync(
        SelectCharacterResult result,
        AccountId accountId,
        CharacterId characterId,
        int level,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.World.Subject is not { } subject)
            return result;

        var identity = await characters.Get(characterId).IdentityAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Character identity is unavailable while refreshing quest offers.");
        var log = logs.LogOf(accountId, characterId);
        var carried = await log.CarriedAsync(cancellationToken).ConfigureAwait(false);
        var cleared = await log.ClearedAsync(cancellationToken).ConfigureAwait(false);

        var updated = subject with
        {
            Level = level,
            Job = identity.Job,
            GrowType = identity.GrowType,
            CarriedQuestIds = [.. carried.Select(quest => quest.QuestId)],
            ClearedQuestFlags = cleared.ToDictionary(
                quest => quest.QuestId, quest => (int)quest.Mark),
        };

        var content = await worldContent.LoadAsync(updated, cancellationToken)
            .ConfigureAwait(false);
        return result with
        {
            World = result.World with { Content = content, Subject = updated },
        };
    }
}
