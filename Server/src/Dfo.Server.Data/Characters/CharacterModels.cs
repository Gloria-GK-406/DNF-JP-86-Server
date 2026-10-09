using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Data.Characters;

public readonly record struct CharacterId(int Value)
{
    public override string ToString() => Value.ToString();
}

/// <summary>One of an account's characters, as a listing of them names it.</summary>
/// <param name="Appearances">
/// What this character is drawn wearing, or <c>null</c> when the listing was not asked to work it
/// out.
/// <para>
/// <b><c>null</c> and empty are two different answers, and the difference is load-bearing.</b>
/// Empty means "asked, and this character is drawn wearing nothing". Null means "nobody asked" --
/// which is what every listing except the one feeding the character-selection frame gets, because
/// working it out costs an account-scoped read of everything every character on the account wears.
/// The one encoder that draws these refuses null rather than drawing an undressed character, so a
/// caller that takes the cheap listing to a screen that draws is told so instead of quietly
/// reproducing the defect this distinction exists because of.
/// </para>
/// </param>
public sealed record CharacterSummary(
    CharacterId CharacterId,
    ushort SlotIndex,
    byte[] Name,
    byte Job,
    byte GrowType,
    byte Level,
    uint CloneTitleItemId,
    IReadOnlyList<Game.CharacterRosterAppearanceState>? Appearances);

public sealed record AccountCharacterRoster(
    AccountId AccountId,
    ushort SlotLimit,
    IReadOnlyList<CharacterSummary> Characters);

/// <summary>What the roster screen asked for when it proposed a name.</summary>
/// <remarks>
/// Kept separate from <see cref="CharacterCreationStatus"/> even though the two overlap. The name
/// check runs on its own protocol and can succeed while creation later fails, so collapsing them
/// would force one of the two wire mappings to invent a status it never receives.
/// </remarks>
public enum CharacterNameStatus
{
    Available,
    TooShort,
    TooLong,
    Malformed,
    AlreadyTaken,
}

public enum CharacterCreationStatus
{
    Created,
    MalformedRequest,
    InvalidName,
    NameTaken,
    NoFreeSlot,
}

public enum CharacterDeletionStatus
{
    Deleted,
    MalformedRequest,
    SlotEmpty,
    NameMismatch,
}

/// <summary>A character's combat stats, in the fixed-point form the wire and the database use.</summary>
/// <remarks>
/// <para>
/// Every value is the archive's number multiplied by ten: the swordman declares
/// <c>[HP MAX] 180</c> and the client is sent 1800. The scale is the same for all of them,
/// including the ones that read as percentages in the panel.
/// </para>
/// <para>
/// A stat the job does not declare is zero, and that is a real value rather than a gap: the
/// swordman lists neither fire nor water resistance, while the knight lists all four.
/// </para>
/// </remarks>
public sealed record CharacterBaseStats(
    int HpMax = 0,
    int MpMax = 0,
    int PhysicalAttack = 0,
    int PhysicalDefense = 0,
    int MagicalAttack = 0,
    int MagicalDefense = 0,
    int FireResistance = 0,
    int WaterResistance = 0,
    int DarkResistance = 0,
    int LightResistance = 0,
    int InventoryLimit = 0,
    int HpRegenSpeed = 0,
    int MpRegenSpeed = 0,
    int MoveSpeed = 0,
    int AttackSpeed = 0,
    int CastSpeed = 0,
    int HitRecovery = 0,
    int JumpPower = 0,
    int Weight = 0)
{
    /// <summary>No stats at all, for callers that are not exercising them.</summary>
    public static CharacterBaseStats Zero { get; } = new();
}

/// <summary>The fields a brand new character is born with.</summary>
/// <param name="Stats">
/// What the job's archive entry says a level-one character of this job starts with. Required
/// rather than defaulted: a character whose stats are all zero cannot act, and that was the
/// state every character created before this parameter existed was born in.
/// </param>
/// <param name="InitialSkills">
/// The skills the character is created holding, in the order they are to occupy page zero's slots.
/// Resolved from the job's archive definition by the caller rather than here, so that storage
/// stays a place that records decisions instead of one that makes them.
/// </param>
/// <param name="InitialEquipment">
/// The pieces the character is created wearing. Resolved from the job's archive definition by the
/// caller for the same reason the skills are.
/// </param>
public sealed record NewCharacterSpec(
    AccountId AccountId,
    byte[] Name,
    byte Job,
    ushort SlotIndex,
    CharacterBaseStats Stats,
    IReadOnlyList<NewCharacterSkill>? InitialSkills = null,
    IReadOnlyList<NewCharacterEquipment>? InitialEquipment = null);

/// <summary>One skill a character is created holding.</summary>
public sealed record NewCharacterSkill(short Slot, uint SkillId, byte Level);

/// <summary>One piece of equipment a character is created wearing.</summary>
/// <param name="ItemKind">
/// The kind its item core records. Carried rather than assumed, because what number equipment takes
/// is a rule about items and whoever writes the row is not the place that knows it.
/// </param>
public sealed record NewCharacterEquipment(
    byte SlotIndex, int ItemId, ushort Durability, byte ItemKind);

public sealed record CharacterCreationResult(
    CharacterCreationStatus Status,
    CharacterId? CharacterId);

/// <param name="SlotIndex">
/// The deleted character's slot, or <c>0</c> when nothing was deleted. Carried so the caller can
/// compare it against the index the request arrived with; see
/// <c>protocol/game/client-to-server/0x0006.md</c> for why that comparison is the open question.
/// </param>
public sealed record CharacterDeletionResult(
    CharacterDeletionStatus Status,
    CharacterId? CharacterId,
    ushort SlotIndex = 0);

/// <summary>Account-wide honour standing, shown on the roster screen.</summary>
public sealed record AccountHonorLevel(uint Level, uint Experience)
{
    /// <summary>What a brand new account has.</summary>
    /// <remarks>
    /// Level 1, not 0. The reference server derives honour level from experience and returns 1 for
    /// a fresh account with no characters -- level 0 never appears on the wire there, so sending it
    /// would be a state the client has no reason to expect.
    /// </remarks>
    public static AccountHonorLevel Initial { get; } = new(1, 0);
}

public sealed record CharacterIdentityState(
    CharacterId CharacterId,
    ushort SlotIndex,
    byte[] Name,
    byte Job,
    byte GrowType);

public sealed record CharacterProgressionState(byte Level, uint Experience);

public sealed record CharacterFatigueState(ushort Total, ushort Used)
{
    public int ConsumedToday { get; init; }
}

public sealed record CharacterPositionState(
    byte TownId,
    byte AreaId,
    short X,
    short Y,
    byte Direction);

public sealed record ActiveCharacterState(
    CharacterIdentityState Identity,
    CharacterProgressionState Progression,
    CharacterFatigueState Fatigue,
    CharacterPositionState Position,
    DateTimeOffset CreatedAt)
{
    public bool TutorialCompleted { get; init; }
    /// <summary>Last ordinary town with a usable Seria arrival gate; zero for legacy saves.</summary>
    public byte LastInnTownId { get; init; }
}

public sealed record CharacterAppearanceCompatibilityState(
    byte FormatVersion,
    byte[] EncodedEntries);

/// <summary>Persisted public identity fields that are separate from entry visibility bits.</summary>
public sealed record CharacterPublicIdentityEntryState(
    byte PvpGrade,
    byte PvpRatingGrade,
    byte UserState);

/// <summary>
/// The character's operational state: what a session did to it and what an operator set on it.
/// </summary>
/// <remarks>
/// These arrived as one flattened record named after a protocol segment, in the segment's own
/// order, because the tables they came from were once cut that way too. The tables were re-cut by
/// concept; this follows them. Which of these the packet carries, in what order, and narrowed to
/// what width, is the encoder's business and is stated where the bytes are written.
/// </remarks>
public sealed record CharacterConditionStorageRecord(
    int Stamina,
    long FatiguePenalty,
    int EventCharacter,
    int PrivateStore,
    int HardcoreMode,
    int HardcoreDead,
    int HardcoreDeathCount,
    long ChatBanEndTime,
    int MoodValue,
    int CreatureField1 = 0,
    int CreatureField2 = 0,
    int CreatureField3 = 0,
    int CreatureField4 = 0,
    byte[]? CreatureBuffer = null,
    long PcRoomId = 0x00010001,
    int PremiumPcRoom = 0,
    int ServerGroupId = 0,
    long BlackCount = 0,
    long ChaosPoint = 0,
    int DisguiseKind = 0,
    int Disguised = 0,
    int FatigueUpdate = 0,
    int ReturnUserFlag = 1,
    int ChannelDisplayMode = 0,
    int ChannelType = 0,
    int ReturnUser = 0,
    int LinkSlotEnabled = 0,
    int LinkTypeA = 0,
    int LinkTypeB = 0,
    int EmotionIndex = 0,
    int ActionByte = 0,
    int FatigueDisplayUpdate = 0,
    int CostumeFlag = 0,
    int AuraFlag = 0,
    int PetDisplayFlag = 0,
    int TitleDisplayFlag = 0,
    int TrailingByte = 0);

/// <summary>What the character has switched on for itself.</summary>
public sealed record CharacterSettingsStorageRecord(long UserStateBits);

/// <summary>One duel the character has on record.</summary>
public sealed record CharacterPvpResultStorageRow(long Value32, int Value16A, int Value16B);

/// <summary>Where the character stands in duelling.</summary>
public sealed record CharacterPvpStandingStorageRecord(
    long RankPoint,
    long StatA,
    int WinStreak,
    int LoseStreak,
    IReadOnlyList<CharacterPvpResultStorageRow> Results);

/// <summary>The second trade a character has taken up, and how far along it is.</summary>
public sealed record CharacterExpertJobStorageRecord(int Type, long Experience);

/// <summary>
/// Persisted compatibility fields for the public stat projection.
/// </summary>
/// <remarks>
/// Base fields are replaced by current career/level rules at projection time. Progress counters,
/// percentages and other unmodeled fields retain their stored values; this is not a complete
/// equipment/buff-derived stat model.
/// </remarks>
public sealed record CharacterDerivedStatsStorageRecord(
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
    int Progress1,
    int Progress2,
    int FireResistance = 0,
    int WaterResistance = 0,
    int DarkResistance = 0,
    int LightResistance = 0,
    int HpRegenSpeed = 0,
    int MpRegenSpeed = 0,
    int HitRecovery = 0,
    int JumpPower = 0,
    long Weight = 0,
    int CurrentHpMpPercent = 0,
    int EquippedCreatureLevel = 0,
    int EquipListTrailing = 0,
    int FlagByte = 0,
    long ServerTimestamp = 0,
    int QuestShopCount = 0);

/// <summary>Where the character stands in its guild.</summary>
public sealed record CharacterGuildStandingStorageRecord(int Level, long PowerWar);

/// <summary>
/// What a character carries into the game beyond the concepts above.
/// </summary>
/// <remarks>
/// Not a concept of its own -- these are the odds and ends the entry packet asks for that belong
/// to no single table: a couple of the character's own row, the dimensions, and the quests whose
/// reward is still owed. They travel together because they arrive together, and they are kept in
/// the widths they were stored in: what the wire gives each of them is decided where the bytes are
/// written.
/// </remarks>
public sealed record CharacterEntryExtrasState(
    int SkillTreeIndex,
    long NameTagItemId,
    long NameTagExpireTime,
    int ManageLevel,
    long CharacterExperience,
    int ExtraEquipmentSlotState,
    IReadOnlyList<(int SortOrder, long Key, int Value1, int Value2)> Dimensions,
    (int Flag1, int Flag2, int Flag3, int Flag4) DimensionFlags,
    IReadOnlyList<int> SpecialRewardQuestIds);

/// <summary>
/// Everything about a character that the entry packet shows, as the concepts it is made of.
/// </summary>
/// <remarks>
/// This used to be two records named after the packet's own segments, filled by a forty-five-field
/// positional copy out of two more records named the same way. Both ends were the packet's shape,
/// so the copy did nothing but narrow each value to the width its field has on the wire -- a
/// decision that belongs where the bytes are written, and has moved there.
///
/// What is left carries the concepts and the widths they were stored in. Which of them the packet
/// mentions, in what order, and as how many bytes, is the encoder's alone.
/// </remarks>
public sealed record CharacterPublicEntryState(
    CharacterConditionStorageRecord Condition,
    CharacterSettingsStorageRecord Settings,
    CharacterPvpStandingStorageRecord Pvp,
    CharacterExpertJobStorageRecord ExpertJob,
    CharacterDerivedStatsStorageRecord DerivedStats,
    CharacterGuildStandingStorageRecord Guild,
    CharacterEntryExtrasState Extras,
    CharacterAppearanceCompatibilityState Appearance,
    long VisibilityBits,
    uint CloneTitleItemId,
    CharacterPublicIdentityEntryState? Identity = null,
    bool HasBlackDiamond = false);

public sealed record CharacterSkillState(
    short Slot,
    uint SkillId,
    byte Level,
    IReadOnlyList<byte> ExtraValues);

public sealed record CharacterSkillPageState(
    int PageIndex,
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
    long VisibilityBits)
{
    public IReadOnlyList<byte> SeenNotices { get; init; } = [];
}

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
    IReadOnlyList<CharacterTitleBookEntryState> Entries)
{
    // Project-owned client compatibility extension, not a native title-book field.
    public bool MirrorProjectionEnabled { get; init; }
    public CharacterTitleBookWearSource? WearSource { get; init; }
}

public sealed record CharacterTitleBookWearSource(int Category, int Index, int ItemId);

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
    IReadOnlyList<CharacterMailboxAttachmentEntryState> Attachments)
{
    public IReadOnlyList<MailGoldAttachmentState> GoldAttachments { get; init; } = [];
}

public sealed record MailGoldAttachmentState(long AttachmentId, int Amount);

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
