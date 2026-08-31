using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.World;

/// <summary>The one reader of what the tutorial's steps hand over.</summary>
internal sealed class TutorialRewardRuleService(
    ITutorialRewardDefinitionSource rewards) : ITutorialRewardRuleService
{
    public ValueTask<IReadOnlyList<TutorialRewardItem>> RewardsForAsync(
        uint tutorialFlag, CancellationToken cancellationToken) =>
        rewards.GetAsync(tutorialFlag, cancellationToken);
}
