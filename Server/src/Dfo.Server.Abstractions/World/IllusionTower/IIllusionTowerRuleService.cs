namespace Dfo.Server.Abstractions.World.IllusionTower;

public interface IIllusionTowerRuleService
{
    ValueTask<IllusionTowerDefinition> GetDefinitionAsync(CancellationToken cancellationToken);
    ValueTask<TowerRoomSeed> CreateRoomAsync(int level, uint seed, CancellationToken cancellationToken);

    TimeSpan DefaultReturnDelay => TimeSpan.FromSeconds(60);
    TimeSpan? ReturnDelay(byte option) => option switch
    {
        0 or 1 => TimeSpan.FromSeconds(3),
        2 => TimeSpan.FromSeconds(1),
        3 => null,
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };
}

public interface IIllusionTowerContentSource
{
    ValueTask<IllusionTowerDefinition> GetAsync(CancellationToken cancellationToken);
}

public sealed record TowerPosition(int X, int Y, int Z);
public sealed record TowerRequiredItem(int ItemId, int Count, bool Consume);
public sealed record TowerActorDeclaration(
    int TemplateId, int LevelValue, bool RelativeLevel, byte ActorType,
    bool BlocksClear, TowerPosition Position);
public sealed record TowerApcCandidate(int TemplateId, byte Level, int AppearancePoint);
public sealed record TowerObjectDeclaration(int TemplateId, TowerPosition Position);
public sealed record TowerRoomDefinition(
    int Level, int MapId, IReadOnlyList<TowerActorDeclaration> Actors,
    int RandomApcPoints, IReadOnlyList<TowerPosition> RandomApcPositions,
    IReadOnlyList<TowerObjectDeclaration> Objects);

// These are PVF operands, not a claim that their entire reward formula is established.
public sealed record IllusionTowerRewardParameters(
    IReadOnlyList<double> ExperienceWeights, IReadOnlyList<int> CardCounts,
    IReadOnlyList<int> ItemProbabilityOperands, double GoldWeight,
    double ApcExperienceWeight, double MonsterExperienceWeight);
public sealed record IllusionTowerParameters(
    int MaxClearItemCount, int FpCubepiece, int StackableItemLimit, int ItemDrop,
    IReadOnlyList<int> HighSkillInitialCoolTime,
    IReadOnlyList<double> HighSkillInitialCoolTimeRate,
    IReadOnlyList<IReadOnlyList<int>> RecoveryOperands);
public sealed record IllusionTowerDefinition(
    int DungeonId, int MinimumLevel, int BasisLevel,
    IReadOnlyList<TowerRequiredItem> RequiredItems,
    IReadOnlyList<TowerRoomDefinition> Rooms,
    IReadOnlyList<TowerApcCandidate> RandomApcCandidates,
    IllusionTowerRewardParameters Rewards, IllusionTowerParameters Parameters);

public sealed record TowerActorSeed(
    int ListIndex, int TemplateId, byte Level, byte ActorType, bool BlocksClear,
    TowerPosition Position);
public sealed record TowerRoomSeed(
    int Level, int MapId, uint Seed, IReadOnlyList<TowerActorSeed> Actors,
    IReadOnlyList<TowerObjectDeclaration> Objects);
