using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>Where a character's quests are kept.</summary>
/// <remarks>
/// One of several kinds of state a database holds, and settled together with the rest of them:
/// that is what lets one use case take a quest on and hand over what it gives as a single outcome.
///
/// These rows used to be a section of the character store, which said that a quest is part of the
/// character's own body. It is not. A quest log is its own thing reached through its own store,
/// the way a bag is, and this is that store.
///
/// <b>These rows have a second reader</b>: the entry projection reads all three of them straight
/// from the database, through <c>ICharacterActiveQuestTable</c>, <c>ICharacterQuestClearFlagTable</c>
/// and <c>ICharacterQuestNotifyTable</c>, as forty-odd tables assembled into the snapshot a
/// character enters the world with. That reader does not share this session, so it sees the rows as
/// they were settled and not as an open use case has decided them.
///
/// <b>Neither reader sees the other's open use case</b>, and it is worth being exact about why
/// that costs nothing rather than trusting it. A use case that wrote here and then asked the
/// tables would be shown the state before its own writes -- which is the shape the item store had
/// to answer with <see cref="Inventory.IItemDomainStoreSession.ApplyDecidedWritesAsync"/>. No use case
/// does that with quests: entry builds its snapshot first and then corrects the snapshot in memory
/// alongside the rows, and every other quest use case reads and writes only here. The day one does
/// read back through the tables inside its own boundary, it needs the same operation the items
/// have; until then, adding it would be answering a question nobody asks.
///
/// <b>The two readers do differ over one thing</b>, and it is not about transactions. For
/// <see cref="Carried"/> and <see cref="Cleared"/> they agree row for row. For
/// <see cref="Tracked"/> they do not agree on order: this store answers in the order the rows were
/// stored, and the entry projection sorts the same rows by quest number before sending them.
/// Nothing has ever written that table, so no session has ever been able to tell the difference,
/// and there is no evidence which order the client wants. Whoever implements quest tracking
/// settles it; see <see cref="IQuestLog.TrackAsync"/>.
/// </remarks>
public interface IQuestDomainStore : IDomainStore<IQuestDomainStoreSession>;

/// <summary>One use case's reads and writes against one store of quests.</summary>
/// <remarks>
/// Three sections, because a character's quest state is three unrelated sets of rows rather than
/// one addressing scheme: what is being carried, what has been finished, and what the player asked
/// to be told about. Each speaks the shared <see cref="IDomainStoreSession{TPlace,TKey,TValue}"/>
/// vocabulary, and each is addressed by the character and the account it belongs to -- the account
/// is half of the address, not a check made before using it.
/// </remarks>
public interface IQuestDomainStoreSession
{
    /// <summary>The quests the character is carrying, by the slot each sits in.</summary>
    IDomainStoreSession<OwnedCharacterId, short, StoredActiveQuest> Carried { get; }

    /// <summary>The quests the character has finished, by quest.</summary>
    IDomainStoreSession<OwnedCharacterId, int, StoredClearedQuest> Cleared { get; }

    /// <summary>Which quests the character asked to be told about, by the slot each sits in.</summary>
    IDomainStoreSession<OwnedCharacterId, short, StoredTrackedQuest> Tracked { get; }
}

/// <summary>The whole of what one quest slot holds.</summary>
/// <param name="ExpectedTriggerValue">
/// What the caller last saw in this slot, or null to write regardless.
/// </param>
/// <remarks>
/// The expectation travels with the value because it is part of what the caller is asserting: not
/// merely that the slot should now hold this, but that it still holds what they were told it did.
/// Writing without it would lose the guard that stands between a stale entry snapshot and the row
/// as it is now.
/// </remarks>
public sealed record StoredActiveQuest(
    short Slot, int QuestId, int TriggerValue, int? ExpectedTriggerValue = null);

/// <summary>The mark one finished quest left behind.</summary>
/// <remarks>
/// A quest is finished by having a mark at all: the entry projection reads every non-zero one of
/// these as a cleared quest, and the value beside it is what the client is handed to draw with.
/// A quest with no row here is one this character has never finished.
/// </remarks>
public sealed record StoredClearedQuest(int QuestId, byte Mark);

/// <summary>One of the quests the character asked to be told about.</summary>
/// <remarks>
/// The selection is an ordered list rather than a set -- the client draws it in slot order -- so
/// the slot is part of what is stored rather than something derived when it is read back.
/// </remarks>
public sealed record StoredTrackedQuest(short SlotIndex, int QuestId);
