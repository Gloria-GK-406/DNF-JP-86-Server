namespace Dfo.Server.Abstractions.Content;

/// <summary>One item a tutorial step hands over.</summary>
public readonly record struct TutorialRewardItem(int ItemId, int Count);

/// <summary>What the archive says each tutorial step gives.</summary>
public interface ITutorialRewardDefinitionSource
{
    /// <summary>
    /// The items declared for this tutorial flag, empty when it declares none.
    /// </summary>
    /// <remarks>
    /// Most flags declare none, and that is the ordinary answer rather than a missing entry: the
    /// archive shipped here declares rewards for exactly one of them.
    /// </remarks>
    ValueTask<IReadOnlyList<TutorialRewardItem>> GetAsync(
        uint tutorialFlag,
        CancellationToken cancellationToken);
}
