namespace Dfo.Server.Abstractions.World;

/// <summary>Static quest declarations and supplied recipient facts; never queries character state.</summary>
public interface IQuestMonsterDropRuleService
{
    ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        MonsterDropRequest request, MonsterDropParticipant participant,
        IReadOnlyList<int> questIds, IReadOnlyDictionary<int, long> held,
        CancellationToken cancellationToken);
}
