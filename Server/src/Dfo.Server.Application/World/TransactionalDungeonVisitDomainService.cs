using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.World;

// Existing entry/move handlers use this boundary directly. It shares the command pipeline's
// transaction scope, so balance and staged runtime movement settle together.
internal sealed class TransactionalDungeonVisitDomainService(DungeonVisitDomainService inner,
    DomainTransactionScope scope) : IDungeonVisitDomainService
{
    public bool DefersSelectionState(byte level, bool tutorialCompleted = false) => inner.DefersSelectionState(level, tutorialCompleted);
    // The entry or move unit settles the visit; it is never opened here.
    private ValueTask<T> Execute<T>(Func<ValueTask<T>> action, CancellationToken token)
    {
        if (scope.Current is null) throw new InvalidOperationException("Dungeon visits require the caller's transaction.");
        return action();
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
    public ValueTask<DungeonEntryAttempt> EnterGrantedPartyAsync(IReadOnlyList<DungeonRunParticipant> members,
        DungeonLayout layout, DungeonEntryFacts facts, IDungeonAdmissionReceipt receipt, CancellationToken token) =>
        Execute(() => inner.EnterGrantedPartyAsync(members, layout, facts, receipt, token), token);
    public ValueTask<DungeonRoomVisit?> MoveByLostLandItemAsync(CharacterId id, DungeonRunState current, MazeCell target, CancellationToken token) =>
        Execute(() => inner.MoveByLostLandItemAsync(id, current, target, token), token);
    public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId id, DungeonRunState current, MazeCell target, CancellationToken token) =>
        Execute(() => inner.MoveAsync(id, current, target, token), token);
}
