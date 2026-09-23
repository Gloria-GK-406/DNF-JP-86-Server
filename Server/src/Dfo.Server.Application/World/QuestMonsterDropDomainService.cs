using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.World;

/// <summary>Reads quest-owned monster item declarations without changing ordinary drop rules.</summary>
internal sealed class QuestMonsterDropDomainService(
    IQuestMonsterDropRuleService rules,
    ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers) : IQuestMonsterDropDomainService
{
    public async ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        MonsterDropRequest kill,
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
