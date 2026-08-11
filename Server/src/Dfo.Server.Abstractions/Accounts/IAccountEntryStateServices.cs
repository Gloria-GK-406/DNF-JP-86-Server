using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Accounts;

public sealed record AccountSettingsStorageRecord(
    byte[]? MainGameOptions,
    byte[] QuickChatBank0,
    byte[] QuickChatBank1,
    int HotkeyKeyType,
    byte[] AccountHotkeySlotPrefix);

public sealed record AccountWalletEntitlementStorageRecord(
    long Cera,
    long TokenCera,
    long HappyTokenCera,
    long LuckyStar,
    IReadOnlyList<(int PremiumType, long ExpiresAtUnixSeconds)> Premiums);

public sealed record AccountProgressionStorageRecord(
    long HonorExperience,
    long GrowthCapsuleExperience,
    IReadOnlyList<(int CharacterId, int SlotIndex, int Level)> Roster,
    IReadOnlyList<(int ItemTemplateId, int RewardIndex)> LotteryProgress);

public sealed record AccountRelationCompatibilityStorageRecord(
    IReadOnlyList<(int OwnerCharacterId, int Slot, int SupportCharacterId, int SkillId, int StrikerSkillId)> Relations,
    IReadOnlyList<(int CharacterSlotIndex, int SortOrder, int EntryId, int SentinelOrValue, int Flag)> Unknown730Entries,
    IReadOnlyList<int> PcRoomPlayTimeStates);

public sealed record AccountEntryStorageRecord(
    AccountId AccountId,
    AccountSettingsStorageRecord Settings,
    AccountWalletEntitlementStorageRecord WalletEntitlements,
    AccountProgressionStorageRecord Progression,
    AccountRelationCompatibilityStorageRecord Relations);

public interface IAccountEntryStateRepository
{
    ValueTask<AccountEntryStorageRecord> LoadAsync(
        AccountId accountId,
        CancellationToken cancellationToken);
}

public interface IAccountSettingsEntryService
{
    AccountSettingsEntryState Build(AccountSettingsStorageRecord storage);
}

public interface IAccountWalletEntitlementEntryService
{
    (AccountWalletEntryState Wallet, AccountEntitlementEntryState Entitlements) Build(
        AccountWalletEntitlementStorageRecord storage);
}

public interface IAccountProgressionEntryService
{
    AccountProgressionEntryState Build(AccountProgressionStorageRecord storage);
}

public interface IAccountRelationCompatibilityEntryService
{
    AccountRelationCompatibilityEntryState Build(
        AccountRelationCompatibilityStorageRecord storage,
        IReadOnlyList<(int CharacterId, int SlotIndex, int Level)> roster);
}

public interface IAccountEntryStateService
{
    ValueTask<AccountEntryState> LoadAsync(
        AccountId accountId,
        CancellationToken cancellationToken);
}
