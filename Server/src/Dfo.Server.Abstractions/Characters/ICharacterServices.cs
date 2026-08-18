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

    ValueTask<CharacterDeletionResult> DeleteCharacterAsync(
        AccountId accountId,
        ushort slotIndex,
        byte[] rawName,
        CancellationToken cancellationToken);
}

public interface IActiveCharacterDomainService
{
    void Publish(AccountId accountId, ActiveCharacterState character);

    ActiveCharacterState GetRequired(AccountId accountId);

    void Publish(SelectedSessionIdentity session, ActiveCharacterState character);

    bool ClearExact(SelectedSessionIdentity session);
}

/// <summary>
/// Exclusive write access to a named set of active characters, held for one business transition.
/// </summary>
/// <remarks>
/// Disposing returns the leases; it applies nothing. Changes are applied by <see cref="Complete"/>,
/// every one of them or none. The split is deliberate: <c>await using</c> disposes on the exception
/// path too, so applying at disposal would commit half a transition whenever one threw part way
/// through. Half a transition is cheap when it means a resource was counted wrong, and terminal
/// when it means two characters disagree about where they are -- this game has no reconnect, so
/// there is no path that resynchronises a client whose position the server no longer shares.
/// </remarks>
public interface IActiveCharacterWriteScope : IAsyncDisposable
{
    /// <summary>The characters held, in the order they were locked.</summary>
    IReadOnlyList<CharacterId> Held { get; }

    /// <summary>Reads, and stages a replacement for, a character this scope holds.</summary>
    ActiveCharacterState this[CharacterId characterId] { get; set; }

    /// <summary>Applies every staged change, or none of them.</summary>
    void Complete();
}

/// <summary>
/// Hands out mirrors of the active characters, and exclusive write access over a set of them.
/// </summary>
/// <remarks>
/// Separate from <see cref="IActiveCharacterDomainService"/> rather than added to it. The existing
/// interface serves login and character selection, which are account-scoped and already serialised
/// by the command bus; this one addresses characters directly because the operations that need it
/// -- a party paying an entry cost together -- span several accounts, which an account-keyed lock
/// cannot express. Keeping them apart leaves the proven path untouched while the new shape is
/// judged on the dungeon slice.
/// </remarks>
public interface IActiveCharacterAccessDomainService
{
    /// <summary>The character's state as of now, or null when it is not active.</summary>
    ActiveCharacterState? Read(CharacterId characterId);

    /// <summary>
    /// Mirrors of several characters, consistent with each other. Characters that are not active
    /// are absent rather than null, so a short answer is a readable one.
    /// </summary>
    IReadOnlyDictionary<CharacterId, ActiveCharacterState> Read(
        IReadOnlyCollection<CharacterId> characterIds);

    /// <summary>
    /// Takes exclusive write access to every named character. The set is de-duplicated and locked
    /// in one fixed order, so presenting the same characters in a different order cannot deadlock.
    /// </summary>
    ValueTask<IActiveCharacterWriteScope> AcquireAsync(
        IReadOnlyCollection<CharacterId> characterIds,
        CancellationToken cancellationToken);
}
