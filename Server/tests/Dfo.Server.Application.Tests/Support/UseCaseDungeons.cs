using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>The registered towers and dungeon visits, each write of which is settled by <see cref="TestUseCase"/>.</summary>
/// <remarks>
/// These services join the caller's transaction and refuse without one. A test driving one directly stands in for
/// the command: a call inside the test's own transaction joins it, any other call commits when it returns, and a
/// throw rolls back. Reads served without a transaction pass straight through.
/// </remarks>
internal static class UseCaseDungeons
{
    public static IBossTowerDomainService Boss(IServiceProvider services) =>
        new BossTower(services.GetRequiredService<IBossTowerDomainService>(), services);

    public static IIllusionTowerDomainService Illusion(IServiceProvider services) =>
        new IllusionTower(services.GetRequiredService<IIllusionTowerDomainService>(), services);

    public static IDungeonVisitDomainService Visits(IServiceProvider services) =>
        Visits(services.GetRequiredService<IDungeonVisitDomainService>(),
            (DomainTransactionScope)services.GetRequiredService<IDomainTransactionAccessor>());

    /// <summary>For a test that builds its visit service by hand around its own scope.</summary>
    public static IDungeonVisitDomainService Visits(IDungeonVisitDomainService inner, DomainTransactionScope scope) =>
        new DungeonVisits(inner, scope);

    private sealed class BossTower(IBossTowerDomainService inner, IServiceProvider services) : IBossTowerDomainService
    {
        private ValueTask<T> Write<T>(Func<ValueTask<T>> call) => new(TestUseCase.RunAsync(services, call));

        public bool Supports(int dungeonId) => inner.Supports(dungeonId);
        public BossTowerSnapshot? Find(SelectedSessionIdentity session) => inner.Find(session);
        public bool IsInTower(CharacterId characterId) => inner.IsInTower(characterId);
        public IReadOnlyList<TowerActionContext> FindReturnsDue(DateTimeOffset now) => inner.FindReturnsDue(now);
        public bool CanShop(SelectedSessionIdentity session, int npcId, int shopId) => inner.CanShop(session, npcId, shopId);

        public ValueTask<BossTowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity leader,
            uint seed, CancellationToken ct, byte requestMode = 0, byte difficulty = 0, byte practiceMode = 0) =>
            Write(() => inner.EnterAsync(members, leader, seed, ct, requestMode, difficulty, practiceMode));
        public ValueTask<BossTowerActionResult> LoadAsync(TowerActionContext context, CancellationToken ct) =>
            Write(() => inner.LoadAsync(context, ct));
        public ValueTask<BossTowerActionResult> StartAsync(TowerActionContext context, CancellationToken ct) =>
            Write(() => inner.StartAsync(context, ct));
        public ValueTask<BossTowerActionResult> KillAsync(TowerActionContext context, ushort uid, IEffectFootprintRecorder footprint, CancellationToken ct) =>
            Write(() => inner.KillAsync(context, uid, footprint, ct));
        public ValueTask<BossTowerActionResult> NextLevelAsync(TowerActionContext context, byte targetX, byte targetY, CancellationToken ct) =>
            Write(() => inner.NextLevelAsync(context, targetX, targetY, ct));
        public ValueTask<BossTowerPickupResult> PickupAsync(TowerActionContext context, ushort uid, CancellationToken ct) =>
            Write(() => inner.PickupAsync(context, uid, ct));
        public ValueTask<BossTowerActionResult> DieAsync(TowerActionContext context, IEffectFootprintRecorder footprint, CancellationToken ct) =>
            Write(() => inner.DieAsync(context, footprint, ct));
        public ValueTask<BossTowerActionResult> LeaveAsync(TowerActionContext context, bool disconnected, IEffectFootprintRecorder footprint, CancellationToken ct) =>
            Write(() => inner.LeaveAsync(context, disconnected, footprint, ct));
        public ValueTask<BossTowerActionResult> SynchronizeLeaderAsync(TowerActionContext context, SelectedSessionIdentity leader, CancellationToken ct) =>
            Write(() => inner.SynchronizeLeaderAsync(context, leader, ct));
        public ValueTask<bool> CanRetryAsync(TowerActionContext context, CancellationToken ct) =>
            Write(() => inner.CanRetryAsync(context, ct));
        public ValueTask<BossTowerActionResult> SelectResultAsync(TowerActionContext context, byte option, CancellationToken ct) =>
            Write(() => inner.SelectResultAsync(context, option, ct));
        public ValueTask<BossTowerActionResult> ReleaseForSelectionAsync(TowerActionContext context, int requestedDungeonId, CancellationToken ct) =>
            Write(() => inner.ReleaseForSelectionAsync(context, requestedDungeonId, ct));
    }

    private sealed class IllusionTower(IIllusionTowerDomainService inner, IServiceProvider services) : IIllusionTowerDomainService
    {
        private ValueTask<T> Write<T>(Func<ValueTask<T>> call) => new(TestUseCase.RunAsync(services, call));

        public IllusionTowerSnapshot? Find(SelectedSessionIdentity session) => inner.Find(session);
        public bool IsInTower(CharacterId characterId) => inner.IsInTower(characterId);
        public bool TryReserveOrdinaryEntry(IReadOnlyList<CharacterId> members) => inner.TryReserveOrdinaryEntry(members);
        public IReadOnlyList<TowerActionContext> FindReturnsDue(DateTimeOffset now) => inner.FindReturnsDue(now);
        public ValueTask<bool> SupportsDungeonAsync(int dungeonId, CancellationToken cancellationToken) =>
            inner.SupportsDungeonAsync(dungeonId, cancellationToken);

        public ValueTask<TowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity leader,
            uint seed, CancellationToken cancellationToken, int dungeonId) =>
            Write(() => inner.EnterAsync(members, leader, seed, cancellationToken, dungeonId));
        public ValueTask<TowerActionResult> LoadAsync(TowerActionContext context, CancellationToken cancellationToken) =>
            Write(() => inner.LoadAsync(context, cancellationToken));
        public ValueTask<TowerActionResult> StartAsync(TowerActionContext context, CancellationToken cancellationToken) =>
            Write(() => inner.StartAsync(context, cancellationToken));
        public ValueTask<TowerActionResult> KillAsync(TowerActionContext context, ushort actorUid, IEffectFootprintRecorder footprint, CancellationToken cancellationToken) =>
            Write(() => inner.KillAsync(context, actorUid, footprint, cancellationToken));
        public ValueTask<TowerPickupResult> PickupAsync(TowerActionContext context, ushort groundUid, CancellationToken token) =>
            Write(() => inner.PickupAsync(context, groundUid, token));
        public ValueTask<TowerActionResult> NextLevelAsync(TowerActionContext context, CancellationToken cancellationToken) =>
            Write(() => inner.NextLevelAsync(context, cancellationToken));
        public ValueTask<TowerActionResult> DieAsync(TowerActionContext context, IEffectFootprintRecorder footprint, CancellationToken cancellationToken) =>
            Write(() => inner.DieAsync(context, footprint, cancellationToken));
        public ValueTask<TowerActionResult> LeaveAsync(TowerActionContext context, bool disconnected, IEffectFootprintRecorder footprint, CancellationToken cancellationToken) =>
            Write(() => inner.LeaveAsync(context, disconnected, footprint, cancellationToken));
        public ValueTask<TowerActionResult> SynchronizeLeaderAsync(TowerActionContext context, SelectedSessionIdentity leader, CancellationToken cancellationToken) =>
            Write(() => inner.SynchronizeLeaderAsync(context, leader, cancellationToken));
        public ValueTask<bool> CanContinueAsync(TowerActionContext context, CancellationToken cancellationToken) =>
            Write(() => inner.CanContinueAsync(context, cancellationToken));
        public ValueTask<TowerActionResult> MarkSettledAsync(TowerActionContext context, TowerSettlementReceipt receipt, CancellationToken cancellationToken) =>
            Write(() => inner.MarkSettledAsync(context, receipt, cancellationToken));
        public ValueTask<TowerActionResult> SelectReturnAsync(TowerActionContext context, byte option, CancellationToken cancellationToken) =>
            Write(() => inner.SelectReturnAsync(context, option, cancellationToken));
    }

    private sealed class DungeonVisits(IDungeonVisitDomainService inner, DomainTransactionScope scope) : IDungeonVisitDomainService
    {
        private ValueTask<T> Write<T>(Func<ValueTask<T>> call) => new(TestUseCase.RunAsync(scope, call));

        public bool DefersSelectionState(byte characterLevel, bool tutorialCompleted = false) => inner.DefersSelectionState(characterLevel, tutorialCompleted);

        public ValueTask<DungeonEntryAttempt> EnterGrantedPartyAsync(IReadOnlyList<DungeonRunParticipant> participants, DungeonLayout layout,
            DungeonEntryFacts facts, IDungeonAdmissionReceipt receipt, CancellationToken ct) =>
            Write(() => inner.EnterGrantedPartyAsync(participants, layout, facts, receipt, ct));
        public ValueTask<DungeonEntryAttempt> TryEnterPreparedPartyAsync(IReadOnlyList<DungeonRunParticipant> participants, DungeonLayout layout,
            DungeonEntryFacts facts, CancellationToken cancellationToken) =>
            Write(() => inner.TryEnterPreparedPartyAsync(participants, layout, facts, cancellationToken));
        public ValueTask<DungeonEntry?> EnterAsync(CharacterId characterId, int dungeonId, DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken) =>
            Write(() => inner.EnterAsync(characterId, dungeonId, difficulty, facts, cancellationToken));
        public ValueTask<DungeonEntryAttempt> TryEnterPartyAsync(IReadOnlyList<DungeonRunParticipant> participants, int dungeonId, DungeonEntryFacts facts, CancellationToken cancellationToken) =>
            Write(() => inner.TryEnterPartyAsync(participants, dungeonId, facts, cancellationToken));
        public ValueTask<DungeonPartyEntry?> EnterPartyAsync(IReadOnlyList<DungeonRunParticipant> participants, int dungeonId, DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken) =>
            Write(() => inner.EnterPartyAsync(participants, dungeonId, difficulty, facts, cancellationToken));
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId characterId, DungeonRunState current, MazeCell target, CancellationToken cancellationToken) =>
            Write(() => inner.MoveAsync(characterId, current, target, cancellationToken));
        public ValueTask<DungeonRoomVisit?> MoveByLostLandItemAsync(CharacterId characterId, DungeonRunState current, MazeCell target, CancellationToken token) =>
            Write(() => inner.MoveByLostLandItemAsync(characterId, current, target, token));
        public ValueTask<DungeonMoveAttempt> TryMoveAsync(CharacterId characterId, DungeonRunState current, MazeCell target, CancellationToken cancellationToken) =>
            Write(() => inner.TryMoveAsync(characterId, current, target, cancellationToken));
    }
}
