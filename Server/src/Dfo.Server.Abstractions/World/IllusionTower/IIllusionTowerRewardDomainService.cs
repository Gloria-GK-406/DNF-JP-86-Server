using System.Collections.Immutable;
namespace Dfo.Server.Abstractions.World.IllusionTower;

public interface IIllusionTowerRewardRuleService
{
    ValueTask<uint> KillExperienceAsync(int characterLevel, byte actorType, CancellationToken cancellationToken);
    ValueTask<uint> ClearExperienceAsync(int characterLevel, int rewardLevel, CancellationToken cancellationToken);
    ValueTask<ImmutableArray<TowerRewardCandidate>> CandidatesAsync(int characterLevel, int rewardLevel,
        ulong seed, CancellationToken cancellationToken);
}

// The caller owns the challenge and its transaction. This capability owns no run state.
public interface IIllusionTowerRewardDomainService
{
    ValueTask<ImmutableArray<TowerKillReward>> KillAsync(IllusionTowerSnapshot run, TowerActor actor,
        CancellationToken cancellationToken);
    ValueTask<TowerSettlementReceipt> SettleAsync(IllusionTowerSnapshot run, ulong seed,
        CancellationToken cancellationToken);
}
