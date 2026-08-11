using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterService(Func<ICharacterRepository> characterRepository)
    : ICharacterService
{
    private const ushort DefaultSlotLimit = 32;

    public async ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        var characters = await characterRepository().ListByAccountAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);
        return new AccountCharacterRoster(accountId, DefaultSlotLimit, characters);
    }
}
