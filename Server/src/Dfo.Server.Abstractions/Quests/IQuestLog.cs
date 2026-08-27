using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>One quest the character is carrying, and how far along it is.</summary>
/// <remarks>
/// A reference and a progress value, and nothing else. What the quest asks for and what it pays
/// out are not copied here: the client reads them from the same archive this server does, so a
/// snapshot could only ever be a second answer to a question that already has one.
/// </remarks>
public sealed record CarriedQuest(short Slot, int QuestId, int TriggerValue);

/// <summary>One quest the character has finished, and the mark finishing it left.</summary>
public sealed record ClearedQuest(int QuestId, byte Mark);

/// <summary>Why a quest was not taken on.</summary>
/// <remarks>
/// These are the log's own reasons, not the client's codes. What a refused player is told is a
/// number on the wire, and which number belongs to which reason is the protocol's business; the
/// log's business is that the two refusals are told apart at all.
/// </remarks>
public enum QuestRefusal
{
    /// <summary>The character is already carrying it.</summary>
    AlreadyCarried,

    /// <summary>Every slot the character has is taken.</summary>
    NoFreeSlot,
}

/// <summary>What became of one attempt to take a quest on.</summary>
/// <param name="TriggerValue">
/// The progress the quest starts at, which is the archive's value rather than anything stored --
/// nothing was stored for this quest until now.
/// </param>
public sealed record QuestAcceptance(
    bool Accepted, short Slot, int TriggerValue, QuestRefusal? Refusal)
{
    public static QuestAcceptance Taken(short slot, int triggerValue) =>
        new(true, slot, triggerValue, null);

    public static QuestAcceptance Refused(QuestRefusal refusal) => new(false, 0, 0, refusal);
}

/// <summary>
/// Everything one character's quests amount to: what is being carried, what has been finished,
/// and what may be done to either.
/// </summary>
/// <remarks>
/// The log is bound to one character. Its methods therefore do not take one: an instance that
/// served several characters would have to be trusted to keep them apart, and every caller would
/// have to keep passing the one fact the log already knows.
///
/// It holds no state. Every answer it gives is read at the moment it is asked for, which is what
/// makes it correct inside a use case that has already written through it and inside one that was
/// rolled back -- a cached answer would outlive the transaction that produced it and start
/// describing a state nobody is in.
///
/// A verb answers with what happened rather than throwing, because "already carrying it" and
/// "no free slot" are ordinary answers that different callers treat differently.
///
/// What is deliberately absent is anything about rewards. Handing over what a quest gives is the
/// caller's to do, through whatever holds items; the log records that the quest was taken on or
/// finished, and a caller whose own part failed says so by giving the quest back up. Keeping the
/// two apart is what lets the log stay a log.
/// </remarks>
public interface IQuestLog
{
    /// <summary>Whose quests these are.</summary>
    CharacterId CharacterId { get; }

    /// <summary>Every quest the character is carrying, in slot order.</summary>
    ValueTask<IReadOnlyList<CarriedQuest>> CarriedAsync(CancellationToken cancellationToken);

    /// <summary>The carried quest with this number, or null when it is not being carried.</summary>
    ValueTask<CarriedQuest?> FindCarriedAsync(int questId, CancellationToken cancellationToken);

    /// <summary>Every quest the character has finished.</summary>
    ValueTask<IReadOnlyList<ClearedQuest>> ClearedAsync(CancellationToken cancellationToken);

    /// <summary>Which quests the character asked to be told about, in the order they are drawn.</summary>
    ValueTask<IReadOnlyList<int>> TrackedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Takes a quest on, deciding for itself whether that is allowed and where it goes.
    /// </summary>
    /// <remarks>
    /// The definition is handed in rather than looked up, because which quest the caller means is
    /// the caller's question and what the quest is is the archive's answer. What the log adds is
    /// the character: whether this one already carries it, and which of its slots is free.
    ///
    /// The slot is the lowest free one, which is the reference server's rule and is visible to the
    /// player -- the client draws the carried list in slot order, so a quest given the wrong slot
    /// is a quest in the wrong place on screen.
    ///
    /// <b>Two of the reference server's refusals are not asked here, and this takes on a quest
    /// where it would refuse</b>: a quest already cleared and not repeatable, and a quest whose
    /// prerequisites are unmet. Both are answerable now that finished quests are recorded, and
    /// neither is answerable yet in practice, because nothing finishes a quest -- every log is
    /// empty of clears, so the checks would pass on state no use case has ever written. They
    /// belong with quest completion rather than ahead of it. The gap is mostly unreachable from a
    /// real client, which offers only what its own copy of that state allows and refuses the rest
    /// before sending; it is not unreachable from a crafted frame. See
    /// protocol/game/client-to-server/0x001F.md.
    /// </remarks>
    ValueTask<QuestAcceptance> AcceptAsync(
        QuestDefinition definition, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a carried quest to a new progress value.
    /// </summary>
    /// <param name="asLastSeen">
    /// The quest as the caller last saw it. All three of its parts are part of what the caller is
    /// asserting: the slot says which row, the quest number says the row still holds that quest,
    /// and the progress says it still holds the value the caller made its decision about. A quest
    /// that has moved in any of those respects fails rather than being overwritten.
    ///
    /// The slot is named rather than looked up because a caller acting on a snapshot knows which
    /// row it read. Looking the quest up by number instead would pick one row when the table holds
    /// two -- which the primary key allows, being the slot alone -- and correct the wrong one.
    /// </param>
    /// <returns>False when that slot is not holding that quest.</returns>
    /// <remarks>
    /// Not to be used on a quest this same use case took on. The expectation asserts a value the
    /// caller has seen, and nobody has seen a row that has not been written yet; the store would
    /// be handed an update for a row its own insert has not reached, and the use case would fail.
    /// A quest whose progress is known at the moment it is taken on says so through
    /// <see cref="AcceptAsync"/>, which is where its starting value comes from.
    /// </remarks>
    ValueTask<bool> RecordProgressAsync(
        CarriedQuest asLastSeen, int triggerValue, CancellationToken cancellationToken);

    /// <summary>
    /// Finishes a carried quest: it stops being carried and starts being cleared.
    /// </summary>
    /// <param name="mark">
    /// What finishing it leaves behind, which is never nothing: a quest marked zero is one every
    /// reader of the cleared list -- this log, the entry projection, the offerable-quest rules --
    /// treats as never finished, so writing one would drop the quest out of both lists at once.
    /// </param>
    /// <returns>False when the character is not carrying that quest.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mark is zero.</exception>
    ValueTask<bool> CompleteAsync(int questId, byte mark, CancellationToken cancellationToken);

    /// <summary>
    /// Gives a carried quest up: it stops being carried and is not recorded as finished.
    /// </summary>
    /// <returns>False when the character is not carrying that quest.</returns>
    ValueTask<bool> GiveUpAsync(int questId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces which quests the character asked to be told about.
    /// </summary>
    /// <remarks>
    /// The whole selection at once rather than one quest at a time, because it is a list stored by
    /// position: adding one quest can move every quest after it, and a verb that took a single
    /// quest would have to invent where it goes.
    ///
    /// What the order means to the player is not settled. This log reads the selection back in the
    /// order it was stored; the entry projection reads the same rows and sorts them by quest
    /// number before sending them. Which of the two the client expects has no evidence either way,
    /// because nothing in this server has ever written the table -- the question arrives with the
    /// tracking protocol, not before it.
    /// </remarks>
    ValueTask TrackAsync(IReadOnlyList<int> questIds, CancellationToken cancellationToken);
}

/// <summary>Which quest log belongs to which character.</summary>
/// <remarks>
/// A character always has one. There is nothing to register and nothing to find missing: the log
/// is a way of asking about rows, and a character with no quests has an empty log rather than none.
/// That is what makes this smaller than the container registry it is modelled on, which has to
/// decide between several containers of one type.
/// </remarks>
public interface ICharacterQuestLogDomainService
{
    /// <summary>The quest log of one character of one account.</summary>
    /// <remarks>
    /// The account is half of the address rather than a check made before using it: quests belong
    /// to a character of an account, and a pairing that does not hold is refused where the rows
    /// are written rather than pretended to be empty here.
    /// </remarks>
    IQuestLog LogOf(AccountId accountId, CharacterId characterId);
}
