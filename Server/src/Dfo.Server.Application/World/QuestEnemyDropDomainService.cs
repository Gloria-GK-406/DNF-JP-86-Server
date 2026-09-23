using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.World;

/// <summary>Reads current recipient facts for quest rewards, independently of ground drops.</summary>
internal sealed class QuestEnemyDropDomainService(
    IQuestEnemyDropRuleService rules,
    ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers) : IQuestEnemyDropDomainService
{
    internal ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        MonsterDropRequest request, MonsterDropParticipant participant, CancellationToken cancellationToken) =>
        this.DrawAsync(QuestEnemyDropRequest.FromMonster(request), participant, cancellationToken);

    public async ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        QuestEnemyDropRequest kill,
        MonsterDropParticipant participant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kill);
        var result = new List<RolledDrop>();
        var bag = containers.FindContainer(participant.CharacterId, InventoryContainerType.Main);
        if (bag is null)
            return result;
        var held = (await bag.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(item => item.ItemId > 0)
            .GroupBy(item => item.ItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.Count));
        var carried = await logs.LogOf(participant.AccountId, participant.CharacterId)
            .CarriedAsync(cancellationToken).ConfigureAwait(false);
        return await rules.DrawAsync(kill, participant,
            carried.Select(active => active.QuestId).ToArray(), held, cancellationToken)
            .ConfigureAwait(false);
    }
}
