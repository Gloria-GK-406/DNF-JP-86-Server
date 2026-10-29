using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterRepository
{
    /// <summary>The account's characters, without working out what any of them is wearing.</summary>
    /// <remarks>
    /// Every summary comes back with a null <c>Appearances</c>. Four callers ask this -- resolving
    /// a roster position, the create-character slot count, the delete-character name lookup, and
    /// the entry burst's own check that the selected character is on the roster -- and not one of
    /// them draws anything.
    /// <para>
    /// Those are four <b>call sites</b>, not four requests. CREATE_CHARACTER reaches this one for
    /// its slot count and then reaches
    /// <see cref="ListByAccountWithAppearanceAsync"/> for the roster it pushes afterwards.
    /// </para>
    /// </remarks>
    ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
        AccountId accountId,
        CancellationToken cancellationToken);

    /// <summary>The same characters, each with what it is currently drawn wearing.</summary>
    /// <remarks>
    /// Separate from <see cref="ListByAccountAsync"/> rather than folded into it, and the reason is
    /// not tidiness. Working the appearance out costs a read of everything every character on the
    /// account wears, and that read can refuse or simply fail. Folded in, that failure would reach
    /// entering the world, deleting a character, resolving a roster position, and the slot count a
    /// creation takes before it writes anything -- none of which draws a character. Whoever draws
    /// pays; whoever does not, does not.
    /// <para>
    /// <b>Creating a character does reach this, and not through that slot count.</b> A successful
    /// creation pushes a roster refresh afterwards
    /// (<c>GameCreateCharacterCommand</c>, directly asking the character domain after the
    /// creation work unit commits and its acknowledgement is registered), and that frame draws every character on the account, so it takes this listing
    /// like the character-selection request does. What the split spares a creation is the read
    /// standing between the request and the write; what it cannot spare it is the read behind the
    /// picture it then sends.
    /// </para>
    /// </remarks>
    ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountWithAppearanceAsync(
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

/// <summary>The character as an entity, addressed by identity.</summary>
/// <remarks>
/// Kept apart from <see cref="ICharacterRosterDomainService"/> so it sits below everything that acts
/// on a character: creation applies initialization, which levels the new character through this, and
/// the drawn roster asks appearance, which reads the character through this. Neither may reach back.
/// </remarks>
public interface ICharacterDomainService
{
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
}

/// <summary>An account's characters: the roster, selecting from it, naming, creating and deleting.</summary>
public interface ICharacterRosterDomainService
{
    /// <summary>The roster, for callers that need to know who is on it and not how they look.</summary>
    ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
        AccountId accountId,
        CancellationToken cancellationToken);

    /// <summary>The roster for the screen that draws it, appearances worked out.</summary>
    /// <remarks>
    /// One caller names it, and two request paths reach that caller: the character-selection
    /// frame the client asks for, and the roster refresh pushed after a character is created. Both
    /// draw, so both belong here. See
    /// <see cref="ICharacterRepository.ListByAccountWithAppearanceAsync"/> for why the two listings
    /// are apart, and why "one caller" is the wrong thing to count -- what crosses the command bus
    /// is the command's name, not this method's.
    /// </remarks>
    ValueTask<AccountCharacterRoster> GetAccountRosterWithAppearanceAsync(
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
