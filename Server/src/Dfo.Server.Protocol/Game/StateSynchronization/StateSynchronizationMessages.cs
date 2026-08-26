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
    byte TownId,
    DateTimeOffset PreparedAt) : GameStateSynchronizationMessage, IGameCmdMessage
{
    internal override ushort Type => 0x0004;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodeEntryAccepted(
            Character, Account, Quests, TownId, PreparedAt);
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

/// <summary>NOTI 0x0003 <c>USER_STATE</c>, four bytes the A21 entry sequence sends before the town.</summary>
/// <remarks>
/// Transcribed from a working A21 session, which sends <c>01 01 00 00</c> here for a character
/// entering a town. Neither byte pair is resolved to a field, so this stays a constant rather than
/// a guess dressed up as an encoder.
/// </remarks>
internal sealed record UserStateBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0003;
    internal override byte[] EncodeBody() => [0x01, 0x01, 0x00, 0x00];
}

/// <summary>NOTI 0x00CA <c>VILLAGE_OBJECT_LIST</c>, an empty list.</summary>
/// <remarks>
/// A working A21 session sends a single zero byte, which reads as a count of no village objects.
/// The client still has to be told the list is empty -- withholding the frame is not the same
/// statement as an empty one.
/// </remarks>
internal sealed record VillageObjectListMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x00CA;
    internal override byte[] EncodeBody() => [0x00];
}

/// <summary>NOTI 0x0465 <c>EPIC_BOOK_INFO</c>, five zero bytes.</summary>
internal sealed record EpicBookBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0465;
    internal override byte[] EncodeBody() => new byte[5];
}

/// <summary>NOTI 0x021E <c>SET_USABLE_COUNT_INFO</c>, one zero byte.</summary>
internal sealed record UsableCountBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x021E;
    internal override byte[] EncodeBody() => [0x00];
}

/// <summary>NOTI 0x0009 <c>PARTY_INFO</c>, two zero bytes for a character in no party.</summary>
internal sealed record PartyBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0009;
    internal override byte[] EncodeBody() => new byte[2];
}

/// <summary>NOTI 0x000D <c>ITEM_LIST</c> for the guild-medal container, which is always empty here.
/// </summary>
/// <remarks>
/// A21 carries a sixth entry container the older client did not: list type 38, <c>GuildMedal</c>
/// upstream. The reference session sends it as the last of six ITEM_LIST frames with a body of
/// <c>26 00 00</c> -- the type byte and a zero <c>u16</c> count.
///
/// It is a baseline rather than a real snapshot because guild medals are not modelled at all: there
/// is no table to read and nothing that could ever put an item in it. Routing it through
/// <c>AddInventorySnapshots</c> would mean inventing a container in the persistence layer whose only
/// possible content is emptiness.
/// </remarks>
internal sealed record GuildMedalBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x000D;
    internal override byte[] EncodeBody() => [(byte)InventoryContainerType.GuildMedal, 0, 0];
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
    // BUY_RESTRICT_ITEM_LIST on A21. The older client numbered it 0x02DA, which A21 gives to YUT_GAME_EVENT_INFO --
    // sending it there hands this body to the wrong handler.
    internal override ushort Type => 0x0344;
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
    // 0x0381 is COLLECT_BOX on A12 and USER_BUY_RESTRICT_UPDATE_ITEM_LIST on A21; COLLECT_BOX
    // moved to 0x03EB there. That table difference is real, and it is not what decides this.
    //
    // This briefly moved to 0x03EB on the argument that the A21 client registers no receive
    // handler at 0x0381, so anything sent there is discarded. Both halves of that are true and
    // the conclusion drawn from them was still backwards. The A21 reference session sends two
    // eight-byte frames at 0x0381 -- 01 01 FF FF FF FF 00 00 and 02 01 FF FF FF FF 00 00 -- and
    // those are byte for byte what this record emits, because that is where these bytes were
    // transcribed from in the first place. The payload is A21's, at A21's number, and the client
    // ignoring it is what the reference server also gets.
    //
    // 0x03EB, by contrast, HAS a receive handler on A21 (RVA 0x008A7490), and the reference never
    // sends it. Moving here therefore stopped being a no-op and started feeding a live handler a
    // body in another protocol's format.
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
    // EQUIPMENT_RENTAL_LIST on A21. The older client numbered it 0x0357, which A21 gives to TOURNAMENT_END --
    // sending it there hands this body to the wrong handler.
    internal override ushort Type => 0x03C1;
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
    // CONTRACT_OF_CUBE_INFO on A21. The older client numbered it 0x0300, which A21 gives to LUKE_RAID_KILLED_NAMED_INFO --
    // sending it there hands this body to the wrong handler.
    internal override ushort Type => 0x036A;
    internal override byte[] EncodeBody() => [CubeType, CubeGrade];
}

internal sealed record PremiumServiceStateMessage(
    AccountEntitlementEntryState Entitlements,
    IReadOnlyList<CharacterPremiumUsageSlotState> UsageSlots) : GameStateSynchronizationMessage, IGameCmdMessage
{
    // PREMIUM_SERVICE on A21. The older client numbered it 0x0312, which A21 gives to CHANGE_GUILD_MARK --
    // sending it there hands this body to the wrong handler.
    internal override ushort Type => 0x036F;
    internal override byte[] EncodeBody() =>
        StateSynchronizationPayloadCodec.EncodePremiumService(Entitlements, UsageSlots);
}

internal sealed record LotteryProgressStateMessage(
    AccountProgressionEntryState Progression) : GameStateSynchronizationMessage, IGameNotiMessage
{
    // A21 calls 0x03D8 COUPLE_ROOM; the A12 table calls it INCREASE_CHANCE_LOTTERY_ALL. A working A21 session
    // sends 204 zero bytes here, byte for byte what this encoder produces, so 0x03D8 stays.
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
    // A21 calls 0x0331 LIMIT_IPGNO; the A12 table calls it UPGRADE_CARRY_GOLD. A working A21 session sends this
    // single byte here, so 0x0331 stays.
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
    // A21 calls 0x02D5 ACCOUNT_TUTORIAL_INFO; the A12 table calls it DAILYSCHEDULE_CONTENTS_STATE. A working A21
    // session sends three 16-byte bodies here, matching this encoder, so 0x02D5 stays.
    internal override ushort Type => 0x02D5;
    internal override byte[] EncodeBody() => StateSynchronizationPayloadCodec.EncodeDailySchedule(Schedule);
}

internal sealed record PcRoomPlayTimeStateMessage(byte State) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x01A8;
    internal override byte[] EncodeBody() => [State];
}

/// <summary>The signal that world entry is over.</summary>
/// <remarks>
/// Eight bytes on A21: two 32-bit ones. It carried nothing at all on the older client, and this
/// server kept sending nothing -- a length the frame layer is perfectly happy with, which is why
/// it survived. Both values are transcribed; what the client does with them is not known here.
/// </remarks>
internal sealed record EntryMarkerMessage : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x007C;

    internal override byte[] EncodeBody() => [0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00];
}

/// <summary>The twenty-five byte account-info frame that follows the entry marker on A21.</summary>
/// <remarks>
/// New with this client: the older one had nothing in this slot, and this server filled it by
/// repeating the subtype the entry batch already opened with. The known-good session sends
/// subtype six here instead, once, and never repeats subtype zero.
///
/// Every value but the character id is transcribed. The 32-bit constant in the middle is
/// <c>0x68624229</c>, which reads as ASCII but spells nothing; it is written as the number it was
/// seen as rather than dressed up as a marker.
/// </remarks>
internal sealed record TownReadyStateMessage(CharacterId CharacterId)
    : GameStateSynchronizationMessage, IGameNotiMessage
{
    private const byte Subtype = 0x06;

    private const ushort Version = 1;

    private const uint AllBits = 0xFFFFFFFFu;

    private const uint SharedOpaqueConstant = 0x68624229u;

    private const uint TownReadyFlag = 0x000000FFu;

    internal override ushort Type => 0x0002;

    internal override byte[] EncodeBody()
    {
        var characterId = CharacterId.Value;
        if (characterId is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException(
                $"Character '{characterId}' does not fit the town-ready frame's 16-bit identifier.");
        }

        using var stream = new MemoryStream(25);
        using var writer = new BinaryWriter(stream);
        writer.Write(Subtype);
        writer.Write(Version);
        writer.Write((ushort)characterId);
        writer.Write(AllBits);
        writer.Write(SharedOpaqueConstant);
        writer.Write(TownReadyFlag);
        writer.Write(TownReadyFlag);
        writer.Write(0u);
        return stream.ToArray();
    }
}

/// <summary>CMD 0x01BA <c>MERCENARY_INFO</c> as the entry batch pushes it, unprompted.</summary>
/// <remarks>
/// The client also asks for this by itself once it is in town, and <c>MercenaryInfoHandler</c>
/// answers that. This is the other occurrence: the reference session sends the same seven bytes a
/// second time, inside the entry batch, between the town-ready frame and CERA -- before the client
/// has asked for anything. Answering only the request leaves that slot empty.
///
/// The body is the family envelope's acceptance byte followed by manageLevel, managePoint and a
/// zero record count, which is byte for byte what the handler's reply carries.
/// </remarks>
internal sealed record MercenarySupportStateMessage
    : GameStateSynchronizationMessage, IGameCmdMessage
{
    internal override ushort Type => 0x01BA;

    // Not a second copy of the handler's six bytes: the handler's own roster is the source, so the
    // two cannot drift apart into a batch frame and a reply that disagree.
    internal override byte[] EncodeBody() =>
        GameCmdEnvelope.Accept(MercenaryInfo.MercenaryInfoResponseEncoder.EmptyRoster);
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
        AddNoti<UserStateBaselineMessage>(services);
        AddNoti<VillageObjectListMessage>(services);
        AddNoti<EpicBookBaselineMessage>(services);
        AddNoti<UsableCountBaselineMessage>(services);
        AddNoti<PartyBaselineMessage>(services);
        AddNoti<GuildMedalBaselineMessage>(services);
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
        AddCmd<MercenarySupportStateMessage>(services);
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
        AddNoti<TownReadyStateMessage>(services);
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
