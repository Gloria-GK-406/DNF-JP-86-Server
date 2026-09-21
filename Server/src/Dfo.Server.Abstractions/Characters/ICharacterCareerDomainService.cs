using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterCareerDomainService
{
    ValueTask AdvanceAsync(AccountId accountId, CharacterId characterId, byte growType, CancellationToken cancellationToken);
}
