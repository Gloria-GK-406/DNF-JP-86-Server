using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

public sealed record CharacterProfileStorageRecord(
    byte Job,
    byte Level,
    uint CloneTitleItemId,
    byte[] AppearanceBlob,
    CharacterConditionStorageRecord Condition,
    CharacterSettingsStorageRecord Settings,
    CharacterPvpStandingStorageRecord Pvp,
    CharacterExpertJobStorageRecord ExpertJob,
    CharacterDerivedStatsStorageRecord DerivedStats,
    CharacterGuildStandingStorageRecord Guild,
    int SkillTreeIndex,
    long NameTagItemId,
    long NameTagExpireTime,
    int ManageLevel,
    IReadOnlyList<ushort> HotkeySlots,
    byte[]? CharacterOptionBlob,
    long CharacterExperience,
    int ExtraEquipmentSlotState,
    IReadOnlyList<(int SortOrder, long Key, int Value1, int Value2)> Dimensions,
    (int Flag1, int Flag2, int Flag3, int Flag4) DimensionFlags,
    byte PvpGrade = 0,
    byte PvpRatingGrade = 0,
    byte UserState = 0);

public sealed record CharacterSkillStorageRow(
    int PageIndex,
    short Slot,
    int SkillId,
    int Level,
    byte[] ExtraValues);

public sealed record CharacterSkillStorageRecord(
    byte Job,
    int BonusSp,
    int BonusTp,
    IReadOnlyList<CharacterSkillStorageRow> Skills,
    IReadOnlyList<(byte PageIndex, byte[] Body)> DarkKnightComboPages,
    IReadOnlyList<(byte SlotIndex, int ShieldItemId)> KnightShieldSlots);

/// <summary>
/// Loads the independently persisted fair-PvP skill profile.  Absence is distinct from
/// an initialized empty profile and must therefore remain observable to entry policy.
/// </summary>
public interface ICharacterPvpSkillProfileRepository
{
    ValueTask<CharacterSkillsEntryState> ReadAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken);

    ValueTask<CharacterSkillsEntryState> LoadOrInitializeAsync(
        AccountId accountId,
        CharacterId characterId,
        CharacterSkillsEntryState initialProfile,
        CancellationToken cancellationToken);
}

public sealed record DungeonPersistentEffectRecoveryOutcome(
    int CommittedCount,
    int DeadLetterCount,
    int FailedCount,
    int RemainingCount)
{
    public bool RequiresReload => CommittedCount > 0;
}

/// <summary>Recovers only the legacy typed dungeon-effect outbox for one owned character.</summary>
public interface IDungeonPersistentEffectRecoveryRepository
{
    ValueTask<DungeonPersistentEffectRecoveryOutcome> RecoverAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}

public interface IExpertSkillContractReconciliationRepository
{
    ValueTask<bool> ReconcileAsync(AccountId accountId, CharacterId characterId,
        IReadOnlySet<int> overSkillPremiumTypes,
        IReadOnlyDictionary<int, Dfo.Server.Abstractions.Content.SkillPointCostDefinition> definitions,
        IReadOnlyDictionary<int, byte> freeBaseline,
        CancellationToken cancellationToken);
}

public sealed record CharacterActiveQuestStorageRow(short Slot, int QuestId, int TriggerValue);

public sealed record QuestTriggerUpdate(short Slot, int QuestId, int Before, int After);

public sealed record PetRevivalMutation(long CreatureKey, int Before, int After);

public sealed record CharacterQuestStorageRecord(
    IReadOnlyList<CharacterActiveQuestStorageRow> ActiveQuests,
    IReadOnlyList<int> NotificationQuestIds,
    IReadOnlyList<(int SlotIndex, byte Value)> ClearFlags);

public sealed record CharacterDailyChallengeStorageGroup(
    byte GroupIndex,
    int GroupId,
    IReadOnlyList<(int EntryIndex, int TrackId, int ValueA, int ValueB)> Entries,
    bool Claimed);

public sealed record CharacterProgressionStorageRecord(
    byte Level,
    IReadOnlyList<int> GrowthWeaponStages,
    int CubeType,
    int CubeGrade,
    int ChampionBreakKey,
    int ChampionBreakMode,
    int ChampionBreakValue,
    int GoldLimitUpgradeLevel,
    IReadOnlyList<CharacterDailyChallengeStorageGroup> DailyChallenges,
    IReadOnlyList<int> DailyChallengeTailIds,
    IReadOnlyList<(int SortOrder, int ParamA, int ModeOrState, int ContentId, int ParamB)> Schedules,
    IReadOnlyList<(int SortOrder, int DungeonId, int ClearState)> Dungeons,
    IReadOnlyList<(byte SlotIndex, int UsedCount)> PremiumUsageSlots,
    bool DailyReviveCoinClaimed);

public sealed record CharacterPetStorageRow(
    int SortOrder,
    long CreatureKey,
    int Field04,
    int ModeFlag,
    int ProgressValue,
    int Mode1Field0A,
    int Mode1Field0B,
    int FieldAfterValue,
    byte[] CreatureText,
    int TailFlag);

public sealed record CharacterTitleBookStorageRow(byte Category, int SlotIndex, byte[] ItemCore);

public sealed record CharacterAchievementStorageRow(
    int SortOrder,
    int AchievementId,
    int P1,
    int P2,
    int P3,
    int P4);

public sealed record CharacterMailboxStorageRecord(
    int TotalCount,
    IReadOnlyList<CharacterMailboxMessageStorageRow> PageMessages);

public sealed record CharacterMailboxAttachmentStorageRow(
    long AttachmentId,
    int Ordinal,
    int ItemType,
    int SourceListType,
    int SourceSlotIndex,
    long SourceItemUid,
    int ItemTemplateId,
    string ItemKind,
    int ItemCount,
    int InstanceValue,
    int Durability,
    int SealFlag,
    int OptionValue,
    int ExpireTime,
    int Marker16,
    int PetSerialOrHandle,
    byte[] ItemCore,
    string DetailJson);

public sealed record CharacterMailboxMessageStorageRow(
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
    int LetterState,
    IReadOnlyList<CharacterMailboxAttachmentStorageRow> Attachments)
{
    public IReadOnlyList<MailGoldAttachmentState> GoldAttachments { get; init; } = [];
}

public sealed record CharacterFeatureStorageRecord(
    IReadOnlyList<CharacterPetStorageRow> Pets,
    IReadOnlyList<CharacterTitleBookStorageRow> TitleBooks,
    IReadOnlyList<CharacterAchievementStorageRow> Achievements,
    IReadOnlyList<(int BoxIndex, int SlotIndex, int ItemId)> CollectionSlots,
    (int ProfessionType, int Experience, int GiveupCount, int DisjointMachineGrade,
        int DisjointMachineEndurance, int EnchanterEndurance,
        IReadOnlyList<int> RecipeIds) ExpertJob,
    CharacterMailboxStorageRecord Mailbox,
    IReadOnlyList<(int SortOrder, int MissionId, int Progress)> PvpMissions);

public sealed record CharacterEntryStorageRecord(
    AccountId AccountId,
    CharacterId CharacterId,
    CharacterProfileStorageRecord Profile,
    CharacterSkillStorageRecord Skill,
    CharacterQuestStorageRecord Quest,
    CharacterProgressionStorageRecord Progression,
    CharacterFeatureStorageRecord Features);

public interface ICharacterEntryStateRepository
{
    ValueTask<CharacterEntryStorageRecord> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}

public interface ICharacterProfileEntryProjection
{
    (CharacterPublicEntryState Public, CharacterSettingsEntryState Settings) Build(
        CharacterProfileStorageRecord storage);
}

public interface ICharacterSkillQuestEntryProjection
{
    (CharacterSkillsEntryState Skills, CharacterQuestEntryState Quests) Build(
        CharacterSkillStorageRecord skills,
        CharacterQuestStorageRecord quests);
}

public interface ICharacterProgressionEntryProjection
{
    CharacterProgressionEntryState Build(CharacterProgressionStorageRecord storage);
}

public interface ICharacterFeatureEntryProjection
{
    CharacterFeaturesEntryState Build(CharacterFeatureStorageRecord storage);
}

public interface ICharacterEntryStateDomainService
{
    ValueTask<CharacterEntryState> LoadAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken);
}
