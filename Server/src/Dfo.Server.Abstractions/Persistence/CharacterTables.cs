using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Persistence;

/// <summary>One of an account's characters, as far as the roster is concerned.</summary>
public sealed record CharacterRosterRow(int CharacterId, int SlotIndex, int Level);

/// <summary>What a character looks like in a list of them.</summary>
public sealed record CharacterSummaryRow(
    int CharacterId, int SlotIndex, byte[] Name, int Job, int GrowType, int Level,
    long CloneTitleItemId, byte[] AppearanceBlob, int CreatedAtUnixSeconds);

/// <summary>How far along a character is.</summary>
public sealed record CharacterProgressRow(int Level, long Experience);

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

    /// <summary>Adds the character and says what id it was given.</summary>
    ValueTask<CharacterId> InsertAsync(
        AccountId accountId,
        string name,
        byte[] nameBytes,
        int job,
        int slotIndex,
        CancellationToken cancellationToken);

    /// <summary>Moves a character along. Says how many rows that touched.</summary>
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
/// One row type for both kinds of page. The duel pages used to have a type of their own with the
/// same five fields, which said nothing the table did not.
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
    /// <summary>Every ordinary skill the character has, and at what level.</summary>
    ValueTask<IReadOnlyList<CharacterSkillLevelRow>> ReadLevelsAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>The same skills with everything an entry needs, in the order it reads them.</summary>
    ValueTask<IReadOnlyList<CharacterSkillEntryRow>> ReadEntryAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    ValueTask InsertAsync(
        CharacterId characterId, int slot, int skillId, int level, CancellationToken cancellationToken);

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

