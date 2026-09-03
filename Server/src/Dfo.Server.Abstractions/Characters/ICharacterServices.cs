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

public interface ICharacterDomainService
{
    ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
        AccountId accountId,
        CancellationToken cancellationToken);

    ValueTask<ActiveCharacterState> LoadForSelectionAsync(
        AccountId accountId,
        ushort slotIndex,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the character the client points at by its position in the roster the server sent,
    /// not by slot. SELECT_CHARACTER and DELETE_CHARACTER both carry a position.
    /// </summary>
    ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(
        AccountId accountId,
        ushort rosterIndex,
        CancellationToken cancellationToken);

    ValueTask<CharacterNameStatus> CheckNameAsync(
        byte[] rawName,
        CancellationToken cancellationToken);

    ValueTask<CharacterCreationResult> CreateCharacterAsync(
        AccountId accountId,
        byte job,
        byte[] rawName,
        CancellationToken cancellationToken);

    /// <summary>
    /// The character as something to ask, rather than as the snapshot everything above reads.
    /// </summary>
    /// <remarks>
    /// The two products of this repository. A snapshot is a set of records taken at one moment and
    /// is what initialisation reads out; the character is what business acts through, and it holds
    /// nothing, so it can be built when it is asked for. Nothing is prepared for it at selection
    /// and nothing released afterwards -- what it reads exists only while the character is being
    /// played, and it answers accordingly.
    /// </remarks>
    ICharacter Get(CharacterId characterId);

    ValueTask<CharacterDeletionResult> DeleteCharacterAsync(
        AccountId accountId,
        ushort slotIndex,
        byte[] rawName,
        CancellationToken cancellationToken);
}

public interface IActiveCharacterDomainService
{
    /// <summary>
    /// Records which character the selected session is playing.
    /// </summary>
    /// <remarks>
    /// The session already names its character, so nothing else is asked for. Which character an
    /// account is playing is read back from the online user's own snapshot, where this binds it.
    /// This used to take a whole <c>ActiveCharacterState</c> and keep it, and no caller ever read
    /// that copy back -- a character's standing is read from the session's runtime state.
    /// </remarks>
    void Publish(SelectedSessionIdentity session);

    bool ClearExact(SelectedSessionIdentity session);
}
