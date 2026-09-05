using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// Which character's rows, and under whose account they must sit.
/// </summary>
/// <remarks>
/// The account is part of the address rather than a check made before using it. These rows belong
/// to a character of an account; naming a character without the account it belongs to does not
/// identify anything this store will touch, and a store asked for a pairing that does not hold
/// refuses rather than writing rows nobody can reach.
/// </remarks>
public readonly record struct OwnedCharacterId(AccountId Account, CharacterId Character);

/// <summary>Where a character's own rows are kept.</summary>
/// <remarks>
/// One of several kinds of state a database holds. Its session settles together with every other
/// kind kept in the same database, which is what lets one use case change a character and the
/// items it owns as a single outcome.
/// </remarks>
public interface ICharacterDomainStore : IDomainStore<ICharacterDomainStoreSession>;

/// <summary>One use case's reads and writes against one store of characters.</summary>
/// <remarks>
/// A character is not one homogeneous table the way a container is, so the session is a handful of
/// named sections rather than a single addressing scheme. Each section that holds many rows per
/// character speaks the shared <see cref="IDomainStoreSession{TPlace,TKey,TValue}"/> vocabulary; the one
/// that holds a single row per character says so by having no key at all.
///
/// Adding a section adds a property here, not a verb: what the caller can ask of a section is
/// fixed, and only what a section holds is new.
/// </remarks>
public interface ICharacterDomainStoreSession
{
    /// <summary>The creatures the character owns, by the identity each of them is.</summary>
    /// <remarks>
    /// The key is the world identity of the creature -- the same number the creature list frame
    /// writes as each entry's first field. There is one identity space for a creature and it is
    /// the narrow one the wire can say; a second key kept beside it purely to be wider would be
    /// two things somebody has to keep in step. The database bounds what it allocates to that
    /// width, so the conversion the frame makes can never be the thing that fails.
    /// </remarks>
    IDomainStoreSession<OwnedCharacterId, long, StoredCreature> Creatures { get; }

    /// <summary>Whether this pairing is one this store can place at all.</summary>
    /// <remarks>
    /// A question about the address, not about the character. A caller that must fail loudly on a
    /// pairing that does not hold asks this before doing anything else, rather than discovering it
    /// only if some later write happens to be staged.
    /// </remarks>
    ValueTask<bool> HoldsAsync(OwnedCharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Where the character is standing, or null when the pairing does not hold.</summary>
    ValueTask<CharacterPositionState?> ReadPositionAsync(
        OwnedCharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Puts the character at this exact place.</summary>
    void WritePosition(OwnedCharacterId characterId, CharacterPositionState position);
}

/// <summary>The condition one of a character's creatures is in.</summary>
/// <param name="CreatureKey">
/// Which creature: its world identity, which is the whole of what names it.
/// </param>
/// <param name="OnlyWhenAtMostZero">
/// Whether to write only while the condition is still at most nothing.
/// </param>
/// <remarks>
/// Like a quest's expectation, the condition under which the write may happen travels with it,
/// because it is part of what the caller is asserting rather than something checked separately
/// and hoped to still hold by the time the write lands.
/// </remarks>
public sealed record StoredCreature(
    long CreatureKey, int Field04, bool OnlyWhenAtMostZero = false);
