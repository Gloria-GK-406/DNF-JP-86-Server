using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

public sealed record HellRewardRateBand(int Low, int High, IReadOnlyList<int> Rates);

public sealed record HellRewardDefinition(
    IReadOnlyList<HellRewardRateBand> Bands,
    IReadOnlyList<IReadOnlyList<int>> RarityThresholds,
    IReadOnlyDictionary<int, ItemGradeBand> GradeBands,
    IReadOnlyDictionary<int, int> EpicPieces);

public interface IHellRewardDefinitionSource
{
    HellRewardDefinition Load();
    bool IsHellMonster(int code, bool isAiCharacter);
}

public sealed record HellActorReward(IReadOnlyList<RolledDrop> Loot, bool EligibleForGroupReward);

public sealed record HellGroupRewardContext(MonsterDropContext Drop, byte HellDifficulty,
    int GroupIndex, int BasisLevel);

public interface IHellRewardRuleService
{
    bool EligibleForGroupReward(int code, bool isAiCharacter);
    ValueTask<IReadOnlyList<RolledDrop>> DrawGroupAsync(HellGroupRewardContext context,
        CancellationToken cancellationToken);
}

public interface IHellRewardDomainService
{
    ValueTask<HellActorReward> FreezeActorAsync(int code, MonsterDropContext context,
        CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<RolledDrop>> FreezeGroupAsync(HellGroupRewardContext context,
        CancellationToken cancellationToken);
}
