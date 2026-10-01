using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record EquipmentMagicSealRuleDecision(
    EquipmentMagicSealRejection Rejection,
    IReadOnlyList<RandomOptionState>? Options = null,
    int GoldCost = 0,
    int Seed = 0);

/// <summary>Content and explicit-seed rule evaluation, without character state.</summary>
public interface IEquipmentMagicSealRuleService
{
    ValueTask<EquipmentMagicSealRuleDecision> DecideAsync(int equipmentItemId,
        IReadOnlyList<RandomOptionState> existingOptions, byte? changedIndex,
        int seed, CancellationToken cancellationToken);
}

public interface IEquipmentMagicSealSeedSource
{
    int NextSeed();
}
