using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal abstract record GameStateSynchronizationMessage : IGameServerMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
    internal abstract ushort Type { get; }
    internal abstract byte[] EncodeBody();
}

internal sealed class GameCmdStateSynchronizationEncoder<TMessage>
    : GameCmdMessageEncoder<TMessage>
    where TMessage : GameStateSynchronizationMessage, IGameCmdMessage
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TMessage message) =>
        OnePayload(message.Type, message.EncodeBody());
}

internal sealed class GameNotiStateSynchronizationEncoder<TMessage>
    : GameNotiMessageEncoder<TMessage>
    where TMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TMessage message) =>
        OnePayload(message.Type, message.EncodeBody());
}

internal sealed record CharacterEntryAcceptedMessage(
    ActiveCharacterState Character,
    AccountEntryState Account,
    CharacterQuestEntryState Quests,
    DateTimeOffset PreparedAt) : GameStateSynchronizationMessage, IGameCmdMessage
{
    internal override ushort Type => 0x0004;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodeEntryAccepted(Character, Account, Quests, PreparedAt);
}

internal sealed record InventorySnapshotMessage(
    InventoryContainerState Container,
    IReadOnlyList<InventoryRuntimeValueState> CharacterVirtualResources,
    IReadOnlyList<InventoryRuntimeValueState> AccountVirtualResources,
    ushort AccountSelectionKey,
    int AccountValue32,
    DateTimeOffset PreparedAt) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x000D;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeInventorySnapshot(
        Container, CharacterVirtualResources, AccountVirtualResources,
        AccountSelectionKey, AccountValue32, PreparedAt);
}

internal sealed record InventoryDeltaMessage(
    InventoryContainerType ContainerType,
    IReadOnlyList<InventoryItemState> Items,
    DateTimeOffset PreparedAt) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x000E;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodeInventoryDelta(ContainerType, Items, PreparedAt);
}

internal sealed record CreatureListMessage(
    IReadOnlyList<CharacterPetRuntimeState> Pets) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0069;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeCreatures(Pets);
}

internal sealed record CurrentUserStateMessage(
    ActiveCharacterState Character,
    CharacterPublicEntryState Public,
    IReadOnlyList<InventoryItemState> Equipment,
    IReadOnlyList<CharacterPetRuntimeState> Pets) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0002;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodeCurrentUserState(Character, Public, Equipment, Pets);
}

internal sealed record ExtendedUserStateMessage(
    ActiveCharacterState Character,
    CharacterPublicEntryState Public,
    CharacterSkillsEntryState Skills,
    IReadOnlyList<InventoryItemState> Equipment,
    DateTimeOffset PreparedAt) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0002;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeExtendedUserState(
        Character, Public, Skills, Equipment, PreparedAt);
}

internal sealed record KnightShieldDeckMessage(
    IReadOnlyList<int> Deck) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0245;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeKnightShieldDeck(Deck);
}

internal sealed record SkillStateMessage(
    CharacterSkillsEntryState Skills) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0013;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeSkills(Skills);
}

internal sealed record DarkKnightComboStateMessage(
    IReadOnlyList<DarkKnightComboEntryState> Combos) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x01C0;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeDarkKnightCombos(Combos);
}

internal sealed record AvailableQuestListMessage(
    byte CharacterLevel,
    AvailableQuestEntryState AvailableQuests) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0015;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodeAvailableQuests(CharacterLevel, AvailableQuests);
}

internal sealed record QuestVisibilityMessage(byte[] Flags) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0164;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeQuestVisibility(Flags);
}

internal sealed record DailyChallengeStateMessage(
    CharacterProgressionEntryState Progression) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0286;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeDailyChallenge(Progression);
}

internal sealed record AccountGameOptionsMessage(
    AccountSettingsEntryState Settings) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x00AD;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeGameOptions(Settings);
}

internal sealed record HotkeyStateMessage(
    AccountSettingsEntryState AccountSettings,
    CharacterSettingsEntryState CharacterSettings) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x01C7;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodeHotkeys(AccountSettings, CharacterSettings);
}

/// <summary>0x01C7 as sent at login, before any character exists to contribute slots.</summary>
/// <remarks>
/// A separate record rather than a nullable character argument on <see cref="HotkeyStateMessage"/>:
/// the two are sent at different points for different reasons, and letting one message mean
/// "with character" or "without character" depending on a null would hide which one a call site
/// intended.
/// </remarks>
internal sealed record LoginHotkeyStateMessage(
    AccountSettingsEntryState AccountSettings) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x01C7;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodeAccountHotkeys(AccountSettings);
}

internal sealed record ServerEventBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x006C;
    internal override byte[] EncodeBody() => new byte[3];
}

internal sealed record DungeonPermissionStateMessage(
    IReadOnlyList<CharacterDungeonProgressState> Dungeons) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0005;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeDungeonPermissions(Dungeons);
}

internal sealed record CharacterOptionStateMessage(byte[]? OptionBitmap) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0187;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeCharacterOption(OptionBitmap);
}

internal sealed record GrowthWeaponStateMessage(
    IReadOnlyList<byte> Stages) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x01B9;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeGrowthWeapon(Stages);
}

internal sealed record TitleBookStateMessage(
    CharacterTitleBookCategoryState Category) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0166;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeTitleBook(Category);
}

internal sealed record AchievementStateMessage(
    IReadOnlyList<CharacterAchievementProgressState> Achievements) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0167;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeAchievements(Achievements);
}

internal sealed record SkillPointSlotStateMessage(
    IReadOnlyList<CharacterSkillPointSlotState> Slots) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x015F;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeSkillPointSlots(Slots);
}

internal sealed record ItemCooldownStateMessage(
    IReadOnlyList<InventoryItemValueState> Entries) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x00AC;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeItemValues(Entries);
}

internal sealed record ItemEffectStateMessage(
    IReadOnlyList<InventoryItemValueState> Entries) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x00AE;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeItemValues(Entries);
}

internal sealed record PurchaseRestrictionCompatibilityMessage(
    IReadOnlyList<AccountCompatibilityEntryState> Entries) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x02DA;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeCompatibilityEntries(Entries);
}

internal sealed record ClientEffectBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x017B;
    internal override byte[] EncodeBody() => [2, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0];
}

internal sealed record CollectionBoxStateMessage(
    CharacterCollectionBoxProgressState Box) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0381;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeCollectionBox(Box);
}

internal sealed record BossTowerBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x021F;
    internal override byte[] EncodeBody() => new byte[4];
}

internal sealed record RentalStateMessage(
    CharacterRentalEntryState Rentals,
    ushort LuckyStar,
    DateTimeOffset PreparedAt) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0357;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodeRentals(Rentals, LuckyStar, PreparedAt);
}

internal sealed record EquipmentLockStateMessage(
    IReadOnlyList<InventoryEquipmentLockState> CharacterLocks,
    IReadOnlyList<InventoryEquipmentLockState> AccountLocks) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x00FB;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodeEquipmentLocks(CharacterLocks, AccountLocks);
}

internal sealed record ExpertJobStateMessage(
    CharacterExpertJobEntryState ExpertJob) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x00CD;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeExpert(ExpertJob);
}

internal sealed record SupportCharacterStateMessage(
    CharacterId OwnerCharacterId,
    IReadOnlyList<AccountCharacterRelationEntryState> Relations,
    DateTimeOffset PreparedAt) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x019F;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeSupportCharacter(
        OwnerCharacterId, Relations, PreparedAt);
}

internal sealed record DailyReviveCoinStateMessage(bool Claimed) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x00B1;
    internal override byte[] EncodeBody() => [Claimed ? (byte)1 : (byte)0];
}

internal sealed record CubeProgressionStateMessage(byte CubeType, byte CubeGrade)
    : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0300;
    internal override byte[] EncodeBody() => [CubeType, CubeGrade];
}

internal sealed record PremiumServiceStateMessage(
    AccountEntitlementEntryState Entitlements,
    IReadOnlyList<CharacterPremiumUsageSlotState> UsageSlots) : GameStateSynchronizationMessage, IGameCmdMessage
{
    internal override ushort Type => 0x0312;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodePremiumService(Entitlements, UsageSlots);
}

internal sealed record LotteryProgressStateMessage(
    AccountProgressionEntryState Progression) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x03D8;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeLotteryProgress(Progression);
}

internal sealed record LuckyStarStateMessage(ushort LuckyStar) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x019D;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeUnsigned32(LuckyStar);
}

internal sealed record ChampionBreakStateMessage(
    CharacterProgressionEntryState Progression) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x025B;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeChampionBreak(Progression);
}

internal sealed record GoldLimitStateMessage(byte UpgradeLevel) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0331;
    internal override byte[] EncodeBody() => [UpgradeLevel];
}

internal sealed record FatigueAccelerationStateMessage(
    ushort Value,
    byte State) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x01EB;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeFatigueAcceleration(Value, State);
}

internal sealed record MailboxStateMessage(
    CharacterMailboxEntryState Mailbox) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0061;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeMailbox(Mailbox);
}

internal sealed record PvpMissionStateMessage(
    IReadOnlyList<CharacterPvpMissionEntryState> Missions) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0158;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodePvpMissions(Missions);
}

internal sealed record DailyScheduleStateMessage(
    CharacterScheduleCompatibilityState Schedule) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x02D5;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeDailySchedule(Schedule);
}

internal sealed record PcRoomPlayTimeStateMessage(byte State) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x01A8;
    internal override byte[] EncodeBody() => [State];
}

internal sealed record EntryMarkerMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x007C;
    internal override byte[] EncodeBody() => [];
}

internal sealed record CeraBalanceStateMessage(
    AccountWalletEntryState Wallet) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0035;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeCera(Wallet);
}

internal sealed record CrossServerFriendBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0111;
    internal override byte[] EncodeBody() => new byte[8];
}

internal sealed record CharacterPositionStateMessage(
    CharacterId CharacterId,
    EntrySpawnState Spawn) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0016;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodePosition(CharacterId, Spawn);
}

internal sealed record PetWelcomeStateMessage(
    CharacterId CharacterId,
    PetWelcomeEntryState Welcome) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0077;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodePetWelcome(CharacterId, Welcome);
}

internal static class StateSynchronizationEncoderRegistration
{
    public static void AddStateSynchronizationEncoders(IServiceCollection services)
    {
        AddCmd<CharacterEntryAcceptedMessage>(services);
        AddNoti<InventorySnapshotMessage>(services);
        AddNoti<InventoryDeltaMessage>(services);
        AddNoti<CreatureListMessage>(services);
        AddNoti<CurrentUserStateMessage>(services);
        AddNoti<ExtendedUserStateMessage>(services);
        AddNoti<KnightShieldDeckMessage>(services);
        AddNoti<SkillStateMessage>(services);
        AddNoti<DarkKnightComboStateMessage>(services);
        AddNoti<AvailableQuestListMessage>(services);
        AddNoti<QuestVisibilityMessage>(services);
        AddNoti<DailyChallengeStateMessage>(services);
        AddNoti<AccountGameOptionsMessage>(services);
        AddNoti<HotkeyStateMessage>(services);
        AddNoti<LoginHotkeyStateMessage>(services);
        AddNoti<ServerEventBaselineMessage>(services);
        AddNoti<DungeonPermissionStateMessage>(services);
        AddNoti<CharacterOptionStateMessage>(services);
        AddNoti<GrowthWeaponStateMessage>(services);
        AddNoti<TitleBookStateMessage>(services);
        AddNoti<AchievementStateMessage>(services);
        AddNoti<SkillPointSlotStateMessage>(services);
        AddNoti<ItemCooldownStateMessage>(services);
        AddNoti<ItemEffectStateMessage>(services);
        AddNoti<PurchaseRestrictionCompatibilityMessage>(services);
        AddNoti<ClientEffectBaselineMessage>(services);
        AddNoti<CollectionBoxStateMessage>(services);
        AddNoti<BossTowerBaselineMessage>(services);
        AddNoti<RentalStateMessage>(services);
        AddNoti<EquipmentLockStateMessage>(services);
        AddNoti<ExpertJobStateMessage>(services);
        AddNoti<SupportCharacterStateMessage>(services);
        AddNoti<DailyReviveCoinStateMessage>(services);
        AddNoti<CubeProgressionStateMessage>(services);
        AddCmd<PremiumServiceStateMessage>(services);
        AddNoti<LotteryProgressStateMessage>(services);
        AddNoti<LuckyStarStateMessage>(services);
        AddNoti<ChampionBreakStateMessage>(services);
        AddNoti<GoldLimitStateMessage>(services);
        AddNoti<FatigueAccelerationStateMessage>(services);
        AddNoti<MailboxStateMessage>(services);
        AddNoti<PvpMissionStateMessage>(services);
        AddNoti<DailyScheduleStateMessage>(services);
        AddNoti<PcRoomPlayTimeStateMessage>(services);
        AddNoti<EntryMarkerMessage>(services);
        AddNoti<CeraBalanceStateMessage>(services);
        AddNoti<CrossServerFriendBaselineMessage>(services);
        AddNoti<CharacterPositionStateMessage>(services);
        AddNoti<PetWelcomeStateMessage>(services);
    }

    private static void AddCmd<TMessage>(IServiceCollection services)
        where TMessage : GameStateSynchronizationMessage, IGameCmdMessage =>
        services.AddGameCmdMessageEncoder<TMessage, GameCmdStateSynchronizationEncoder<TMessage>>();

    private static void AddNoti<TMessage>(IServiceCollection services)
        where TMessage : GameStateSynchronizationMessage, IGameNotiMessage =>
        services.AddGameNotiMessageEncoder<TMessage, GameNotiStateSynchronizationEncoder<TMessage>>();
}
