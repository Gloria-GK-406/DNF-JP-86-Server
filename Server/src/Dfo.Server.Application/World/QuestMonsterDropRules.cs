using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal static class QuestMonsterDropRules
{
    public static IReadOnlyList<RolledDrop> DrawMonsterRewards(
        MonsterDropRequest kill, MonsterDropParticipant participant,
        IReadOnlyList<QuestDefinition> definitions, IReadOnlyDictionary<int, long> held)
    {
        var result = new List<RolledDrop>();
        var planned = new Dictionary<int, int>();
        foreach (var definition in definitions)
        {
            var random = new DropRandom(
                DropRandom.SeedFor(
                    kill.Context.Seed,
                    kill.Context.MapId,
                    kill.Context.Cell.X,
                    kill.Context.Cell.Y,
                    kill.ActorSequence)
                ^ 0x5155_4553_5444_524FUL
                ^ (ulong)(uint)participant.CharacterId.Value
                ^ ((ulong)(uint)definition.QuestId << 32));
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
                    QuestId: definition.QuestId,
                    GrantLimit: checked((int)limit)));
            }
        }
        return result;
    }

    // [enemy reward item] is not proven equivalent to [monster reward item].
    public static IReadOnlyList<RolledDrop> DrawEnemyRewards(
        MonsterDropRequest kill, MonsterDropParticipant participant,
        IReadOnlyList<QuestDefinition> definitions, IReadOnlyDictionary<int, long> held) => [];
}
