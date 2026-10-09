using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public enum EquipmentQualityRejection { None, NoSession, InvalidRequest, ItemUnavailable, Locked, MaterialUnavailable, UnsupportedContent, InvalidOption, UnmodifiableOption }
public sealed record EquipmentQualityReceipt(SelectedSessionIdentity Session, BusinessSlot TargetSlot,
    BusinessSlot MaterialSlot, ItemSnapshot Equipment, int MaterialRemaining, DateTimeOffset At)
{
    public ItemSnapshot? Material { get; init; }
}
public sealed record EquipmentQualityResult(EquipmentQualityRejection Rejection, EquipmentQualityReceipt? Receipt = null);
public interface IEquipmentQualityDomainService
{
    ValueTask<EquipmentQualityResult> RerollAsync(SelectedSessionIdentity session, BusinessSlot target,
        int expectedItemId, BusinessSlot material, CancellationToken ct);

    ValueTask<EquipmentQualityResult> RerollMagicSealGradeAsync(SelectedSessionIdentity session,
        BusinessSlot target, BusinessSlot material, ushort optionIndex, CancellationToken ct);
}

public interface IEquipmentQualityRuleService
{
    ValueTask<int?> RollAsync(int equipmentId, int materialId, int randomSeed, CancellationToken ct);
}

public interface IEquipmentQualitySeedSource
{
    int Next();
}
