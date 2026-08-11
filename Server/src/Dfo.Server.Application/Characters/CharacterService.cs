using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterService(Func<ICharacterRepository> characterRepository)
    : ICharacterService
{
    private const ushort DefaultSlotLimit = 32;
    private readonly object sync = new();
    private readonly Dictionary<AccountId, ActiveCharacterState> activeByAccount = [];

    public async ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        var characters = await characterRepository().ListByAccountAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);
        return new AccountCharacterRoster(accountId, DefaultSlotLimit, characters);
    }

    public async ValueTask<ActiveCharacterState> LoadForSelectionAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken)
    {
        return await characterRepository().FindByAccountAndSlotAsync(
            accountId,
            slotIndex,
            cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Account '{accountId}' has no character in slot '{slotIndex}'.");
    }

    public void Activate(AccountId accountId, ActiveCharacterState character)
    {
        ArgumentNullException.ThrowIfNull(character);
        lock (sync)
        {
            if (character.Identity.CharacterId.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(character),
                    "An active character ID must be positive.");
            }

            activeByAccount[accountId] = character;
        }
    }

    public ActiveCharacterState GetRequiredActive(AccountId accountId)
    {
        lock (sync)
        {
            return activeByAccount.TryGetValue(accountId, out var character)
                ? character
                : throw new KeyNotFoundException(
                    $"Account '{accountId}' does not have an active character.");
        }
    }
}
