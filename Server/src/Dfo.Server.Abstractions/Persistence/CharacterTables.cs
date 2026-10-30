using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Persistence;

/// <summary>One of an account's characters, as far as the roster is concerned.</summary>
public sealed record CharacterRosterRow(int CharacterId, int SlotIndex, int Level);

/// <summary>What a character looks like in a list of them.</summary>
public sealed record CharacterSummaryRow(
    int CharacterId, int SlotIndex, byte[] Name, int Job, int GrowType, int Level,
    long CloneTitleItemId, int CreatedAtUnixSeconds)
{
    /// <summary>Persisted location, populated by the selection lookup.</summary>
    public CharacterPositionState Position { get; init; } = new(0, 0, 0, 0, 5);
    public bool TutorialCompleted { get; init; }
    public byte LastInnTownId { get; init; }
}

/// <summary>How far along a character is.</summary>
public sealed record CharacterProgressRow(int Level, long Experience);

/// <summary>What a character is, and how far along, in the terms the archive prices by.</summary>
/// <remarks>
/// The three columns the archive needs before it can say what a character is entitled to. They
/// travel together because every one of those questions needs all three at once, and reading them
/// apart would let two of them come from different moments.
/// </remarks>
public sealed record CharacterStandingRow(int Job, int GrowType, int Level);

/// <summary>Which character this is, said without saying how far it has got.</summary>
/// <remarks>
/// The four columns that name a character: where it sits on the roster screen, what it is called,
/// what it took up and how it grew. They travel together because they are asked together -- one
/// question about one character at one moment -- and reading them apart would let a name and a slot
/// come from two.
/// </remarks>
public sealed record CharacterIdentityRow(int SlotIndex, byte[] Name, int Job, int GrowType);

/// <summary>One row of <c>character_buy_restrict_items</c>.</summary>
public sealed record CharacterBuyRestrictRow(
    int CharacterId, int SortOrder, int EntryId, int SentinelOrValue, int Flag);

/// <summary>One row of <c>character_mercenary_support</c>.</summary>
public sealed record CharacterMercenarySupportRow(
    int OwnerCharacterId, int Slot, int SupportCharacterId, int SkillId, int StrikerSkillId);

/// <summary>One row of <c>character_skills</c>, as far as levelling is concerned.</summary>
public sealed record CharacterSkillLevelRow(int PageIndex, int Slot, int SkillId, int Level);

/// <summary>
/// The <c>characters</c> table.
/// </summary>
/// <remarks>
/// One table, so one repository, however many different questions get asked of it. What none of
/// these do is decide anything: whether a name may be taken, whether a slot is free, what a new
/// character starts with -- those are answered above, out of what these return.
/// </remarks>
public interface ICharacterTable
{
    /// <summary>The account's characters that have not been deleted, in slot order.</summary>
    ValueTask<IReadOnlyList<CharacterRosterRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken);

    /// <summary>The same characters, with everything a roster listing shows.</summary>
    ValueTask<IReadOnlyList<CharacterSummaryRow>> ReadSummariesByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken);

    /// <summary>The character in that slot of that account, or nothing when the slot is free.</summary>
    ValueTask<CharacterSummaryRow?> FindBySlotAsync(
        AccountId accountId, int slotIndex, CancellationToken cancellationToken);

    /// <summary>Whether any character that has not been deleted already has that name.</summary>
    ValueTask<bool> NameIsTakenAsync(byte[] name, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this character is that account's, counting one that has been deleted.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="FindOwnerAsync"/>, which ignores deleted characters. The
    /// inventory paths ask this one: what they are guarding against is being handed someone
    /// else's character, and a deleted character of the right account is a different problem
    /// with a different answer.
    /// </remarks>
    ValueTask<bool> BelongsToAsync(
        CharacterId characterId, AccountId accountId, CancellationToken cancellationToken);

    /// <summary>Which account a character belongs to, or nothing when it is gone.</summary>
    ValueTask<AccountId?> FindOwnerAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Where a character has got to, or nothing when it is not there.</summary>
    ValueTask<CharacterProgressRow?> FindProgressAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>
    /// What the character is and what level it has reached, or nothing when it is gone.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose, and by character rather than by slot. The job and the grow type were
    /// only ever readable here through a roster read keyed on an account and a slot, which is a
    /// question nobody outside the roster has the answer to; anything holding a character's own
    /// identifier had to go without them. Deleted characters are left out of this read. The
    /// neighbouring <see cref="FindIdentityAsync"/> keeps them instead, because it is asked as one
    /// question with <see cref="FindProgressAsync"/> and the two must see the same rows; the
    /// reason is written out there.
    /// </remarks>
    ValueTask<CharacterStandingRow?> FindStandingAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>
    /// Which slot the character sits in, what it is called and what it is, or nothing when there is
    /// no such row.
    /// </summary>
    /// <remarks>
    /// By character rather than by account and slot, for the reason
    /// <see cref="FindStandingAsync"/> is: the slot and the name were only readable through a
    /// roster read keyed on an account, which is a question nothing holding a character's own
    /// identifier can answer -- so a character asked what it is had to be told by whoever last
    /// looked it up.
    /// <para>
    /// A character marked deleted still answers, which is <see cref="FindProgressAsync"/>'s
    /// condition rather than <see cref="FindStandingAsync"/>'s. The two narrow reads are asked
    /// together as one question -- what the character is, and how far along it is -- and a caller
    /// that reads either absence as "nothing to judge against" needs them to see the same rows. A
    /// consumer that must tell a deleted character apart filters on its own side, where the
    /// exclusion can be read next to the decision it is for.
    /// </para>
    /// <para>
    /// Distinct from <see cref="FindStandingAsync"/>, which overlaps it on two columns and is a
    /// different question: that one prices what a character is entitled to and needs the level
    /// beside the job, this one says who the character is and must not depend on how far along it
    /// is.
    /// </para>
    /// </remarks>
    ValueTask<CharacterIdentityRow?> FindIdentityAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Adds the character and says what id it was given.</summary>
    ValueTask<CharacterId> InsertAsync(
        AccountId accountId,
        string name,
        byte[] nameBytes,
        int job,
        int slotIndex,
        CancellationToken cancellationToken);

    /// <summary>Moves a character along. Says how many rows that touched.</summary>
    ValueTask<int> SetGrowTypeAsync(
        CharacterId characterId, int expectedGrowType, int growType, CancellationToken cancellationToken);

    ValueTask<int> SetProgressAsync(
        CharacterId characterId, int level, long experience, CancellationToken cancellationToken);

    /// <summary>Marks the character deleted. False when it already was.</summary>
    ValueTask<bool> MarkDeletedAsync(CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>What each character may not buy.</summary>
public interface ICharacterBuyRestrictTable
{
    ValueTask<IReadOnlyList<CharacterBuyRestrictRow>> ReadByCharactersAsync(
        IReadOnlyList<int> characterIds, CancellationToken cancellationToken);
}

/// <summary>Which character supports which, and with what.</summary>
public interface ICharacterMercenarySupportTable
{
    ValueTask<IReadOnlyList<CharacterMercenarySupportRow>> ReadByOwnersAsync(
        IReadOnlyList<int> ownerCharacterIds, CancellationToken cancellationToken);
}

/// <summary>One skill of a character, with whatever else was stored beside it.</summary>
/// <remarks>
/// One row type for both kinds of page: a separate duel type would carry the same five fields.
/// </remarks>
public sealed record CharacterSkillEntryRow(
    int PageIndex, int Slot, int SkillId, int Level, byte[] ExtraValues);

/// <summary>The <c>character_skills</c> table.</summary>
/// <remarks>
/// One table with two sets of pages in it, and one interface over it. Which set a row belongs to is
/// a column, not a table, and the two sets never share a row: the ordinary pages are written by
/// spending skill points, the duel pages by seeding them when a duel channel asks. Every method
/// here names the set it works on, because a read that forgot would hand one set the other's rows.
/// </remarks>
public interface ICharacterSkillTable
{
    ValueTask SwapSlotsAsync(CharacterId characterId, int pageIndex, int first, int second, CancellationToken cancellationToken);

    /// <summary>Every ordinary skill the character has, and at what level.</summary>
    ValueTask<IReadOnlyList<CharacterSkillLevelRow>> ReadLevelsAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>The same skills with everything an entry needs, in the order it reads them.</summary>
    ValueTask<IReadOnlyList<CharacterSkillEntryRow>> ReadEntryAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <param name="pageIndex">
    /// Which skill tree the row belongs to. A character is created holding two, and while the
    /// second is locked it is a copy of the first; see <c>protocol/game/server-to-client/NOTI-0x0013.md</c>.
    /// </param>
    ValueTask InsertAsync(
        CharacterId characterId, int pageIndex, int slot, int skillId, int level,
        CancellationToken cancellationToken);

    /// <summary>Puts one ordinary skill at that level. Says how many rows that touched.</summary>
    ValueTask<int> SetLevelAsync(
        CharacterId characterId, int pageIndex, int slot, int level, CancellationToken cancellationToken);

    /// <summary>Takes one ordinary skill away. Says how many rows that touched.</summary>
    ValueTask<int> RemoveAsync(
        CharacterId characterId, int pageIndex, int slot, CancellationToken cancellationToken);

    /// <summary>The duel pages, in the order they are read.</summary>
    ValueTask<IReadOnlyList<CharacterSkillEntryRow>> ReadDuelSkillsAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Puts one skill on a duel page.</summary>
    ValueTask InsertDuelSkillAsync(
        CharacterId characterId,
        int pageIndex,
        int slot,
        int skillId,
        int level,
        byte[] extraValues,
        CancellationToken cancellationToken);
}
