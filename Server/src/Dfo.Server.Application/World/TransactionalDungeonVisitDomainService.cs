using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.World;

// Existing entry/move handlers use this boundary directly. It shares the command pipeline's
// writer gate and transaction scope, so balance and staged runtime movement settle together.
internal sealed class TransactionalDungeonVisitDomainService(DungeonVisitDomainService inner,
    DomainTransactionScope scope, TransactionalExecutionGate gate) : IDungeonVisitDomainService
{
    public bool DefersSelectionState(byte level, bool tutorialCompleted = false) => inner.DefersSelectionState(level, tutorialCompleted);
    private async ValueTask<T> Execute<T>(Func<ValueTask<T>> action, CancellationToken token)
    {
        if (scope.Current is not null) return await action().ConfigureAwait(false);
        await using var admission = await gate.AcquireAsync(token).ConfigureAwait(false);
        await using var transaction = scope.Begin();
        var result = await action().ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return result;
    }
    public ValueTask<DungeonEntry?> EnterAsync(CharacterId id, int dungeon, DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken token) =>
        Execute(() => inner.EnterAsync(id, dungeon, difficulty, facts, token), token);
    public ValueTask<DungeonPartyEntry?> EnterPartyAsync(IReadOnlyList<DungeonRunParticipant> members, int dungeon, DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken token) =>
        Execute(() => inner.EnterPartyAsync(members, dungeon, difficulty, facts, token), token);
    public ValueTask<DungeonEntryAttempt> TryEnterPartyAsync(IReadOnlyList<DungeonRunParticipant> members, int dungeon, DungeonEntryFacts facts, CancellationToken token) =>
        Execute(() => inner.TryEnterPartyAsync(members, dungeon, facts, token), token);
    public ValueTask<DungeonEntryAttempt> TryEnterPreparedPartyAsync(IReadOnlyList<DungeonRunParticipant> members,
        DungeonLayout layout, DungeonEntryFacts facts, CancellationToken token) =>
        Execute(() => inner.TryEnterPreparedPartyAsync(members, layout, facts, token), token);
    public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId id, DungeonRunState current, MazeCell target, CancellationToken token) =>
        Execute(() => inner.MoveAsync(id, current, target, token), token);
}
