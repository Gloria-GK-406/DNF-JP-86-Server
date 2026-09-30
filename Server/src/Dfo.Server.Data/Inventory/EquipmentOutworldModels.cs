using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum EquipmentOutworldAction : byte
{
    Purify = 0,
    Invest = 1,
    Twist = 2,
}

public enum EquipmentOutworldRejection
{
    None,
    NoSession,
    InvalidRequest,
    ItemUnavailable,
    WrongItem,
    MaterialUnavailable,
    Locked,
    UnsupportedContent,
    InvalidState,
    SameOption,
    AlreadyUpgraded,
    AlreadyHasAmplifyOption,
    NoAmplifyOption,
}

public sealed record EquipmentOutworldDecision(
    EquipmentOutworldRejection Rejection,
    int MaterialCount = 0,
    byte AmplifyType = 0,
    ushort AmplifyValue = 0,
    bool Cleared = false,
    int Seed = 0);

public sealed record EquipmentOutworldReceipt(
    SelectedSessionIdentity Session,
    EquipmentOutworldAction Action,
    BusinessSlot TargetSlot,
    int TargetItemId,
    BusinessSlot MaterialSlot,
    int MaterialItemId,
    int MaterialRemaining,
    InventoryItemCoreState UpdatedCore,
    bool Cleared,
    int Seed,
    DateTimeOffset CommittedAt);

public sealed record EquipmentOutworldResult(
    EquipmentOutworldRejection Rejection,
    EquipmentOutworldReceipt? Receipt = null);
