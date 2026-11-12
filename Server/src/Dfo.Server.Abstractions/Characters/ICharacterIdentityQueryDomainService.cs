using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>What anyone may know about a live character: who it is and what it looks like in a list.</summary>
/// <remarks>The name is the stored bytes, which are not always valid text and are compared as bytes.</remarks>
public sealed record CharacterPublicIdentity(CharacterId Character, byte[] Name, int Level, int Job, int GrowType);

/// <summary>Finds live characters by exact name and reads several by identifier at once.</summary>
/// <remarks>
/// Live means not marked deleted. Both questions answer from the characters' current rows, so a
/// caller showing someone else's name or level never shows a copy it kept.
/// </remarks>
public interface ICharacterIdentityQueryDomainService
{
    /// <summary>The live character whose stored name is exactly these bytes, or nothing.</summary>
    ValueTask<CharacterPublicIdentity?> FindByNameAsync(byte[] name, CancellationToken cancellationToken);

    /// <summary>The live characters among <paramref name="characters"/>; a missing or deleted one is simply absent.</summary>
    ValueTask<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>> ReadAsync(
        IReadOnlyCollection<CharacterId> characters, CancellationToken cancellationToken);
}

/// <summary>The <c>characters</c> rows behind <see cref="ICharacterIdentityQueryDomainService"/>.</summary>
public interface ICharacterIdentityRepository
{
    /// <summary>Exact match on <c>COALESCE(name_bytes, CAST(name AS BLOB))</c> among rows with <c>delete_flag=0</c>.</summary>
    ValueTask<CharacterPublicIdentity?> FindLiveByNameAsync(byte[] name, CancellationToken cancellationToken);

    /// <summary>The rows with <c>delete_flag=0</c> among <paramref name="characters"/>, in no particular order.</summary>
    ValueTask<IReadOnlyList<CharacterPublicIdentity>> ReadLiveAsync(
        IReadOnlyList<CharacterId> characters, CancellationToken cancellationToken);
}
