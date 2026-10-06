using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum EquipmentForgingRejection
{
    None,
    NoSession,
    InvalidRequest,
    ItemUnavailable,
    WrongItem,
    Locked,
    Expired,
    NotWeapon,
    Durability,
    MaxLevel,
    MaterialUnavailable,
    UnsupportedContent,
}

public sealed record EquipmentForgingDecision(
    EquipmentForgingRejection Rejection,
    int MaterialItemId = 0,
    int MaterialCount = 0,
    bool Succeeded = false,
    byte NewLevel = 0,
    int SuccessWeight = 0,
    int Roll = 0);

public sealed record EquipmentForgingReceipt(
    SelectedSessionIdentity Session,
    BusinessSlot TargetSlot,
    int TargetItemId,
    BusinessSlot MaterialSlot,
    int MaterialItemId,
    int MaterialRemaining,
    ItemSnapshot UpdatedCore,
    byte OldLevel,
    byte NewLevel,
    bool Succeeded,
    int SuccessWeight,
    int Roll,
    DateTimeOffset CommittedAt);

public sealed record EquipmentForgingResult(
    EquipmentForgingRejection Rejection,
    EquipmentForgingReceipt? Receipt = null);
