using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Data.Accounts;

public readonly record struct AccountId(int Value)
{
    public override string ToString() => Value.ToString();
}

public sealed record AccountRecord(
    AccountId AccountId,
    string LoginId,
    string PasswordHash,
    DateTimeOffset CreatedAt);

public sealed record AccountSettingsEntryState(
    byte[] MainGameOptions,
    byte[] QuickChatBank0,
    byte[] QuickChatBank1,
    byte HotkeyKeyType,
    ushort? AccountHotkeySlot)
{
    public IReadOnlyList<byte> SeenNotices { get; init; } = [];
    public IReadOnlyList<HotkeyProfileState> HotkeyProfiles { get; init; } = [];
}

public sealed record AccountWalletEntryState(
    int Cera,
    int TokenCera,
    int HappyTokenCera,
    ushort LuckyStar);

public sealed record AccountPremiumEntryState(int PremiumType, long ExpiresAtUnixSeconds);

public sealed record AccountEntitlementEntryState(
    IReadOnlyList<AccountPremiumEntryState> Premiums);

public sealed record AccountAdventureEntryState(
    IReadOnlyList<byte> CharacterLevels);

public sealed record AccountLotteryProgressEntryState(
    int ItemTemplateId,
    IReadOnlyList<byte> ClaimedRewardIndexes);

public sealed record AccountProgressionEntryState(
    ulong HonorExperience,
    uint GrowthCapsuleExperience,
    AccountAdventureEntryState Adventure,
    IReadOnlyList<AccountLotteryProgressEntryState> LotteryProgress);

public sealed record AccountCharacterRelationEntryState(
    CharacterId OwnerCharacterId,
    byte Slot,
    CharacterId SupportCharacterId,
    int SkillId,
    int StrikerSkillId,
    AccountSupportCharacterEntryState? SupportProjection = null);

public sealed record AccountSupportCharacterEntryState(
    ActiveCharacterState Character,
    CharacterEntryState Entry,
    CharacterInventoryEntryState Inventory);

public sealed record AccountRosterReferenceEntryState(
    CharacterId CharacterId,
    ushort SlotIndex);

public sealed record AccountCompatibilityEntryState(
    int EntryId,
    int SentinelOrValue,
    int Flag);

public sealed record AccountRelationCompatibilityEntryState(
    IReadOnlyList<AccountRosterReferenceEntryState> RosterReferences,
    IReadOnlyList<AccountCharacterRelationEntryState> CharacterRelations,
    IReadOnlyList<AccountCompatibilityEntryState> Unknown730Entries,
    byte PcRoomPlayTimeState);

public sealed record AccountEntryState(
    AccountId AccountId,
    AccountSettingsEntryState Settings,
    AccountWalletEntryState Wallet,
    AccountEntitlementEntryState Entitlements,
    AccountProgressionEntryState Progression,
    AccountRelationCompatibilityEntryState Relations);
