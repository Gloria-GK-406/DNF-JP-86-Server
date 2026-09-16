using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.World;

/// <summary>Reads quest-owned monster item declarations without changing ordinary drop rules.</summary>
internal sealed class QuestMonsterDropRuleService(
    IQuestContentRuleService quests,
    ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers) : IQuestMonsterDropRuleService
{
    public async ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        MonsterKill kill,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kill);
        var purpose = kill.Context.Layout.Purpose;
        if (purpose.Kind != DungeonRunPurposeKind.QuestLinked
            || purpose.QuestState != DungeonQuestConnectionState.Active
            || purpose.QuestId is not { } questId)
        {
            return [];
        }

        var leader = kill.Context.Participants.SingleOrDefault(participant => participant.IsLeader);
        if (leader is null)
            return [];

        var carried = await logs.LogOf(leader.AccountId, leader.CharacterId)
            .FindCarriedAsync(questId, cancellationToken).ConfigureAwait(false);
        if (carried is null)
            return [];

        var definition = await quests.FindAsync(questId, cancellationToken).ConfigureAwait(false);
        if (definition is null || definition.MonsterRewardItems.Count == 0)
            return [];

        var bag = containers.FindContainer(leader.CharacterId, InventoryContainerType.Main);
        if (bag is null)
            return [];
        var held = (await bag.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(item => item.ItemId > 0)
            .GroupBy(item => item.ItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.Count));
        var planned = new Dictionary<int, int>();
        var random = new DropRandom(
            DropRandom.SeedFor(
                kill.Context.Layout.Seed,
                kill.Context.MapId,
                kill.Context.Cell.X,
                kill.Context.Cell.Y,
                kill.ActorSequence)
            ^ 0x5155_4553_5444_524FUL);
        var result = new List<RolledDrop>();
        foreach (var source in definition.MonsterRewardItems)
        {
            if (source.MonsterCode != kill.MonsterCode
                || (source.DungeonId >= 0 && source.DungeonId != kill.Context.Layout.DungeonId)
                || (source.Difficulty >= 0 && source.Difficulty != kill.Context.Difficulty))
            {
                continue;
            }

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
                OwnerCharacterId: leader.CharacterId));
        }

        return result;
    }
}
