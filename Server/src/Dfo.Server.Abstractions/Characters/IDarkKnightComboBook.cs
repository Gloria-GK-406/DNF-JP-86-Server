using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// One character's Dark Knight combo pages, asked for and changed by page number.
/// </summary>
/// <remarks>
/// The book is bound to one character. Its methods therefore do not take one: an instance that
/// served several characters would have to be trusted to keep them apart, and every caller would
/// have to keep passing the one fact the book already knows.
///
/// It holds no state. Every answer it gives is read at the moment it is asked for, which is what
/// makes it correct inside a use case that has already written through it and inside one that was
/// rolled back -- a cached answer would outlive the transaction that produced it and start
/// describing a state nobody is in.
///
/// <b>It is not a store</b>, and the difference is not decoration. A store is what a transaction
/// settles: it is asked for a session and it is keyed on identity. This is an instance -- it has
/// the character's identity, it can be changed, and what it changes lands through the store like
/// any other change to rows that outlive the process.
///
/// <b>It does not ask what job the character is.</b> Whether a page is there is what the rows say;
/// whether a character is a Dark Knight is a column of another table. The entry load does ask,
/// because it is deciding whether to put the section in a frame at all, and that decision is the
/// frame's rather than the rows'.
/// </remarks>
public interface IDarkKnightComboBook
{
    /// <summary>Whose combo pages these are.</summary>
    CharacterId CharacterId { get; }

    /// <summary>Every page the character has, in page order.</summary>
    ValueTask<IReadOnlyList<DarkKnightComboEntryState>> PagesAsync(
        CancellationToken cancellationToken);

    /// <summary>The page with this number, or null when the character has no such page.</summary>
    /// <remarks>
    /// Null rather than an empty page. A character who has never had a page stored and one whose
    /// page is empty are different states of the rows, and answering both the same way would
    /// describe the first as the second.
    /// </remarks>
    ValueTask<DarkKnightComboEntryState?> FindPageAsync(
        byte pageIndex, CancellationToken cancellationToken);

    /// <summary>Puts this exact page in its place, replacing whatever was there.</summary>
    /// <remarks>
    /// The whole page at once, because that is what a page is on this wire: one body the client
    /// reads from end to end. There is no verb for part of one.
    ///
    /// It carries no rule of its own beyond the address. Which page numbers a character can have
    /// is the store's to refuse, because a row outside them is not a wrong answer but a character
    /// the entry load will not carry.
    /// </remarks>
    ValueTask SetPageAsync(DarkKnightComboEntryState page, CancellationToken cancellationToken);

    /// <summary>Leaves the character with no page of this number.</summary>
    ValueTask ClearPageAsync(byte pageIndex, CancellationToken cancellationToken);
}

/// <summary>Which combo pages belong to which character.</summary>
/// <remarks>
/// A character always has a book. There is nothing to register and nothing to find missing: the
/// book is a way of asking about rows, and a character with no pages has an empty book rather than
/// none -- including every character who is not a Dark Knight, because what makes the book empty
/// is that there are no rows.
/// </remarks>
public interface ICharacterDarkKnightComboDomainService
{
    /// <summary>The combo pages of one character of one account.</summary>
    /// <remarks>
    /// The account is half of the address rather than a check made before using it: these rows
    /// belong to a character of an account, and a pairing that does not hold is refused where the
    /// rows are written rather than pretended to be empty here.
    /// </remarks>
    IDarkKnightComboBook BookOf(AccountId accountId, CharacterId characterId);
}
