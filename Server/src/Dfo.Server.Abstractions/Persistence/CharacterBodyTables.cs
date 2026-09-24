using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Persistence;

// The character's own tables, one interface per table, as the ruling in
// Docs/Refactor/TableOwnershipRuling.md cut them. Each of these holds exactly one row per
// character: the row is written when the character is created, so a reader that finds nothing has
// found a character that was never finished, not a character with nothing to say.

/// <summary>One row of <c>character_condition</c>.</summary>
public sealed record CharacterConditionRow(
    int ActionByte,
    int AuraFlag,
    long BlackCount,
    int ChampionBreakKeyId,
    int ChampionBreakMode,
    int ChampionBreakValue,
    int ChannelDisplayMode,
    int ChannelType,
    long ChaosPoint,
    long ChatBanEndTime,
    int CostumeFlag,
    byte[]? CreatureBuffer,
    int CreatureField1,
    int CreatureField2,
    int CreatureField3,
    int CreatureField4,
    int CrystalCubeGrade,
    int CrystalCubeType,
    int DimensionFlag1,
    int DimensionFlag2,
    int DimensionFlag3,
    int DimensionFlag4,
    int DisguiseKind,
    int EmotionIndex,
    int FatigueDisplayUpdate,
    long FatiguePenalty,
    int FatigueUpdate,
    int HardcoreDeathCount,
    int Disguised,
    int EventCharacter,
    int HardcoreDead,
    int HardcoreMode,
    int PremiumPcRoom,
    int PrivateStore,
    int ReturnUser,
    int LinkSlotEnabled,
    int LinkTypeA,
    int LinkTypeB,
    int MoodValue,
    long PcRoomId,
    int PcRoomState,
    int PetDisplayFlag,
    int ReturnUserFlag,
    int ServerGroupId,
    int Stamina,
    int TitleDisplayFlag,
    int TrailingByte);

/// <summary>One row of <c>character_stats</c>.</summary>
public sealed record CharacterStatsRow(
    int AuctionGoldLimit,
    int CurrentHpMpPercent,
    int EquipListTrailing,
    int EquippedCreatureLevel,
    int FlagByte,
    int GoldCarryLimit,
    int Progress1,
    int Progress2,
    int QuestShopCount,
    long ServerTimestamp,
    int AttackSpeed,
    int CastSpeed,
    int DarkResistance,
    int FireResistance,
    int HitRecovery,
    int HpMaximum,
    int HpRegenSpeed,
    int InventoryLimit,
    int JumpPower,
    int LightResistance,
    int MagicalAttack,
    int MagicalDefense,
    int MoveSpeed,
    int MpMaximum,
    int MpRegenSpeed,
    int PhysicalAttack,
    int PhysicalDefense,
    int WaterResistance,
    long Weight);

/// <summary>One row of <c>character_pvp</c>.</summary>
public sealed record CharacterPvpRow(
    int PvpGrade,
    int PvpLoseStreak,
    long PvpRankPoint,
    int PvpRatingGrade,
    long PvpStatA,
    int PvpWinStreak,
    int SkillsInitialized);

/// <summary>One row of <c>character_settings</c>.</summary>
public sealed record CharacterSettingsRow(
    byte[]? OptionBlob,
    long UserStateBits);

/// <summary>One row of <c>character_expert_job</c>.</summary>
public sealed record CharacterExpertJobRow(
    int DisjointMachineEndurance,
    int DisjointMachineGrade,
    int EnchanterEndurance,
    long ExpertJobExperience,
    int ExpertJobType,
    int GiveUpCount);

/// <summary>The <c>character_condition</c> table.</summary>
public interface ICharacterConditionTable
{
    ValueTask<CharacterConditionRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>
    /// Gives the character its row unless it has one.
    /// </summary>
    /// <remarks>
    /// <c>pc_room_state</c> is named explicitly because it is the one column here that is nullable
    /// with no default, and its reader refuses a NULL. Everything else takes the declaration's own
    /// default, which is what a missing row used to read as.
    /// </remarks>
    ValueTask InsertIfAbsentAsync(CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>
    /// The PC-room state of each named character that has a row.
    /// </summary>
    /// <remarks>
    /// The account load asks for a whole roster at once, and it decides for itself what a character
    /// with no row means -- which is why this reports what it found rather than filling gaps.
    /// </remarks>
    ValueTask<IReadOnlyList<CharacterPcRoomRow>> ReadPcRoomStatesAsync(
        IReadOnlyList<int> characterIds, CancellationToken cancellationToken);
}

/// <summary>One character's PC-room state.</summary>
public sealed record CharacterPcRoomRow(int CharacterId, int PcRoomState);

/// <summary>The <c>character_stats</c> table.</summary>
public interface ICharacterStatsTable
{
    ValueTask<CharacterStatsRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Gives the character its row, carrying the stats it starts with.</summary>
    ValueTask InsertIfAbsentAsync(
        CharacterId characterId, CharacterBaseStats stats, int currentHpMpPercent,
        CancellationToken cancellationToken);
}

/// <summary>The <c>character_pvp</c> table.</summary>
public interface ICharacterPvpTable
{
    ValueTask<CharacterPvpRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    ValueTask InsertIfAbsentAsync(CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>
    /// Says the duel-arena skill pages have been seeded.
    /// </summary>
    /// <remarks>
    /// A flag on a row that is always there, rather than a row whose presence was the fact.
    /// </remarks>
    ValueTask MarkSkillsInitializedAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_settings</c> table.</summary>
public interface ICharacterSettingsTable
{
    ValueTask SaveOptionAsync(CharacterId characterId, byte[] body, CancellationToken cancellationToken);

    ValueTask<CharacterSettingsRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    ValueTask InsertIfAbsentAsync(CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The <c>character_expert_job</c> table.</summary>
public interface ICharacterExpertJobTable
{
    ValueTask<CharacterExpertJobRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    ValueTask InsertIfAbsentAsync(CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>One row of <c>character_daily_challenges</c>.</summary>
/// <remarks>
/// The claim used to be a row in a table of its own, and its presence was the claim. It is a
/// timestamp on the group's own row now, and its presence is the claim.
/// </remarks>
public sealed record DailyChallengeRow(int GroupIndex, int GroupId, bool Claimed);

/// <summary>The <c>character_daily_challenges</c> table.</summary>
public interface ICharacterDailyChallengeTable
{
    ValueTask<IReadOnlyList<DailyChallengeRow>> ReadAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}
