using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterIdentityQueryDomainService(ICharacterIdentityRepository repository)
    : ICharacterIdentityQueryDomainService
{
    /// <remarks>An empty name cannot be any character's, so it is answered without a read.</remarks>
    public async ValueTask<CharacterPublicIdentity?> FindByNameAsync(byte[] name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length == 0 ? null : await repository.FindLiveByNameAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>> ReadAsync(
        IReadOnlyCollection<CharacterId> characters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (characters.Count == 0) return new Dictionary<CharacterId, CharacterPublicIdentity>();
        var rows = await repository.ReadLiveAsync([.. characters.Distinct()], cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(x => x.Character);
    }
}
