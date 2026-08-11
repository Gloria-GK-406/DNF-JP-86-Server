using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterRepository
{
    ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
        AccountId accountId,
        CancellationToken cancellationToken);
}

public interface ICharacterService
{
    ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
        AccountId accountId,
        CancellationToken cancellationToken);
}
