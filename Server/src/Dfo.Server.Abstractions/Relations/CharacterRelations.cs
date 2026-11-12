using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Relations;

/// <summary>What one character keeps another as.</summary>
/// <remarks>
/// The numeric value is the stored <c>kind</c>, so a value is never renumbered. Recent contacts and
/// mentorship, when they come, are further values of this enum kept by the same owner and the same
/// table rather than owners of their own.
/// </remarks>
public enum CharacterRelationKind : byte
{
    Friend = 1,
    Blacklist = 2,
}

/// <summary>One relation as its owner sees it: who, and since when.</summary>
/// <remarks>
/// Deliberately no name, level or job. Those are the target's current facts and are read by
/// identifier when shown; a copy kept here would go stale on the target's next rename or level.
/// </remarks>
public sealed record CharacterRelation(CharacterId Target, DateTimeOffset Since);

/// <summary>What adding a relation did.</summary>
/// <remarks>
/// Facts only. A repeated friend is a success to the client and a repeated blacklist entry is a
/// refusal; that mapping belongs to each protocol, which is why both are the same fact here.
/// </remarks>
public enum CharacterRelationAddOutcome
{
    Added,
    AlreadyPresent,
    Self,
    TargetMissing,
    Full,
}

/// <summary>What removing a relation did.</summary>
public enum CharacterRelationRemoveOutcome
{
    Removed,
    Missing,
}

/// <summary>What clearing a deleted character's relations leaves for the caller to announce.</summary>
/// <param name="FriendObservers">
/// The live characters that had the purged character as a friend, read before the rows went. They
/// are the ones whose friend list just changed; blacklist owners are not told, as with any other
/// blacklist change made by someone else.
/// </param>
public sealed record CharacterRelationPurge(IReadOnlyList<CharacterId> FriendObservers);

/// <summary>Reads relations; the face every reader (chat, pushes, list building) takes.</summary>
/// <remarks>
/// A <c>*DomainService</c> rather than a query because notification handlers may only take domain
/// services, and they are among its readers.
/// </remarks>
public interface ICharacterRelationQueryDomainService
{
    /// <summary>The owner's relations of that kind whose target is a live character, oldest first, then by target.</summary>
    ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(
        CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken);

    /// <summary>The live characters that hold <paramref name="target"/> in a relation of that kind.</summary>
    ValueTask<IReadOnlyList<CharacterId>> ObserversOfAsync(
        CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="owner"/> holds <paramref name="target"/> in a relation of that kind.</summary>
    ValueTask<bool> ExistsAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken);

    /// <summary>How many relations of that kind one owner may hold.</summary>
    int Limit(CharacterRelationKind kind);
}

/// <summary>Changes relations. Every write, a GM's included, goes through here.</summary>
/// <remarks>
/// Passive by design: it sends nothing, records no projection and raises no event. The caller
/// decides who to tell from the outcome, which keeps one rule set behind every path that writes.
/// Each method needs the use case's writable transaction and refuses without one.
/// </remarks>
public interface ICharacterRelationDomainService
{
    ValueTask<CharacterRelationAddOutcome> AddAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken);

    ValueTask<CharacterRelationRemoveOutcome> RemoveAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken);

    /// <summary>Removes every relation the character is either side of, in the deleting use case.</summary>
    ValueTask<CharacterRelationPurge> PurgeAsync(CharacterId character, CancellationToken cancellationToken);
}

/// <summary>Where relations are kept. Data operations only; every rule is the domain service's.</summary>
public interface ICharacterRelationDomainStore
{
    /// <summary>The owner's rows of that kind whose target is not deleted, ordered by creation time then target.</summary>
    ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(
        CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken);

    /// <summary>The owners, not deleted, of rows of that kind naming <paramref name="target"/>, by owner.</summary>
    ValueTask<IReadOnlyList<CharacterId>> OwnersOfAsync(
        CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken);

    ValueTask<bool> ExistsAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken);

    /// <summary>The owner's rows of that kind whose target is not deleted: what <see cref="ListAsync"/> would return.</summary>
    ValueTask<int> CountAsync(CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken);

    /// <summary>Adds the row; false when it was already there.</summary>
    ValueTask<bool> InsertAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, DateTimeOffset at,
        CancellationToken cancellationToken);

    /// <summary>Removes the row; false when there was none.</summary>
    ValueTask<bool> DeleteAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the live owners holding <paramref name="character"/> as a friend, then deletes every row
    /// with the character on either side.
    /// </summary>
    ValueTask<IReadOnlyList<CharacterId>> PurgeAsync(CharacterId character, CancellationToken cancellationToken);
}
