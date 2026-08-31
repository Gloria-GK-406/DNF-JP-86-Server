namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// What a tutorial step hands over.
/// </summary>
/// <remarks>
/// One question, and the archive is the whole of its answer. It is a rule service rather than a
/// bare source because what a step gives is a rule about the tutorial, and the step that reports a
/// flag has no business knowing where that rule is kept.
/// </remarks>
public interface ITutorialRewardRuleService
{
    /// <summary>
    /// What this tutorial flag declares, empty when it declares nothing.
    /// </summary>
    /// <remarks>
    /// Most flags declare nothing, and that is the ordinary answer rather than a missing entry: the
    /// archive shipped here declares rewards for exactly one of them.
    /// </remarks>
    ValueTask<IReadOnlyList<TutorialRewardItem>> RewardsForAsync(
        uint tutorialFlag, CancellationToken cancellationToken);
}
