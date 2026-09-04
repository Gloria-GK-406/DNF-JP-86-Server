using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// One character's combo pages, answered by reading the store every time.
/// </summary>
/// <remarks>
/// Nothing is held between calls. The alternative -- reading once and keeping the answer -- fails
/// in both directions inside a use case: a book that cached before a write would go on describing
/// the state before it, and one that cached after a rollback would describe a state that was taken
/// back. Reading through costs a statement and removes the question.
///
/// How the store is reached is <see cref="AmbientStoreAccess"/>'s to decide. A read outside a use
/// case is allowed and opens a boundary it throws away; a write outside one is refused, because a
/// change to rows that outlive the process with nothing to unwind it is exactly what the boundary
/// exists to prevent.
/// </remarks>
internal sealed class DarkKnightComboBook(
    OwnedCharacterId place, IDarkKnightComboDomainStore store, AmbientStoreAccess stores)
    : IDarkKnightComboBook
{
    public CharacterId CharacterId => place.Character;

    public ValueTask<IReadOnlyList<DarkKnightComboEntryState>> PagesAsync(
        CancellationToken cancellationToken) =>
        stores.ReadAsync(store, session => session.ReadAllAsync(place, cancellationToken));

    /// <remarks>
    /// Asked of the store by number rather than filtered out of every page, because the store is
    /// what addresses a page and a book that read them all and picked one would be answering from
    /// its own copy of the addressing.
    /// </remarks>
    public ValueTask<DarkKnightComboEntryState?> FindPageAsync(
        byte pageIndex, CancellationToken cancellationToken) =>
        stores.ReadAsync(store, session => session.ReadAsync(place, pageIndex, cancellationToken));

    public ValueTask SetPageAsync(
        DarkKnightComboEntryState page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return stores.ChangeAsync(store, session =>
        {
            session.Write(place, page);
            return ValueTask.CompletedTask;
        }, cancellationToken);
    }

    public ValueTask ClearPageAsync(byte pageIndex, CancellationToken cancellationToken) =>
        stores.ChangeAsync(store, session =>
        {
            session.Clear(place, pageIndex);
            return ValueTask.CompletedTask;
        }, cancellationToken);
}

/// <summary>Hands out a book per character, holding nothing between calls.</summary>
/// <remarks>
/// There is no registry here and nothing to clean up when a character leaves, because a book owns
/// no state of its own -- it is a way of addressing rows. Two books asked for the same character
/// are the same book in every way that matters: they read and write the same place through the
/// same store.
/// </remarks>
internal sealed class CharacterDarkKnightComboDomainService(
    IDarkKnightComboDomainStore store, AmbientStoreAccess stores)
    : ICharacterDarkKnightComboDomainService
{
    public IDarkKnightComboBook BookOf(AccountId accountId, CharacterId characterId) =>
        new DarkKnightComboBook(new OwnedCharacterId(accountId, characterId), store, stores);
}
