using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

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

    /// <summary>Is this exact name already taken, on any account?</summary>
    /// <remarks>
    /// Compares the raw name bytes rather than a decoded string. Two different byte sequences can
    /// decode to the same text, and the client identifies a character by the bytes it sent.
    /// </remarks>
    ValueTask<bool> NameExistsAsync(byte[] name, CancellationToken cancellationToken);

    ValueTask<CharacterId> CreateAsync(NewCharacterSpec spec, CancellationToken cancellationToken);

    /// <summary>Marks the character deleted without removing the row. Returns false if absent.</summary>
    ValueTask<bool> SoftDeleteAsync(CharacterId characterId, CancellationToken cancellationToken);
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

    ValueTask<CharacterNameStatus> CheckNameAsync(
        byte[] rawName,
        CancellationToken cancellationToken);

    ValueTask<CharacterCreationResult> CreateCharacterAsync(
        AccountId accountId,
        byte job,
        byte[] rawName,
        CancellationToken cancellationToken);

    ValueTask<CharacterDeletionResult> DeleteCharacterAsync(
        AccountId accountId,
        ushort slotIndex,
        byte[] rawName,
        CancellationToken cancellationToken);
}

public interface IActiveCharacterService
{
    void Publish(AccountId accountId, ActiveCharacterState character);

    ActiveCharacterState GetRequired(AccountId accountId);

    void Publish(SelectedSessionIdentity session, ActiveCharacterState character);

    bool ClearExact(SelectedSessionIdentity session);
}
