using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>What a tutorial step declares it hands over.</summary>
internal sealed class TutorialRewardDomainService(
    ITutorialRewardRuleService rewards) : ITutorialRewardDomainService
{
    public ValueTask<IReadOnlyList<TutorialRewardItem>> DeclaredForAsync(
        uint tutorialFlag, CancellationToken cancellationToken) =>
        rewards.RewardsForAsync(tutorialFlag, cancellationToken);
}
