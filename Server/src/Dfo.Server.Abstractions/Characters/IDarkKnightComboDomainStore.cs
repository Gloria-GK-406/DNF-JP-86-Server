using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>Where a character's Dark Knight combo pages are kept.</summary>
/// <remarks>
/// One of several kinds of state a database holds, and settled together with the rest of them:
/// that is what lets one use case change a combo page and whatever else the same use case decided
/// as a single outcome.
///
/// <b>These rows have a second reader</b>: the entry load reads them straight from the database,
/// through <see cref="Persistence.ICharacterComboPageTable"/>, as one of the forty-odd tables
/// assembled into the snapshot a character enters the world with. That reader is not a rival
/// statement here -- this store's own reads go through the very same gateway, so the two cannot
/// drift over what a row says. What they do not share is a session: the entry reader sees the rows
/// as they were settled, not as an open use case has decided them.
///
/// <b>What the two do differ over is which rows they will carry at all</b>, and it is worth being
/// exact. The entry load decides whether a character has combo pages from the character's job,
/// leaving the section out entirely unless the job is the Dark Knight's; this store decides
/// nothing of the sort, because a page is a row and a row is either there or it is not. The job is
/// a column of another table and no part of this address.
/// </remarks>
public interface IDarkKnightComboDomainStore : IDomainStore<IDarkKnightComboDomainStoreSession>;

/// <summary>One use case's reads and writes against one store of combo pages.</summary>
/// <remarks>
/// One homogeneous table addressed by page number, so this is a named instance of the shared
/// <see cref="IDomainStoreSession{TPlace,TKey,TValue}"/> vocabulary rather than a set of sections
/// the way a character's or a quest log's is. It is named all the same, because what a store keeps
/// is part of the state and an untyped session would trade away exactly the checking that makes
/// these worth having.
///
/// The place is the character and the account it belongs to -- the account is half of the address,
/// not a check made before using it -- and the key is the page number.
///
/// What a page holds is <see cref="DarkKnightComboEntryState"/>, the record the entry projection
/// already builds out of these same two columns, rather than a second record of that shape
/// declared here. Two records saying the same thing drift, and there is nothing about a stored
/// page that the sent one does not already say: a page number and the body as stored.
/// </remarks>
public interface IDarkKnightComboDomainStoreSession
    : IDomainStoreSession<OwnedCharacterId, byte, DarkKnightComboEntryState>;
