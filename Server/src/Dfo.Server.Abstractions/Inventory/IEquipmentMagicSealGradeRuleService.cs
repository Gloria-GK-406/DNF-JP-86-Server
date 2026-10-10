using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record EquipmentMagicSealGradeDecision(EquipmentQualityRejection Rejection,
    RandomOptionState? Option = null);

public interface IEquipmentMagicSealGradeRuleService
{
    ValueTask<EquipmentMagicSealGradeDecision> RollAsync(int equipmentId, int materialId,
        RandomOptionState selected, int randomSeed, CancellationToken ct);
}
