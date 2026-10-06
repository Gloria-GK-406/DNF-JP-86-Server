using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public enum EquipmentMagicSealRejection
{
    None,
    NoSession,
    InvalidRequest,
    ItemUnavailable,
    Locked,
    UnsupportedContent,
    AlreadyUnsealed,
    NotUnsealed,
    InvalidOption,
    MaterialUnavailable,
    InsufficientGold,
}

public sealed record EquipmentMagicSealReceipt(
    SelectedSessionIdentity Session, BusinessSlot TargetSlot,
    ItemSnapshot UpdatedCore, int GoldRemaining,
    int Seed, DateTimeOffset CommittedAt);

public sealed record EquipmentMagicSealResult(
    EquipmentMagicSealRejection Rejection, EquipmentMagicSealReceipt? Receipt = null);
