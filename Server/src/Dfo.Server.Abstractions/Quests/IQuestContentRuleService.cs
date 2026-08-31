using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>
/// What the archive declares about quests: the quests themselves, which of them a trainer offers at
/// which level, and the shared parameters a reward is worked out from.
/// </summary>
/// <remarks>
/// Two archive declarations answer to one subject. A quest's own definition says what it asks for
/// and what it hands over; the shared parameter table says how much a completion is worth once the
/// level gap is applied. Nothing decides a quest's outcome without both, which is why they arrive
/// together rather than through two seams that each half of the server reaches for separately.
/// </remarks>
public interface IQuestContentRuleService
{
    /// <summary>Every quest the content defines, in list order.</summary>
    ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken cancellationToken);

    /// <summary>One quest, or null when the content does not define it.</summary>
    ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken cancellationToken);

    /// <summary>Training-quest NPC availability in authoritative level order.</summary>
    ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(
        CancellationToken cancellationToken);

    /// <summary>The shared parameters a quest reward is worked out from.</summary>
    QuestParameters Parameters();
}
