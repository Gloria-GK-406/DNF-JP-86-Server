using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Data.Characters;

public readonly record struct CharacterId(int Value)
{
    public override string ToString() => Value.ToString();
}

public sealed record CharacterSummary(
    CharacterId CharacterId,
    ushort SlotIndex,
    byte[] Name,
    byte Job,
    byte GrowType,
    byte Level,
    uint CloneTitleItemId);

public sealed record AccountCharacterRoster(
    AccountId AccountId,
    ushort SlotLimit,
    IReadOnlyList<CharacterSummary> Characters);

public sealed record CharacterIdentityState(
    CharacterId CharacterId,
    ushort SlotIndex,
    byte[] Name,
    byte Job,
    byte GrowType);

public sealed record CharacterProgressionState(byte Level, uint Experience);

public sealed record CharacterFatigueState(ushort Total, ushort Used);

public sealed record CharacterPositionState(
    byte TownId,
    byte AreaId,
    short X,
    short Y,
    byte Direction);

public sealed record DarkKnightComboState(IReadOnlyList<uint> SkillIds);

public sealed record ActiveCharacterState(
    CharacterIdentityState Identity,
    CharacterProgressionState Progression,
    CharacterFatigueState Fatigue,
    CharacterPositionState Position,
    DateTimeOffset CreatedAt,
    DarkKnightComboState? DarkKnightCombo);

public sealed record CharacterAppearanceCompatibilityState(
    byte FormatVersion,
    byte[] EncodedEntries);

/// <summary>Persisted public identity fields that are separate from entry visibility bits.</summary>
public sealed record CharacterPublicIdentityEntryState(
    byte PvpGrade,
    byte PvpRatingGrade,
    byte UserState);

public sealed record CharacterSubtype0EntryState(
    byte Stamina,
    uint FatiguePenalty,
    bool EventCharacter,
    bool PrivateStore,
    byte ExpertJobType,
    uint ExpertJobExperience,
    bool HardcoreMode,
    bool HardcoreDead,
    ushort HardcoreDeathCount,
    uint UserStateBits,
    uint ChatBanEndTime,
    ushort MoodValue,
    uint PvpRankPoint,
    byte CreatureField1 = 0,
    byte CreatureField2 = 0,
    byte CreatureField3 = 0,
    byte CreatureField4 = 0,
    byte[] CreatureBuffer = null!,
    uint PcRoomId = 0x00010001,
    bool PremiumPcRoom = false,
    byte ServerGroupId = 0,
    uint BlackCount = 0,
    byte GuildLevel = 0,
    uint ChaosPoint = 0,
    byte DisguiseKind = 0,
    bool Disguised = false,
    ushort FatigueUpdate = 0,
    byte ReturnUserFlag = 1,
    ushort ChannelDisplayMode = 0,
    byte ChannelType = 0,
    bool ReturnUser = false,
    bool LinkSlotEnabled = false,
    byte LinkTypeA = 0,
    byte LinkTypeB = 0,
    ushort EmotionIndex = 0,
    byte ActionByte = 0,
    ushort FatigueDisplayUpdate = 0,
    byte CostumeFlag = 0,
    byte AuraFlag = 0,
    byte PetDisplayFlag = 0,
    byte TitleDisplayFlag = 0,
    uint PvpStatA = 0,
    byte PvpWinStreak = 0,
    byte PvpLoseStreak = 0,
    byte TrailingByte = 0);

public sealed record CharacterSubtype1EntryState(
    int HpMaximum,
    int MpMaximum,
    int PhysicalAttack,
    int PhysicalDefense,
    int MagicalAttack,
    int MagicalDefense,
    int MoveSpeed,
    int AttackSpeed,
    int CastSpeed,
    int InventoryLimit,
    int SkillTreeIndex,
    int Progress1,
    int Progress2,
    short FireResistance = 0,
    short WaterResistance = 0,
    short DarkResistance = 0,
    short LightResistance = 0,
    ushort HpRegenSpeed = 0,
    ushort MpRegenSpeed = 0,
    ushort HitRecovery = 0,
    ushort JumpPower = 0,
    uint Weight = 0,
    byte StatLevel = 0,
    uint NameTagItemId = 0,
    uint NameTagExpireTime = 0,
    byte EquippedCreatureLevel = 0,
    int EquipListTrailing = 0,
    byte FlagByte = 0,
    uint GuildPowerWar = 0,
    uint ServerTimestamp = 0,
    ushort QuestShopCount = 0,
    uint CharacterExperience = 0,
    byte ExtraEquipmentSlotState = 0,
    IReadOnlyList<CharacterDimensionEntryState> Dimensions = null!,
    CharacterDimensionFlagsState DimensionFlags = null!,
    byte ManageLevel = 0,
    IReadOnlyList<CharacterPvpResultState> PvpResults = null!,
    IReadOnlyList<uint> SpecialRewardQuestIds = null!);

public sealed record CharacterDimensionEntryState(uint Key, byte Value1, byte Value2);

public sealed record CharacterDimensionFlagsState(byte Flag1, byte Flag2, byte Flag3, byte Flag4);

public sealed record CharacterPvpResultState(uint Value32, ushort Value16A, ushort Value16B);

public sealed record CharacterPublicEntryState(
    CharacterSubtype0EntryState Subtype0,
    CharacterSubtype1EntryState Subtype1,
    CharacterAppearanceCompatibilityState Appearance,
    uint VisibilityBits,
    uint CloneTitleItemId,
    CharacterPublicIdentityEntryState? Identity = null);

public sealed record CharacterSkillState(
    short Slot,
    uint SkillId,
    byte Level,
    IReadOnlyList<byte> ExtraValues);

public sealed record CharacterSkillPageState(
    byte PageIndex,
    IReadOnlyList<CharacterSkillState> Skills);

public sealed record CharacterSkillPointSlotState(byte SlotIndex, int Value);

public sealed record CharacterSkillPointBasisState(int BonusSp, int BonusTp);

public sealed record CharacterSkillProtocolMirrorState(
    ushort Page0Sp,
    ushort Page1Sp,
    ushort Page0Tp,
    ushort Page1Tp);

public sealed record DarkKnightComboEntryState(
    byte PageIndex,
    byte[] EncodedBody);

public sealed record CharacterSkillsEntryState(
    IReadOnlyList<CharacterSkillPageState> Pages,
    IReadOnlyList<CharacterSkillPointSlotState> SkillPointSlots,
    CharacterSkillPointBasisState SkillPointBasis,
    IReadOnlyList<DarkKnightComboEntryState>? DarkKnightCombos,
    IReadOnlyList<int>? KnightShieldDeck,
    CharacterSkillProtocolMirrorState? ProtocolMirror = null);

public sealed record CharacterQuestProgressState(
    short Slot,
    int QuestId,
    int TriggerValue);

public sealed record CharacterQuestEntryState(
    IReadOnlyList<CharacterQuestProgressState> ActiveQuests,
    IReadOnlyList<int> ClearedQuestIds,
    IReadOnlyList<int> NotificationQuestIds,
    byte[] VisibilityFlags);

public sealed record CharacterDungeonProgressState(
    int DungeonId,
    byte State);

public sealed record CharacterDailyChallengeItemState(
    uint TrackId,
    uint ValueA,
    uint ValueB);

public sealed record CharacterDailyChallengeGroupState(
    byte GroupIndex,
    uint GroupId,
    IReadOnlyList<CharacterDailyChallengeItemState> Entries,
    bool RewardClaimed);

public sealed record CharacterScheduleCompatibilityState(
    byte SlotIndex,
    int Value0,
    int Value1,
    int Value2,
    int Value3);

public sealed record CharacterPremiumUsageSlotState(byte SlotIndex, int UsedCount);

public sealed record CharacterProgressionEntryState(
    IReadOnlyList<byte> GrowthWeaponStages,
    byte CubeType,
    byte CubeGrade,
    int ChampionBreakKey,
    byte ChampionBreakMode,
    int ChampionBreakValue,
    byte GoldLimitUpgradeLevel,
    ushort FatigueAccelerationValue,
    byte FatigueAccelerationState,
    bool DailyReviveCoinClaimed,
    uint DailyChallengeCharacterLevel,
    IReadOnlyList<CharacterDailyChallengeGroupState> DailyChallenges,
    byte[] DailyChallengeClaimFlags,
    IReadOnlyList<uint> DailyChallengeTailIds,
    IReadOnlyList<CharacterScheduleCompatibilityState> ScheduleCompatibility,
    IReadOnlyList<CharacterDungeonProgressState> Dungeons,
    IReadOnlyList<CharacterPremiumUsageSlotState> PremiumUsageSlots);

public sealed record CharacterSettingsEntryState(
    IReadOnlyList<ushort> HotkeySlots,
    byte[]? OptionBitmap,
    uint VisibilityBits);

public sealed record CharacterPetRuntimeState(
    long PetKey,
    int Field04,
    byte ModeFlag,
    int ProgressValue,
    int Mode1Field0A,
    int Mode1Field0B,
    int FieldAfterValue,
    byte[] CreatureText,
    byte TailFlag);

public sealed record CharacterTitleBookEntryState(
    ushort SlotIndex,
    int ItemId,
    int Value,
    byte Attribute,
    ushort Durability,
    byte SealFlag,
    int EnchantIndex,
    byte EnchantUpgradeCount,
    byte AmplifyType,
    ushort AmplifyValue);

public sealed record CharacterTitleBookCategoryState(
    byte Category,
    byte InfoType,
    ushort OwnerId16,
    IReadOnlyList<CharacterTitleBookEntryState> Entries);

public sealed record CharacterAchievementProgressState(
    int AchievementId,
    ushort P1,
    ushort P2,
    ushort P3,
    ushort P4);

public sealed record CharacterCollectionBoxSlotState(
    int SlotIndex,
    int ItemId);

public sealed record CharacterCollectionBoxProgressState(
    byte BoxIndex,
    IReadOnlyList<CharacterCollectionBoxSlotState> Slots,
    CharacterCollectionBoxProtocolState? Protocol = null);

public sealed record CharacterCollectionBoxProtocolState(
    uint RemainingSeconds,
    byte StatusFlags);

public sealed record CharacterCollectionsEntryState(
    IReadOnlyList<CharacterTitleBookCategoryState> TitleBooks,
    IReadOnlyList<CharacterAchievementProgressState> Achievements,
    IReadOnlyList<CharacterCollectionBoxProgressState> CollectionBoxes);

public sealed record CharacterExpertJobEntryState(
    byte ProfessionType,
    int Experience,
    ushort GiveupCount,
    int DisjointMachineGrade,
    int DisjointMachineEndurance,
    int EnchanterEndurance,
    IReadOnlyList<int> LearnedRecipeIds,
    CharacterExpertProtocolState? Protocol = null);

public sealed record CharacterExpertProtocolState(
    IReadOnlyList<byte> CardQualificationLevels,
    int EnchanterLevel);

public sealed record CharacterMailboxAttachmentEntryState(
    long AttachmentId,
    int Ordinal,
    byte ItemType,
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

public sealed record CharacterMailboxMessageEntryState(
    long MessageId,
    int SenderCharacterId,
    byte[] SenderName,
    int MailType,
    int SourceProtocol,
    byte[] Title,
    byte[] Body,
    int Gold,
    int RemainingSeconds,
    int CreatedAtUnixSeconds,
    byte LetterState,
    IReadOnlyList<CharacterMailboxAttachmentEntryState> Attachments);

public sealed record CharacterMailboxEntryState(
    int NotLoadedCount,
    IReadOnlyList<CharacterMailboxMessageEntryState> Messages);

public sealed record CharacterPvpMissionEntryState(int MissionId, int Progress);

public sealed record CharacterFeaturesEntryState(
    IReadOnlyList<CharacterPetRuntimeState> Pets,
    CharacterCollectionsEntryState Collections,
    CharacterExpertJobEntryState ExpertJob,
    CharacterMailboxEntryState Mailbox,
    IReadOnlyList<CharacterPvpMissionEntryState> PvpMissions);

public sealed record CharacterEntryState(
    CharacterId CharacterId,
    CharacterPublicEntryState Public,
    CharacterSettingsEntryState Settings,
    CharacterSkillsEntryState Skills,
    CharacterQuestEntryState Quests,
    CharacterProgressionEntryState Progression,
    CharacterFeaturesEntryState Features);
