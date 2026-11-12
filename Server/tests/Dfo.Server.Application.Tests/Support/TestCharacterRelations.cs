using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Friends and names held in memory, for the session cleanup that reads who to tell a character left.
/// Empty, it is a world where nobody keeps anyone as a friend.
/// </summary>
internal sealed class TestCharacterRelations : ICharacterRelationQueryDomainService, ICharacterIdentityQueryDomainService
{
    private readonly Dictionary<CharacterId, List<CharacterId>> observers = [];
    private readonly Dictionary<CharacterId, CharacterPublicIdentity> names = [];

    /// <summary>While set, every read fails with it.</summary>
    public Exception? Failure { get; set; }

    public TestCharacterRelations Named(int character, string name)
    {
        names[new(character)] = new(new(character), System.Text.Encoding.UTF8.GetBytes(name), 30, 12, 0);
        return this;
    }

    /// <summary><paramref name="owner"/> keeps <paramref name="target"/> as a friend.</summary>
    public TestCharacterRelations Friend(int owner, int target)
    {
        if (!observers.TryGetValue(new(target), out var list)) observers[new(target)] = list = [];
        list.Add(new(owner));
        return this;
    }

    public ValueTask<IReadOnlyList<CharacterId>> ObserversOfAsync(CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken)
    {
        if (Failure is { } failure) return ValueTask.FromException<IReadOnlyList<CharacterId>>(failure);
        return ValueTask.FromResult<IReadOnlyList<CharacterId>>(kind == CharacterRelationKind.Friend && observers.TryGetValue(target, out var list) ? [.. list] : []);
    }

    public ValueTask<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>> ReadAsync(
        IReadOnlyCollection<CharacterId> characters, CancellationToken cancellationToken)
    {
        if (Failure is { } failure) return ValueTask.FromException<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>>(failure);
        return ValueTask.FromResult<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>>(
            names.Where(entry => characters.Contains(entry.Key)).ToDictionary());
    }

    public ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<bool> ExistsAsync(CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public int Limit(CharacterRelationKind kind) => 64;

    public ValueTask<CharacterPublicIdentity?> FindByNameAsync(byte[] name, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
