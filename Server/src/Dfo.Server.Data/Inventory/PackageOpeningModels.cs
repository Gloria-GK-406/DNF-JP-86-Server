using System.Collections.Immutable;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Inventory;

public sealed record MagicBoxOpeningInput(byte ListType, int SourceSlot, int ExpectedItemId,
    int MaterialSlot, int ExpectedMaterialId, int Count);
/// <summary>The self-selection window's category coordinates: the first and second drop-down indexes.
/// A [booster select category] block declares its pair; the 0x00A0 selection form carries the chosen one.</summary>
public readonly record struct OpeningCategory(byte First, byte Second);
public sealed record PackageOpeningInput(int SourceSlot, ImmutableArray<int> SelectedItems)
{
    public OpeningCategory Category { get; init; }
}
public sealed record AvatarPackageChoice(int ItemId, byte Ability);
public sealed record AvatarPackageOpeningInput(int SourceSlot, ImmutableArray<AvatarPackageChoice> Choices)
{
    public OpeningCategory Category { get; init; }
}
public sealed record BusinessMagicBoxOpeningInput(byte ListType, BusinessSlot SourceSlot, int ExpectedItemId,
    BusinessSlot? MaterialSlot, int ExpectedMaterialId, int Count);
public sealed record BusinessPackageOpeningInput(BusinessSlot SourceSlot, ImmutableArray<int> SelectedItems)
{
    public OpeningCategory Category { get; init; }
}
public sealed record BusinessAvatarPackageOpeningInput(BusinessSlot SourceSlot, ImmutableArray<AvatarPackageChoice> Choices)
{
    public OpeningCategory Category { get; init; }
}
public enum OpeningRejection { None, NoSession, InvalidRequest, WrongItem, MissingMaterial, UnsupportedContent, InvalidChoice, NoRoom, StateChanged, StorageFailure, ItemUnusable, PremiumUnavailable,
    /// <summary>An equipment input still carries unpurified other-world amplification (type 0x80).</summary>
    EquipmentUnpurified,
    /// <summary>An equipment input is not fully repaired.</summary>
    EquipmentUnrepaired }
public sealed record OpeningReward(int ItemId, int Count, byte Ability = 0, int PeriodDays = 0)
{
    public byte TradeRestriction { get; init; }
    /// <summary>Equipment reinforcement from [booster equipment upgrade]; <see cref="RandomUpgrade"/> draws it from <see cref="UpgradeChances"/>.</summary>
    public byte Upgrade { get; init; }
    public ImmutableArray<OpeningUpgradeChance> UpgradeChances { get; init; } = [];
    /// <summary>Equipment forging level from [booster equipment separate] (the description names it 锻造).</summary>
    public byte Forging { get; init; }
    /// <summary>Equipment quality tier 0..4 from [booster equipment grade]; null keeps a random quality.</summary>
    public byte? QualityTier { get; init; }
    /// <summary>An avatar row's first extension: the index into the avatar's own [avatar type select] rows,
    /// whose period it takes. Out of range (the client stores it as u16, so -1 is 65535) keeps the avatar's own period.</summary>
    public int? AvatarTypeRow { get; init; }
    /// <summary>The pool row's broadcast column is 1: drawing this reward is told to the whole server.</summary>
    public bool Announce { get; init; }
    public const byte RandomUpgrade = 255;
}
/// <summary>One [random probability] pair: a reinforcement level and its weight.</summary>
public sealed record OpeningUpgradeChance(byte Level, int Weight);
public sealed record OpeningRewardDisplay(BusinessSlot? Slot, int ItemId, int Count, ushort Durability, byte Ability, int Expiration,
    ItemSnapshot? Snapshot = null);
public sealed record OpeningInventoryUpdate(BusinessSlot Slot, ItemIdentity? Identity, ItemSnapshot Core);
public sealed record OpeningInventoryReceipt(SelectedSessionIdentity Session,
    ImmutableArray<OpeningInventoryUpdate> Updates, ImmutableArray<BusinessSlot> Cleared,
    ImmutableArray<OpeningRewardDisplay> Rewards, DateTimeOffset PreparedAt,
    ImmutableArray<AccountPremiumEntryState> ChangedPremiums = default,
    ImmutableArray<CharacterPetRuntimeState> Pets = default, bool CreaturesChanged = false)
{
    /// <summary>Exhausted slots occupied again during settlement; clear their old client objects before updates.</summary>
    public ImmutableArray<BusinessSlot> Replaced { get; init; } = [];
}
public sealed record PackageOpeningResult(OpeningRejection Rejection, OpeningInventoryReceipt? Receipt = null, string? Detail = null);
public sealed record MagicBoxOpeningResult(OpeningRejection Rejection, OpeningInventoryReceipt? Receipt = null,
    ImmutableArray<OpeningRewardDisplay> Primary = default, ImmutableArray<OpeningRewardDisplay> Bonus = default,
    int? LuckBefore = null, int? LuckAfter = null, string? Detail = null);
public sealed class OpeningRejectedException(OpeningRejection reason, string detail) : Exception(detail)
{
    public OpeningRejection Reason { get; } = reason;
}
