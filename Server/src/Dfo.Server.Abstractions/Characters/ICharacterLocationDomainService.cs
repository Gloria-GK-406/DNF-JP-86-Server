using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Characters;
/// <summary>Remembers the character's accepted ordinary-world location for its next entry.</summary>
public interface ICharacterLocationDomainService
{
    ValueTask PersistAsync(AccountId account, CharacterId character, CharacterPositionState position, CancellationToken token);
}
