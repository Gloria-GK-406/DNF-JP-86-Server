using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Persistence;

/// <summary>One of an account's characters, as far as the roster is concerned.</summary>
public sealed record CharacterRosterRow(int CharacterId, int SlotIndex, int Level);

/// <summary>What a character looks like in a list of them.</summary>
public sealed record CharacterSummaryRow(
    int CharacterId, int SlotIndex, byte[] Name, int Job, int GrowType, int Level,
    long CloneTitleItemId);

/// <summary>How far along a character is.</summary>
public sealed record CharacterProgressRow(int Level, long Experience);

/// <summary>One row of <c>character_init_flags</c>, for the part an account cares about.</summary>
public sealed record CharacterPcRoomRow(int CharacterId, int PcRoomState);

/// <summary>One row of <c>character_buy_restrict_items</c>.</summary>
public sealed record CharacterBuyRestrictRow(
    int CharacterId, int SortOrder, int EntryId, int SentinelOrValue, int Flag);

/// <summary>One row of <c>character_mercenary_support</c>.</summary>
public sealed record CharacterMercenarySupportRow(
    int OwnerCharacterId, int Slot, int SupportCharacterId, int SkillId, int StrikerSkillId);

/// <summary>One row of <c>character_skills</c>, as far as levelling is concerned.</summary>
public sealed record CharacterSkillLevelRow(int PageIndex, int Slot, int SkillId, int Level);

/// <summary>One row of <c>character_pvp_skills</c>.</summary>
public sealed record CharacterPvpSkillRow(
    int PageIndex, int Slot, int SkillId, int Level, byte[] ExtraValues);

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

/// <summary>The <c>character_init_flags</c> table.</summary>
public interface ICharacterInitFlagTable
{
    /// <summary>
    /// One row per named character that has one. A character with no row is simply absent, which
    /// is a fact the caller decides what to make of.
    /// </summary>
    ValueTask<IReadOnlyList<CharacterPcRoomRow>> ReadByCharactersAsync(
        IReadOnlyList<int> characterIds, CancellationToken cancellationToken);

    /// <summary>Gives the character a row unless it has one.</summary>
    ValueTask InsertIfAbsentAsync(CharacterId characterId, CancellationToken cancellationToken);
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
public sealed record CharacterSkillEntryRow(
    int PageIndex, int Slot, int SkillId, int Level, byte[] ExtraValues);

/// <summary>The <c>character_skills</c> table.</summary>
public interface ICharacterSkillTable
{
    /// <summary>Every skill the character has, and at what level.</summary>
    ValueTask<IReadOnlyList<CharacterSkillLevelRow>> ReadLevelsAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>The same skills with everything an entry needs, in the order it reads them.</summary>
    ValueTask<IReadOnlyList<CharacterSkillEntryRow>> ReadEntryAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    ValueTask InsertAsync(
        CharacterId characterId, int slot, int skillId, int level, CancellationToken cancellationToken);

    /// <summary>Puts one skill at that level. Says how many rows that touched.</summary>
    ValueTask<int> SetLevelAsync(
        CharacterId characterId, int pageIndex, int slot, int level, CancellationToken cancellationToken);

    /// <summary>Takes one skill away. Says how many rows that touched.</summary>
    ValueTask<int> RemoveAsync(
        CharacterId characterId, int pageIndex, int slot, CancellationToken cancellationToken);
}

/// <summary>The <c>character_pvp_skills</c> table.</summary>
public interface ICharacterPvpSkillTable
{
    ValueTask<IReadOnlyList<CharacterPvpSkillRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    ValueTask InsertAsync(
        CharacterId characterId,
        int pageIndex,
        int slot,
        int skillId,
        int level,
        byte[] extraValues,
        CancellationToken cancellationToken);
}

/// <summary>
/// The <c>character_pvp_skill_state</c> table.
/// </summary>
/// <remarks>
/// One row per character, and the row's presence is the whole of what it says: this character's
/// duelling skills have been set up. It holds nothing else.
/// </remarks>
public interface ICharacterPvpSkillStateTable
{
    ValueTask<bool> ExistsAsync(CharacterId characterId, CancellationToken cancellationToken);

    ValueTask InsertAsync(CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_subtype0_fields</c> table.</summary>
public interface ICharacterSubtype0Table
{
    ValueTask InsertIfAbsentAsync(CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_subtype1_fields</c> table.</summary>
public interface ICharacterSubtype1Table
{
    ValueTask InsertIfAbsentAsync(
        CharacterId characterId, CharacterBaseStats stats, int currentHpMpPercent,
        CancellationToken cancellationToken);
}
