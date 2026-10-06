using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public sealed record QuestEnemyDropRequest(
    QuestEnemyType EnemyType, int EnemyCode, int InstanceId,
    int DungeonId, DungeonDifficulty Difficulty, int Seed, int PositionX, int PositionY, int MapId)
{
    public static QuestEnemyDropRequest FromMonster(MonsterDropRequest request) => new(
        request.Context.SourceType switch
        {
            IndependentDropSourceType.Monster => QuestEnemyType.Monster,
            IndependentDropSourceType.APC => QuestEnemyType.APC,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        },
        request.MonsterCode, request.ActorSequence, request.Context.DungeonId,
        request.Context.Difficulty, request.Context.Seed,
        request.Context.Cell.X, request.Context.Cell.Y, request.Context.MapId);
}

public interface IQuestEnemyDropDomainService
{
    ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        QuestEnemyDropRequest request, MonsterDropParticipant participant, CancellationToken cancellationToken);
}
