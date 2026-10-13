using System.Collections.Immutable;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Inventory;

public sealed record MagicBoxOpeningInput(byte ListType, int SourceSlot, int ExpectedItemId,
    int MaterialSlot, int ExpectedMaterialId, int Count);
public sealed record PackageOpeningInput(int SourceSlot, ImmutableArray<int> SelectedItems);
public sealed record AvatarPackageChoice(int ItemId, byte Ability);
public sealed record AvatarPackageOpeningInput(int SourceSlot, ImmutableArray<AvatarPackageChoice> Choices);
public sealed record BusinessMagicBoxOpeningInput(byte ListType, BusinessSlot SourceSlot, int ExpectedItemId,
    BusinessSlot? MaterialSlot, int ExpectedMaterialId, int Count);
public sealed record BusinessPackageOpeningInput(BusinessSlot SourceSlot, ImmutableArray<int> SelectedItems);
public sealed record BusinessAvatarPackageOpeningInput(BusinessSlot SourceSlot, ImmutableArray<AvatarPackageChoice> Choices);
public enum OpeningRejection { None, NoSession, InvalidRequest, WrongItem, MissingMaterial, UnsupportedContent, InvalidChoice, NoRoom, StateChanged, StorageFailure, ItemUnusable, PremiumUnavailable }
public sealed record OpeningReward(int ItemId, int Count, byte Ability = 0, int PeriodDays = 0);
public sealed record OpeningRewardDisplay(BusinessSlot? Slot, int ItemId, int Count, ushort Durability, byte Ability, int Expiration,
    ItemSnapshot? Snapshot = null);
public sealed record OpeningInventoryUpdate(BusinessSlot Slot, ItemIdentity? Identity, ItemSnapshot Core);
public sealed record OpeningInventoryReceipt(SelectedSessionIdentity Session,
    ImmutableArray<OpeningInventoryUpdate> Updates, ImmutableArray<BusinessSlot> Cleared,
    ImmutableArray<OpeningRewardDisplay> Rewards, DateTimeOffset PreparedAt,
    ImmutableArray<AccountPremiumEntryState> ChangedPremiums = default,
    ImmutableArray<CharacterPetRuntimeState> Pets = default, bool CreaturesChanged = false);
public sealed record PackageOpeningResult(OpeningRejection Rejection, OpeningInventoryReceipt? Receipt = null, string? Detail = null);
public sealed record MagicBoxOpeningResult(OpeningRejection Rejection, OpeningInventoryReceipt? Receipt = null,
    ImmutableArray<OpeningRewardDisplay> Primary = default, ImmutableArray<OpeningRewardDisplay> Bonus = default,
    int? LuckBefore = null, int? LuckAfter = null, string? Detail = null);
public sealed class OpeningRejectedException(OpeningRejection reason, string detail) : Exception(detail)
{
    public OpeningRejection Reason { get; } = reason;
}
