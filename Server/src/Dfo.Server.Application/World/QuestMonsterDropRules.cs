using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal static class QuestMonsterDropRules
{
    public static IReadOnlyList<RolledDrop> Draw(
        MonsterDropRequest kill, MonsterDropParticipant participant,
        IReadOnlyList<QuestDefinition> definitions, IReadOnlyDictionary<int, long> held) =>
        Draw(kill, participant, definitions, held, kill.Context.SourceType switch
        {
            IndependentDropSourceType.Monster => QuestEnemyType.Monster,
            IndependentDropSourceType.APC => QuestEnemyType.APC,
            _ => throw new ArgumentOutOfRangeException(nameof(kill)),
        });

    internal static IReadOnlyList<RolledDrop> Draw(
        MonsterDropRequest kill, MonsterDropParticipant participant,
        IReadOnlyList<QuestDefinition> definitions, IReadOnlyDictionary<int, long> held,
        QuestEnemyType enemyType) =>
        Draw(QuestEnemyDropRequest.FromMonster(kill) with { EnemyType = enemyType }, participant, definitions, held);

    internal static IReadOnlyList<RolledDrop> Draw(
        QuestEnemyDropRequest kill, MonsterDropParticipant participant,
        IReadOnlyList<QuestDefinition> definitions, IReadOnlyDictionary<int, long> held)
    {
        var result = new List<RolledDrop>();
        var planned = new Dictionary<int, int>();
        foreach (var definition in definitions)
        {
            var random = new DropRandom(DropRandom.SeedFor(kill.Seed, kill.MapId,
                kill.Cell.X, kill.Cell.Y, kill.InstanceId)
                ^ 0x5155_4553_5444_524FUL ^ (ulong)(uint)participant.CharacterId.Value
                ^ ((ulong)(uint)definition.QuestId << 32));
            var candidates = definition.EnemyRewardItems.Where(source =>
                source.EnemyType == kill.EnemyType && source.EnemyCode == kill.EnemyCode
                && (source.DungeonId == -1 || source.DungeonId == kill.DungeonId)
                && (source.Difficulty == -1 || source.Difficulty == (int)kill.Difficulty)).ToList();
            while (candidates.Count > 0)
            {
                var selected = random.Next(candidates.Count) - 1;
                var source = candidates[selected];
                candidates.RemoveAt(selected);
                var count = 0;
                for (var attempt = 0; attempt < source.Count; attempt++)
                    if (random.Hits(source.DropRate, 100)) count++;
                // A probability miss ends this task's attempt, not a retry of other rows.
                if (count == 0) break;

                var required = definition.SeekingItems?
                    .Where(requirement => requirement.ItemId == source.ItemId)
                    .Sum(requirement => (long)requirement.RequiredCount) ?? 0;
                var limit = source.MaxStack == -1 ? required : Math.Min(required, source.MaxStack);
                var alreadyHeld = held.GetValueOrDefault(source.ItemId)
                    + planned.GetValueOrDefault(source.ItemId);
                // Retain the server's seeking cap, including first insertion. A full candidate
                // is removed before retrying the remaining pool, as in the reference ELF.
                if (alreadyHeld >= limit) continue;
                count = checked((int)Math.Min(count, limit - alreadyHeld));
                planned[source.ItemId] = checked(planned.GetValueOrDefault(source.ItemId) + count);
                result.Add(new RolledDrop(source.ItemId, count, DropRarity.Common, DropItemKind.Stackable,
                    OwnerCharacterId: participant.CharacterId, Delivery: RolledDropDelivery.DirectQuestItem,
                    QuestId: definition.QuestId, GrantLimit: checked((int)limit)));
                break;
            }
        }
        return result;
    }
}
