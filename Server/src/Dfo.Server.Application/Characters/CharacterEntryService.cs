using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterEntryService(
    ICharacterService characters,
    IOnlineUserService users,
    TimeProvider timeProvider) : ICharacterEntryService
{
    public async ValueTask<SelectCharacterResult> EnterAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken)
    {
        var character = await characters.LoadForSelectionAsync(
            accountId,
            slotIndex,
            cancellationToken).ConfigureAwait(false);

        users.BindCurrentCharacter(accountId, character.Identity.CharacterId);
        characters.Activate(accountId, character);
        var preparedAt = timeProvider.GetUtcNow();
        return new SelectCharacterResult(
            character,
            new AccountEntryState(accountId),
            new GameWorldEntryState(preparedAt),
            new CharacterSessionEntryState(accountId, character.Identity.CharacterId));
    }
}
