using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Persistence;

/// <summary>What the <c>characters</c> row contributes to an entry.</summary>
public sealed record CharacterEntryRow(
    int AccountId,
    int Job,
    int Level,
    long CloneTitleItemId,
    byte[] Appearance,
    long Experience,
    int ExtraEquipSlotStat,
    int UserState,
    int SkillTreeIndex,
    long NameTagItemId,
    long NameTagExpireTime,
    int ManageLevel,
    long GuildPowerWar,
    int GuildLevel);

/// <summary>One row of <c>character_dimensions</c>.</summary>
public sealed record DimensionRow(int SortOrder, long Key, int Value1, int Value2);

/// <summary>One row of <c>character_dark_knight_combo_skill_pages</c>.</summary>
public sealed record ComboPageRow(int PageIndex, byte[] Body);

/// <summary>One row of <c>character_knight_shield_deck</c>.</summary>
public sealed record ShieldSlotRow(int SlotIndex, int ShieldItemId);

/// <summary>One row of <c>character_active_quests</c>.</summary>
public sealed record ActiveQuestRow(int Slot, int QuestId, int TriggerValue);

/// <summary>One row of <c>character_invisible_flags</c>, which is where quest clears are kept.</summary>
public sealed record QuestClearFlagRow(int SlotIndex, int FlagValue);

/// <summary>One row of <c>character_daily_challenge_entries</c>.</summary>
public sealed record DailyChallengeEntryRow(
    int EntryIndex, int TrackLikeId, int ValueA, int ValueB);

/// <summary>One row of <c>character_daily_schedule_states</c>.</summary>
public sealed record DailyScheduleRow(
    int SortOrder, int ParamA, int ModeOrState, int ContentId, int ParamB);

/// <summary>One row of <c>character_dungeon_permissions</c>.</summary>
public sealed record DungeonPermissionRow(int SortOrder, int DungeonId, int ClearState);

/// <summary>One row of <c>character_new_titlebook</c>, before it is made sense of.</summary>
public sealed record TitleBookRow(int Category, int SlotIndex, byte[] Core);

/// <summary>One row of <c>character_collectbox_slots</c>.</summary>
public sealed record CollectionBoxRow(int BoxIndex, int SlotIndex, int ItemId);

/// <summary>One row of <c>character_pvp_missions</c>.</summary>
public sealed record PvpMissionRow(int SortOrder, int MissionId, int ProgressValue);

/// <summary>The two bonus point columns of <c>characters</c>.</summary>
public sealed record BonusPointRow(int BonusSp, int BonusTp);

/// <summary>One of a character's inbox rows in <c>mailbox_recipients</c>.</summary>
public sealed record MailboxRecipientRow(
    long MessageId, int ReadFlag, int SavedFlag, int ReceivedGoldFlag);

/// <summary>
/// One row of <c>mailbox_messages</c>, with the two values only the database can work out.
/// </summary>
/// <remarks>
/// Whether a message has expired and how long it has left are both measured against the
/// database's own clock, so they are worked out where that clock is. Everything else is the
/// column as stored.
/// </remarks>
public sealed record MailboxMessageRow(
    long MessageId,
    int SenderCharacterId,
    string SenderName,
    int MailType,
    int SourceProtocol,
    string Title,
    string Body,
    int Gold,
    int RemainingSeconds,
    int CreatedAtUnixSeconds,
    bool Live);

/// <summary>The entry-load reads of <c>characters</c>.</summary>
public interface ICharacterEntryTable
{
    /// <summary>The row an entry needs, or nothing when the character is gone.</summary>
    ValueTask<CharacterEntryRow?> FindEntryAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>
    /// The bonus points, counting a character that has been deleted.
    /// </summary>
    /// <remarks>
    /// Separate from the row above, and without its filter, because that is how it has always
    /// been read. Nothing depends on the difference today -- an entry has already refused a
    /// deleted character by the time this is asked -- but it is not this change's business to
    /// decide that.
    /// </remarks>
    ValueTask<BonusPointRow?> FindBonusPointsAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_hotkey_slots</c> table.</summary>
public interface ICharacterHotkeyTable
{
    ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_dimensions</c> table.</summary>
public interface ICharacterDimensionTable
{
    ValueTask<IReadOnlyList<DimensionRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_dark_knight_combo_skill_pages</c> table.</summary>
public interface ICharacterComboPageTable
{
    ValueTask<IReadOnlyList<ComboPageRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_knight_shield_deck</c> table.</summary>
public interface ICharacterShieldDeckTable
{
    ValueTask<IReadOnlyList<ShieldSlotRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_active_quests</c> table.</summary>
/// <remarks>
/// The entry projection's way in. The same rows are read and written inside a use case through
/// <c>IQuestDomainStore</c>, which does not share this reader's connection; what the two agree on, and
/// the one thing they do not, is written out there.
/// </remarks>
public interface ICharacterActiveQuestTable
{
    ValueTask<IReadOnlyList<ActiveQuestRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_quest_notify_selections</c> table.</summary>
public interface ICharacterQuestNotifyTable
{
    ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_invisible_flags</c> table.</summary>
public interface ICharacterQuestClearFlagTable
{
    ValueTask<IReadOnlyList<QuestClearFlagRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_growth_weapon_stages</c> table.</summary>
public interface ICharacterGrowthWeaponTable
{
    ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_daily_challenge_entries</c> table.</summary>
public interface ICharacterDailyChallengeEntryTable
{
    ValueTask<IReadOnlyList<DailyChallengeEntryRow>> ReadAsync(
        CharacterId characterId, int groupIndex, CancellationToken cancellationToken);
}

/// <summary>The <c>character_daily_challenge_tail_ids</c> table.</summary>
public interface ICharacterDailyChallengeTailTable
{
    ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_daily_schedule_states</c> table.</summary>
public interface ICharacterDailyScheduleTable
{
    ValueTask<IReadOnlyList<DailyScheduleRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_dungeon_permissions</c> table.</summary>
public interface ICharacterDungeonPermissionTable
{
    ValueTask<IReadOnlyList<DungeonPermissionRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_creatures</c> table.</summary>
public interface ICharacterCreatureTable
{
    ValueTask<IReadOnlyList<CharacterPetStorageRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_new_titlebook</c> table.</summary>
public interface ICharacterTitleBookTable
{
    ValueTask<IReadOnlyList<TitleBookRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_achievement_complete</c> table.</summary>
public interface ICharacterAchievementTable
{
    ValueTask<IReadOnlyList<CharacterAchievementStorageRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_collectbox_slots</c> table.</summary>
public interface ICharacterCollectionBoxTable
{
    ValueTask<IReadOnlyList<CollectionBoxRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_expert_job_recipes</c> table.</summary>
public interface ICharacterExpertRecipeTable
{
    ValueTask<IReadOnlyList<int>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_pvp_missions</c> table.</summary>
public interface ICharacterPvpMissionTable
{
    ValueTask<IReadOnlyList<PvpMissionRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>mailbox_recipients</c> table.</summary>
public interface IMailboxRecipientTable
{
    /// <summary>The character's inbox: not saved, not deleted, folder zero.</summary>
    ValueTask<IReadOnlyList<MailboxRecipientRow>> ReadInboxAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>mailbox_messages</c> table.</summary>
public interface IMailboxMessageTable
{
    /// <summary>
    /// Just enough of each of them to say which are still live and which order they came in.
    /// </summary>
    /// <remarks>
    /// Separate from the read below so that a character with a large inbox does not have every
    /// letter's text fetched to show twenty of them.
    /// </remarks>
    ValueTask<IReadOnlyList<MailboxHeaderRow>> ReadHeadersAsync(
        IReadOnlyList<long> messageIds, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<MailboxMessageRow>> ReadAsync(
        IReadOnlyList<long> messageIds, CancellationToken cancellationToken);
}

/// <summary>When a message arrived, and whether it is still worth showing.</summary>
public sealed record MailboxHeaderRow(long MessageId, int CreatedAtUnixSeconds, bool Live);

/// <summary>The <c>mailbox_attachments</c> table.</summary>
public interface IMailboxAttachmentTable
{
    ValueTask<IReadOnlyList<CharacterMailboxAttachmentStorageRow>> ReadAsync(
        long messageId, CancellationToken cancellationToken);
}

/// <summary>What a daily counter is at.</summary>
public interface ICharacterDailyCounterValueTable
{
    ValueTask<int?> FindValueAsync(
        CharacterId characterId, string counterKey, CancellationToken cancellationToken);
}

