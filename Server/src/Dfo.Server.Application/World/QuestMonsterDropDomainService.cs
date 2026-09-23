using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.World;

/// <summary>Reads quest-owned monster item declarations without changing ordinary drop rules.</summary>
internal sealed class QuestMonsterDropDomainService(
    IQuestContentRuleService quests,
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
        var planned = new Dictionary<int, int>();
        var carried = await logs.LogOf(participant.AccountId, participant.CharacterId)
            .CarriedAsync(cancellationToken).ConfigureAwait(false);
        foreach (var active in carried)
        {
            var definition = await quests.FindAsync(active.QuestId, cancellationToken)
                .ConfigureAwait(false);
            if (definition is null || definition.MonsterRewardItems.Count == 0)
                continue;
            var random = new DropRandom(
                DropRandom.SeedFor(
                    kill.Context.Seed,
                    kill.Context.MapId,
                    kill.Context.Cell.X,
                    kill.Context.Cell.Y,
                    kill.ActorSequence)
                ^ 0x5155_4553_5444_524FUL
                ^ (ulong)(uint)participant.CharacterId.Value
                ^ ((ulong)(uint)active.QuestId << 32));
            foreach (var source in definition.MonsterRewardItems)
            {
                if (source.MonsterCode != kill.MonsterCode
                    || (source.DungeonId >= 0 && source.DungeonId != kill.Context.DungeonId)
                    || (source.Difficulty >= 0 && source.Difficulty != (int)kill.Context.Difficulty))
                    continue;

                var required = definition.SeekingItems?
                    .Where(requirement => requirement.ItemId == source.ItemId)
                    .Sum(requirement => (long)requirement.RequiredCount) ?? 0;
                if (required <= 0)
                    continue;

                var limit = source.MaxStack < 0
                    ? required
                    : Math.Min(required, source.MaxStack);
                var alreadyHeld = held.GetValueOrDefault(source.ItemId)
                    + planned.GetValueOrDefault(source.ItemId);
                if (alreadyHeld >= limit)
                    continue;

                var count = 0;
                for (var attempt = 0; attempt < source.Count; attempt++)
                {
                    if (random.Hits(source.DropRate, 100))
                        count++;
                }
                count = checked((int)Math.Min(count, limit - alreadyHeld));
                if (count <= 0)
                    continue;

                planned[source.ItemId] = planned.GetValueOrDefault(source.ItemId) + count;
                result.Add(new RolledDrop(
                    source.ItemId,
                    count,
                    DropRarity.Common,
                    DropItemKind.Stackable,
                    OwnerCharacterId: participant.CharacterId,
                    Delivery: RolledDropDelivery.DirectQuestItem,
                    QuestId: active.QuestId,
                    GrantLimit: checked((int)limit)));
            }
        }

        return result;
    }
}
