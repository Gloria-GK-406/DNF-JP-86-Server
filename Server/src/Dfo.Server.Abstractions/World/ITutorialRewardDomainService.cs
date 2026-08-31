using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>What a tutorial step declares it hands over.</summary>
/// <remarks>
/// The declaration and the handing over are two things. What a step gives is content; putting it in
/// a character's bag is a transactional command, because a step can declare several items and they
/// settle together. This answers the first half, so that whoever reports the flag does not have to
/// know where the rule is kept.
/// </remarks>
public interface ITutorialRewardDomainService
{
    /// <summary>What this tutorial flag declares, empty when it declares nothing.</summary>
    ValueTask<IReadOnlyList<TutorialRewardItem>> DeclaredForAsync(
        uint tutorialFlag, CancellationToken cancellationToken);
}
