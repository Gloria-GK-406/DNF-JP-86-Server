using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
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
    /// <summary>
    /// Prepares and publishes a character's entry on a channel.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="ChannelAdmissionRefusedException"/> when the channel turns the
    /// character away. That happens before anything is published, so a refused entry
    /// leaves no trace.
    /// </remarks>
    ValueTask<SelectCharacterResult> EnterAsync(
        AccountId accountId,
        ushort slotIndex,
        ChannelEntryIdentity channel,
        CancellationToken cancellationToken);
}
