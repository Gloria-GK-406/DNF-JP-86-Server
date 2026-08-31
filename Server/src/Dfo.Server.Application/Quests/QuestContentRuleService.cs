using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;

namespace Dfo.Server.Application.Quests;

/// <summary>
/// The one reader of what the archive declares about quests.
/// </summary>
/// <remarks>
/// Five places used to read the quest definitions and two the parameter table, across the entry
/// path, the three quest commands and the dungeon settlement. What each of them wants is a rule
/// answer about a quest; none of them wants to be the thing that knows where quests are kept.
/// </remarks>
internal sealed class QuestContentRuleService(
    IQuestDefinitionSource quests,
    IQuestParameterSource questParameters) : IQuestContentRuleService
{
    public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken cancellationToken) =>
        quests.GetAllAsync(cancellationToken);

    public ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken cancellationToken) =>
        quests.GetAsync(questId, cancellationToken);

    public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(
        CancellationToken cancellationToken) =>
        quests.GetTrainingAvailabilityAsync(cancellationToken);

    public QuestParameters Parameters() => questParameters.Load();
}
