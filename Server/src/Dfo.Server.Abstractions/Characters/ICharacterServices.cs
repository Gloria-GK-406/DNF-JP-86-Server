using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterRepository
{
    ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
        AccountId accountId,
        CancellationToken cancellationToken);

    ValueTask<ActiveCharacterState?> FindByAccountAndSlotAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken);
}

public interface ICharacterService
{
    ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
        AccountId accountId,
        CancellationToken cancellationToken);

    ValueTask<ActiveCharacterState> LoadForSelectionAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken);
}

public interface IActiveCharacterService
{
    void Publish(AccountId accountId, ActiveCharacterState character);

    ActiveCharacterState GetRequired(AccountId accountId);
}

public interface ICharacterEntryService
{
    ValueTask<SelectCharacterResult> EnterAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken);
}
