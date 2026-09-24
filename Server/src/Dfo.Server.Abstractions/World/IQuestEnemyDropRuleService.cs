namespace Dfo.Server.Abstractions.World;

/// <summary>Static quest declarations and supplied recipient facts; never queries character state.</summary>
public interface IQuestEnemyDropRuleService
{
    ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        QuestEnemyDropRequest request, MonsterDropParticipant participant,
        IReadOnlyList<int> questIds, IReadOnlyDictionary<int, long> held, bool hasQuestHelper,
        CancellationToken cancellationToken);
}
