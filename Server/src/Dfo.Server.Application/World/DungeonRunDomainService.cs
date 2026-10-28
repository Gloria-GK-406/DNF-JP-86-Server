using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.World;

/// <summary>Keeps process-local dungeon runs as shared aggregates with participant views.</summary>
internal sealed partial class DungeonRunDomainService : IDungeonRunDomainService
{
    private readonly ILogger<DungeonRunDomainService> logger = NullLogger<DungeonRunDomainService>.Instance;
    private readonly IDomainTransactionAccessor? transactions;
    private readonly IPartyService parties;
    private readonly IDungeonParticipationDomainService? participation;
    private readonly IWeaknessDomainService? weakness;
    private readonly ICreatureRuntimeDomainService? creatures;
    private readonly IDungeonLifecycleDomainService? lifecycle;
    private readonly TimeProvider clock = TimeProvider.System;
    private readonly IDungeonSettlementRuleService settlementRules = new DungeonSettlementRuleService();
    public ValueTask<DungeonSettlementRules> SettlementRulesForAsync(Dfo.Server.Data.Accounts.AccountId account, CharacterId character,
        DungeonRunState run, CancellationToken token) => settlementRules.ForAsync(account, character, run, token);
    private readonly object gate = new();
    private readonly IDungeonRoomMechanismRuleService mechanismRules = new DungeonRoomMechanismRuleService();
    private readonly ISealedTreasureRuleService sealedTreasureRules = new SealedTreasureRuleService();
    private readonly IDungeonRoomMechanismWakeSignal? mechanismWake;
    private readonly IGiantDragonRuleService? giantDragons;
    private readonly Dictionary<DungeonRunId, DateTimeOffset> wipeDeadlines = [];
    private readonly Dictionary<DungeonRunId, DateTimeOffset> clearReturnDeadlines = [];
    // A party holds at most one run. Who is in it is the party's answer, so there is no
    // character-to-run index to keep in step with party changes.
    private readonly Dictionary<PartyKey, DungeonRunAggregate> runsByParty = new();
    private readonly Dictionary<DungeonRunId, DungeonRunAggregate> runsById = new();
    private readonly Dictionary<DeathClaimKey, long> deathClaims = new();
    private readonly Dictionary<ParticipantEffectClaimKey, long> participantEffectClaims = new();
    private readonly Dictionary<SettlementClaimKey, long> settlementClaims = new();
    private readonly Dictionary<GroundDropClaimKey, long> groundDropClaims = new();
    private readonly HashSet<GroundDropClaimKey> preparedGroundDropClaims = [];
    private readonly Dictionary<ClearRewardCardClaimKey, ClearRewardReservation>
        clearRewardCardClaims = new();
    private readonly HashSet<ClearRewardCardClaimKey> preparedClearRewardCardClaims = [];
    private long nextClaim;

    public DungeonRunDomainService(
        IDomainTransactionAccessor transactions,
        IPartyService parties,
        IWeaknessDomainService? weakness = null,
        ICreatureRuntimeDomainService? creatures = null,
        IDungeonLifecycleDomainService? lifecycle = null, TimeProvider? clock = null,
        IDungeonRoomMechanismRuleService? mechanismRules = null, IDungeonRoomMechanismWakeSignal? mechanismWake = null,
        ISealedTreasureRuleService? sealedTreasureRules = null, IDungeonParticipationDomainService? participation = null,
        ILogger<DungeonRunDomainService>? logger = null, IDungeonSettlementRuleService? settlementRules = null,
        IGiantDragonRuleService? giantDragons = null)
    {
        this.settlementRules = settlementRules ?? new DungeonSettlementRuleService();
        this.logger = logger ?? NullLogger<DungeonRunDomainService>.Instance;
        this.transactions = transactions;
        this.participation = participation;
        this.parties = parties ?? throw new ArgumentNullException(nameof(parties));
        this.weakness = weakness;
        this.creatures = creatures;
        this.lifecycle = lifecycle;
        this.clock = clock ?? TimeProvider.System;
        this.mechanismRules = mechanismRules ?? new DungeonRoomMechanismRuleService();
        this.mechanismWake = mechanismWake;
        this.sealedTreasureRules = sealedTreasureRules ?? new SealedTreasureRuleService();
        this.giantDragons = giantDragons;
    }

    public DungeonEntryChange PreparePartyEntry(
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        DungeonDifficulty difficulty,
        DungeonCellSeed initialCell,
        DungeonRunId runId,
        DateTimeOffset startedAt)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(PreparePartyEntry));
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(initialCell);
        if (!initialCell.IsComplete)
            throw new ArgumentException("A dungeon run cannot publish an incomplete Cell.", nameof(initialCell));
        if (initialCell.Cell != layout.StartCell
            || initialCell.MapId != layout.StartRoomMapId)
        {
            throw new ArgumentException(
                "The initial Cell identity must match the layout's start room.", nameof(initialCell));
        }
        if (participants.Count == 0)
            throw new ArgumentException("A dungeon run requires at least one participant.", nameof(participants));
        if (participants.Any(participant => participant is null))
            throw new ArgumentException("A dungeon participant cannot be null.", nameof(participants));
        if (participants.Select(participant => participant.CharacterId).Distinct().Count()
            != participants.Count)
        {
            throw new ArgumentException(
                "A character can occur only once in a dungeon run.", nameof(participants));
        }
        if (participants.Select(participant => participant.Slot).Distinct().Count()
            != participants.Count)
        {
            throw new ArgumentException(
                "A dungeon participant slot can occur only once.", nameof(participants));
        }
        if (participants.Count(participant => participant.IsLeader) != 1)
        {
            throw new ArgumentException(
                "A dungeon run requires exactly one leader.", nameof(participants));
        }

        var frozen = participants.ToImmutableArray();
        var entryLeader = frozen.Single(participant => participant.IsLeader);
        var partyKey = this.parties.FindParty(new PartyCharacterSnapshot(
            entryLeader.AccountId, entryLeader.CharacterId, 0, null)).Key;
        var currentParty = this.parties.FindByKey(partyKey)
            ?? throw new InvalidOperationException("The entry party no longer exists.");
        if (!currentParty.Members.Select(member => member.CharacterId)
                .ToHashSet().SetEquals(frozen.Select(participant => participant.CharacterId)))
            throw new InvalidOperationException("The entry roster no longer matches the party.");
        if (this.participation is not null && !this.participation.TryClaim(
            frozen.Select(x => x.CharacterId).ToArray(), new(DungeonParticipationMode.Ordinary, runId.Value)))
            throw new InvalidOperationException("A dungeon participant is already occupied.");
        var aggregate = new DungeonRunAggregate(
            runId, partyKey, this.parties, frozen, layout, difficulty, startedAt, initialCell);

        lock (this.gate)
        {
            if (this.transactions?.Current is { } transaction)
            {
                var previous = this.runsByParty.GetValueOrDefault(partyKey);
                transaction.Enlist(new object(), () => new EntryCommit(this, aggregate, previous));
                return aggregate.EntryChange(DungeonEntryChangeKind.Prepared, aggregate.Leader.CharacterId);
            }
            // The party's earlier run ends here without a departure: nobody left, they started over.
            if (this.runsByParty.GetValueOrDefault(partyKey) is { } replaced)
                _ = this.EndRun(replaced);

            this.Publish(aggregate);
            foreach (var participant in frozen)
                this.weakness?.For(participant.CharacterId).PauseRecovery();

            return aggregate.EntryChange(
                DungeonEntryChangeKind.Prepared,
                aggregate.Leader.CharacterId);
        }
    }

    public bool HasUsedDungeonBuff(CharacterId characterId, DungeonRunId runId)
    {
        lock (this.gate)
            return !this.runsById.TryGetValue(runId, out var run)
                || !run.IsMember(characterId) || run.BuffParticipants.Contains(characterId);
    }

    public bool StageDungeonBuff(CharacterId characterId, DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(StageDungeonBuff));
        lock (this.gate)
        {
            if (HasUsedDungeonBuff(characterId, runId)) return false;
            var transaction = this.transactions?.Current
                ?? throw new InvalidOperationException("Dungeon buff consumption requires a transaction.");
            transaction.Enlist((this, runId, characterId, "premium-buff"),
                () => new BuffCommitParticipant(this, this.runsById[runId], characterId));
            return true;
        }
    }

    private sealed class BuffCommitParticipant(DungeonRunDomainService owner,
        DungeonRunAggregate run, CharacterId characterId) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token)
        {
            if (owner.HasUsedDungeonBuff(characterId, run.RunId))
                throw new InvalidOperationException("Dungeon buff participant changed before commit.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken token)
        {
            lock (owner.gate) run.BuffParticipants.Add(characterId);
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }

    public bool StageRevival(CharacterId payer, CharacterId target, DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(StageRevival));
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var aggregate)
                || !CanRevive(aggregate, payer, target)) return false;
            var transaction = this.transactions?.Current
                ?? throw new InvalidOperationException("Revival requires a transaction.");
            transaction.Enlist((this, runId, target, "revival"),
                () => new RevivalCommitParticipant(this, aggregate, payer, target));
            return true;
        }
    }

    private static bool CanRevive(DungeonRunAggregate aggregate, CharacterId payer, CharacterId target) =>
        aggregate.IsMember(payer) && aggregate.IsMember(target)
        && aggregate.ReadyParticipants.Contains(payer) && aggregate.ReadyParticipants.Contains(target)
        && aggregate.DeadParticipants.Contains(target)
        && !aggregate.Cells[aggregate.CurrentCell].ClearOpen
        && !aggregate.ProgressFor(target).IsSettled;

    private sealed class RevivalCommitParticipant(DungeonRunDomainService owner,
        DungeonRunAggregate aggregate, CharacterId payer, CharacterId target) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            lock (owner.gate)
            {
                if (!owner.runsById.TryGetValue(aggregate.RunId, out var current)
                    || current != aggregate || !CanRevive(aggregate, payer, target))
                    throw new InvalidOperationException("Revival target changed before commit.");
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            // Caller retains instance and admitted-session leases across the durable commit.
            lock (owner.gate)
            {
                aggregate.DeadParticipants.Remove(target);
                owner.wipeDeadlines.Remove(aggregate.RunId);
                aggregate.RuntimeRevision++;
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    public void ScheduleClearReturn(CharacterId characterId, DungeonRunId runId, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ScheduleClearReturn));
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var run)
                || !run.IsMember(characterId) || run.Participants.Length < 2
                || !run.Cells[run.CurrentCell].ClearOpen
                || !run.ProgressFor(characterId).CardLayoutOpen) return;
            // A21 ordinary party result UI starts a 60 second return countdown.
            this.clearReturnDeadlines.TryAdd(runId, now.AddSeconds(60));
        }
    }

    public IReadOnlyList<DungeonRunId> FindExpiredClearReturns(DateTimeOffset now)
    {
        lock (this.gate)
            return this.clearReturnDeadlines.Where(pair => pair.Value <= now)
                .Select(pair => pair.Key).ToArray();
    }

    public DungeonRunState? TryEndClearReturn(DungeonRunId runId, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryEndClearReturn));
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var run)
                || !this.clearReturnDeadlines.TryGetValue(runId, out var deadline)
                || now < deadline || !run.Cells[run.CurrentCell].ClearOpen) return null;
            var members = run.Participants;
            if (members.Length == 0) return null;
            var snapshot = run.ViewFor(members[0].CharacterId) with { Participants = members };
            _ = this.EndRun(run);
            return snapshot;
        }
    }

    public IReadOnlyList<DungeonRunId> FindExpiredPartyWipes(DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(FindExpiredPartyWipes));
        lock (this.gate)
        {
            foreach (var aggregate in this.runsById.Values)
            {
                if (!IsPartyWiped(aggregate))
                    this.wipeDeadlines.Remove(aggregate.RunId);
                else
                    this.wipeDeadlines.TryAdd(aggregate.RunId, now.AddSeconds(10));
            }
            return this.wipeDeadlines.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray();
        }
    }

    public DungeonRunState? TryEndPartyWipe(DungeonRunId runId, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryEndPartyWipe));
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var aggregate)
                || !this.wipeDeadlines.TryGetValue(runId, out var deadline)
                || now < deadline)
                return null;
            if (!IsPartyWiped(aggregate))
            {
                this.wipeDeadlines.Remove(runId);
                return null;
            }
            var members = aggregate.Participants;
            var snapshot = aggregate.ViewFor(members[0].CharacterId) with { Participants = members };
            _ = this.EndRun(aggregate);
            return snapshot;
        }
    }

    private static bool IsPartyWiped(DungeonRunAggregate aggregate)
    {
        var members = aggregate.Participants;
        return !aggregate.Cells[aggregate.CurrentCell].ClearOpen
            && members.Length > 0
            && members.All(member => !aggregate.ProgressFor(member.CharacterId).IsSettled
                && aggregate.DeadParticipants.Contains(member.CharacterId));
    }

    public DungeonRunState? TryMarkParticipantDead(
        CharacterId characterId, DungeonRunId expectedRunId, long expectedRoomVersion)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMarkParticipantDead));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || !aggregate.ReadyParticipants.Contains(characterId)
                || !aggregate.DeadParticipants.Add(characterId))
                return null;

            aggregate.RuntimeRevision++;
            return aggregate.ViewFor(characterId);
        }
    }

    public DungeonEntryChange? TryMarkParticipantReady(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMarkParticipantReady));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || !aggregate.ReadyParticipants.Add(characterId))
            {
                return null;
            }

            aggregate.EntryRevision++;
            if (aggregate.Participants.All(member => aggregate.ReadyParticipants.Contains(member.CharacterId)))
            {
                var cell = aggregate.Cells[aggregate.CurrentCell];
                cell.Mechanisms = cell.Mechanisms.Select(state => this.mechanismRules.Start(state, this.clock.GetUtcNow())).ToArray();
                if (cell.IsCleared)
                    cell.Mechanisms = cell.Mechanisms.Select(state => this.mechanismRules.Complete(state, this.clock.GetUtcNow())).ToArray();
                this.mechanismWake?.Wake();
            }
            return aggregate.EntryChange(DungeonEntryChangeKind.ParticipantReady, characterId);
        }
    }

    public DungeonEntryChange? FindEntryChange(CharacterId characterId)
    {
        lock (this.gate)
        {
            return this.RunOf(characterId) is { } aggregate
                ? aggregate.EntryChange(DungeonEntryChangeKind.Snapshot, characterId)
                : null;
        }
    }

    public bool TryAbortEntry(
        DungeonRunId expectedRunId,
        long expectedEntryRevision,
        IReadOnlyList<CharacterId> expectedRecipients)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryAbortEntry));
        ArgumentNullException.ThrowIfNull(expectedRecipients);
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(expectedRunId, out var aggregate)
                || aggregate.EntryRevision != expectedEntryRevision
                || !aggregate.Participants.Select(participant => participant.CharacterId)
                    .ToHashSet().SetEquals(expectedRecipients))
            {
                return false;
            }

            _ = this.EndRun(aggregate);
            return true;
        }
    }

    public DungeonRunState? Find(CharacterId characterId)
    {
        lock (this.gate)
        {
            return this.RunOf(characterId)?.ViewFor(characterId);
        }
    }

    public DungeonRunState? Find(DungeonRunId runId)
    {
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var aggregate))
                return null;

            var active = aggregate.Participants
                .OrderBy(participant => participant.Slot)
                .FirstOrDefault();
            return active is null ? null : aggregate.ViewFor(active.CharacterId);
        }
    }

    public DungeonRunState? TryMove(CharacterId characterId, MazeCell target)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMove));
        lock (this.gate)
        {
            return this.TryMoveCore(characterId, expectedRunId: null, expectedRoomVersion: null, target);
        }
    }

    public DungeonRunState? TryMove(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        MazeCell target)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMove));
        lock (this.gate)
        {
            return this.TryMoveCore(
                characterId, expectedRunId, expectedRoomVersion, target, preparedCell: null);
        }
    }

    public DungeonRunState? TryMove(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        MazeCell target,
        DungeonCellSeed? preparedCell)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryMove));
        lock (this.gate)
        {
            return this.TryMoveCore(
                characterId, expectedRunId, expectedRoomVersion, target, preparedCell);
        }
    }

    private DungeonRunState? TryMoveCore(
        CharacterId characterId,
        DungeonRunId? expectedRunId,
        long? expectedRoomVersion,
        MazeCell target,
        DungeonCellSeed? preparedCell = null)
    {
        DungeonRunState? Refused(string reason, DungeonRunAggregate? run)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Dungeon move aggregate refused character={CharacterId} run={RunId} expectedRun={ExpectedRunId} version={RoomVersion} expectedVersion={ExpectedVersion} from={FromCell} target={TargetCell} reason={Reason}",
                    characterId, run?.RunId, expectedRunId, run?.RoomVersion, expectedRoomVersion, run?.CurrentCell, target, reason);
            return null;
        }

        if (this.RunOf(characterId) is not { } aggregate)
            return Refused("NoRun", null);
        if (!this.IsCurrentLeader(aggregate, characterId))
            return Refused("NotLeader", aggregate);
        if (expectedRunId is { } runId && aggregate.RunId != runId)
            return Refused("RunChanged", aggregate);
        if (expectedRoomVersion is { } version && aggregate.RoomVersion != version)
            return Refused("RoomVersionChanged", aggregate);
        if (this.deathClaims.Keys.Any(key =>
                key.RunId == aggregate.RunId && key.Actor.Cell == aggregate.CurrentCell))
            return Refused("ActorDeathPending", aggregate);
        if (this.groundDropClaims.Keys.Any(key =>
                key.RunId == aggregate.RunId && key.Drop.Cell == aggregate.CurrentCell))
            return Refused("GroundDropPending", aggregate);
        if (this.preparedGroundDropClaims.Any(key =>
                key.RunId == aggregate.RunId && key.Drop.Cell == aggregate.CurrentCell))
            return Refused("GroundDropPrepared", aggregate);
        if (Math.Abs(target.X - aggregate.CurrentCell.X)
                + Math.Abs(target.Y - aggregate.CurrentCell.Y) != 1)
            return Refused("NotAdjacent", aggregate);
        if (!aggregate.Layout.CanTraverse(aggregate.CurrentCell, target))
            return Refused("Disconnected", aggregate);
        if (!this.sealedTreasureRules.AllowsTraversal(aggregate.Layout.DungeonId,
                aggregate.KilledMonsterCodes, aggregate.CurrentCell, target))
            return Refused("SealedTreasureGate", aggregate);
        if (aggregate.Cells[aggregate.CurrentCell].MechanismBlocksClear)
            return Refused("MechanismBlocksClear", aggregate);
        if (aggregate.Cells[aggregate.CurrentCell].Mechanisms.Any(state =>
                !this.mechanismRules.AllowsExit(state, aggregate.CurrentCell, target)))
            return Refused("MechanismBlocksExit", aggregate);
        if (preparedCell is not null
                && (preparedCell.Cell != target || preparedCell.MapId <= 0
                    || (aggregate.Cells.TryGetValue(target, out var retained) && retained.MapId != preparedCell.MapId)))
            return Refused("PreparedCellMismatch", aggregate);
        if (!aggregate.Cells.ContainsKey(target) && (preparedCell is null || !preparedCell.IsComplete))
            return Refused("IncompleteCell", aggregate);
        if (this.transactions?.Current is { } transaction)
        {
            var cell = aggregate.Cells.GetValueOrDefault(target) ?? aggregate.CreateCell(preparedCell!);
            var movement = transaction.Enlist(new object(), () => new MoveCommit(this, aggregate, characterId, target, cell));
            return movement.Preview;
        }
        if (!aggregate.Cells.ContainsKey(target))
        {
            if (preparedCell is null || !preparedCell.IsComplete)
                return null;
            aggregate.Cells.Add(
                target,
                aggregate.CreateCell(preparedCell));
        }
        aggregate.CurrentCell = target;
        aggregate.VisitedCells = aggregate.VisitedCells.Add(target);
        aggregate.RoomVersion++;
        aggregate.EntryRevision++;
        aggregate.ReadyParticipants.Clear();
        this.mechanismWake?.Wake();
        return aggregate.ViewFor(characterId);
    }

    private bool IsCurrentLeader(DungeonRunAggregate aggregate, CharacterId characterId)
    {
        return aggregate.Participants.Any(participant =>
            participant.CharacterId == characterId && participant.IsLeader);
    }

    public DungeonGroundDropClaim? TryClaimGroundDrop(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        ushort groundDropNumber)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryClaimGroundDrop));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion)
            {
                return null;
            }

            var drop = new GroundDropId(aggregate.CurrentCell, groundDropNumber);
            var cell = aggregate.Cells[aggregate.CurrentCell];
            var item = cell.FindGroundItem(groundDropNumber);
            // Preserve A21's declared-object association, but never bank its reward before
            // the owning chest destruction has actually committed (reservation is insufficient).
            if (item?.Source is { Kind: DungeonGroundItemSourceKind.DeclaredObject, ObjectIndex: { } objectIndex }
                && cell.HasUnopenedRewardObject(aggregate.Layout.DungeonId, objectIndex, this.sealedTreasureRules))
                return null;
            var key = new GroundDropClaimKey(aggregate.RunId, drop, characterId);
            if (item is null
                || this.groundDropClaims.Keys.Any(candidate =>
                    candidate.RunId == aggregate.RunId && candidate.Drop == drop)
                || this.preparedGroundDropClaims.Any(candidate =>
                    candidate.RunId == aggregate.RunId && candidate.Drop == drop))
                return null;

            var token = Interlocked.Increment(ref this.nextClaim);
            this.groundDropClaims.Add(key, token);
            return new DungeonGroundDropClaim(
                characterId,
                aggregate.ViewFor(characterId),
                drop,
                token,
                aggregate.RunId,
                item,
                aggregate.RoomVersion,
                cell.Revision);
        }
    }

    public DungeonPickupChange? CommitGroundDrop(
        DungeonGroundDropClaim claim,
        DungeonPickupKind kind)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(CommitGroundDrop));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new GroundDropClaimKey(claim.RunId, claim.Drop, claim.CharacterId);
            if (!this.groundDropClaims.TryGetValue(key, out var token)
                || token != claim.Token
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || aggregate.CurrentCell != claim.Drop.Cell
                || aggregate.RoomVersion != claim.RoomVersion
                || !aggregate.Cells.TryGetValue(claim.Drop.Cell, out var cell)
                || cell.Revision != claim.CellRevision
                || cell.RemoveGroundItem(claim.Drop.Number) is null)
            {
                return null;
            }

            _ = this.groundDropClaims.Remove(key);
            aggregate.RuntimeRevision++;
            return new DungeonPickupChange(
                aggregate.RunId,
                aggregate.RuntimeRevision,
                claim.CharacterId,
                claim.Drop,
                kind,
                [.. aggregate.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)],
                aggregate.RoomVersion,
                cell.Revision);
        }
    }

    public DungeonPickupCommit StageGroundDrop(
        DungeonGroundDropClaim claim,
        DungeonPickupKind kind)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(StageGroundDrop));
        ArgumentNullException.ThrowIfNull(claim);
        var transaction = this.transactions?.Current;
        if (transaction is null)
            return DungeonPickupCommit.Completed(this.CommitGroundDrop(claim, kind));

        var participant = transaction.Enlist(
            new GroundDropCommitIdentity(
                this, claim.RunId, claim.Drop, claim.CharacterId, claim.Token),
            () => new GroundDropCommitParticipant(this, claim, kind));
        participant.RequireKind(kind);
        return participant.Receipt;
    }

    public bool ReleaseGroundDrop(DungeonGroundDropClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ReleaseGroundDrop));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            return this.TryRemoveGroundDropClaim(
                new GroundDropClaimKey(claim.RunId, claim.Drop, claim.CharacterId), claim.Token);
        }
    }

    public DungeonGroundItemsAddedChange? TryAddGroundItems(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        long expectedCellRevision,
        IReadOnlyList<DungeonGroundItemDraft> items)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryAddGroundItems));
        ArgumentNullException.ThrowIfNull(items);
        lock (this.gate)
        {
            if (items.Count == 0
                || this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion)
            {
                return null;
            }

            var cell = aggregate.Cells[aggregate.CurrentCell];
            if (cell.Revision != expectedCellRevision)
                return null;
            var eligible = items.Where(item => item.OwnerCharacterId is not { } owner
                || aggregate.IsMember(owner)).ToArray();
            if (eligible.Length == 0)
                return null;
            var added = cell.AppendGroundItems(eligible);
            cell.Revision++;
            aggregate.RuntimeRevision++;
            var run = aggregate.ViewFor(characterId);
            return new DungeonGroundItemsAddedChange(
                run,
                aggregate.RuntimeRevision,
                cell.Revision,
                added,
                [.. aggregate.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);
        }
    }

    public DungeonClearChange? TryOpenClear(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryOpenClear));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || aggregate.Cells[aggregate.CurrentCell].ClearOpen)
            {
                return null;
            }

            var cell = aggregate.Cells[aggregate.CurrentCell];
            cell.ClearOpen = true;
            aggregate.ClearedAt = this.clock.GetUtcNow();
            cell.Revision++;
            aggregate.RuntimeRevision++;
            return new DungeonClearChange(
                aggregate.RunId,
                aggregate.RuntimeRevision,
                [.. aggregate.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);
        }
    }

    public bool IsActorDeathClaimCurrent(DungeonActorDeathClaim claim)
    {
        lock (gate) return deathClaims.GetValueOrDefault(new DeathClaimKey(claim.RunId, claim.Actor)) == claim.Token
            && runsById.TryGetValue(claim.RunId, out var run) && run.IsMember(claim.CharacterId)
            && run.RoomVersion == claim.Run.RoomVersion && run.CurrentCell == claim.Actor.Cell;
    }

    public bool IsParticipantKillEffectClaimCurrent(DungeonParticipantKillEffectClaim claim)
    {
        lock (gate) return IsActorDeathClaimCurrent(claim.Death)
            && participantEffectClaims.GetValueOrDefault(new ParticipantEffectClaimKey(claim.Death.RunId,
                new ParticipantEffectKey(claim.Death.Actor, claim.CharacterId))) == claim.Token
            && runsById.TryGetValue(claim.Death.RunId, out var run) && run.IsMember(claim.CharacterId);
    }

    public DungeonActorDeathClaim? TryClaimActorDeath(
        CharacterId characterId,
        ushort actorSequence)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryClaimActorDeath));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate)
                return null;

            var actor = new RoomActorId(aggregate.CurrentCell, actorSequence);
            var cell = aggregate.Cells[aggregate.CurrentCell];
            if (!cell.CanClaimDeath(actorSequence))
                return null;

            var key = new DeathClaimKey(aggregate.RunId, actor);
            if (this.deathClaims.ContainsKey(key))
                return null;

            var token = Interlocked.Increment(ref this.nextClaim);
            this.deathClaims.Add(key, token);
            return new DungeonActorDeathClaim(
                characterId,
                aggregate.ViewFor(characterId),
                actor,
                token,
                aggregate.RunId,
                cell.FindActor(actorSequence)) { OccurredAt = this.clock.GetUtcNow() };
        }
    }

    public DungeonRunState? CommitActorDeath(DungeonActorDeathClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(CommitActorDeath));
        return this.CommitActorDeathChange(claim)?.Run;
    }

    public bool WillClearMap(DungeonActorDeathClaim claim)
    {
        lock (this.gate)
        {
            if (!this.IsActorDeathClaimCurrent(claim)) return false;
            var aggregate = this.runsById[claim.RunId];
            var cell = aggregate.Cells[claim.Actor.Cell];
            if (cell.IsCleared) return false;
            var suppressed = this.SuppressedAfterDeath(aggregate, cell, claim);
            return cell.WillClearAfterDeath(claim.Actor.Sequence, suppressed);
        }
    }

    private IReadOnlyList<ushort> SuppressedAfterDeath(DungeonRunAggregate aggregate, DungeonCellAggregate cell, DungeonActorDeathClaim claim) =>
        !cell.MechanismBlocksClear && aggregate.Layout.Altar is null
        && cell.FindActor(claim.Actor.Sequence) is { BlocksClear: true, Actor.IsBoss: true }
        && aggregate.Layout.KindOf(claim.Actor.Cell) == DungeonRoomKind.Boss
        && !cell.HasLivingBossExcept(claim.Actor.Sequence)
            ? cell.SuppressibleEnemies(sequence => !this.deathClaims.ContainsKey(new DeathClaimKey(
                claim.RunId, new RoomActorId(claim.Actor.Cell, sequence)))) : [];

    public DungeonActorDeathChange? CommitActorDeathChange(DungeonActorDeathClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(CommitActorDeathChange));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new DeathClaimKey(claim.RunId, claim.Actor);
            if (!this.TryRemoveClaim(key, claim.Token)
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || !aggregate.IsMember(claim.CharacterId))
            {
                return null;
            }

            var cell = aggregate.Cells[claim.Actor.Cell];
            var killedActor = cell.FindActor(claim.Actor.Sequence);
            var autoKilled = this.SuppressedAfterDeath(aggregate, cell, claim);
            IReadOnlyList<RolledDrop>? resolvedLoot = null;
            if (aggregate.Layout.GiantDragonTimeReward is { } timeReward
                && killedActor is { IsMonster: true, BlocksClear: true, Actor.IsBoss: true }
                && aggregate.Layout.KindOf(claim.Actor.Cell) == DungeonRoomKind.Boss
                && !cell.HasLivingBossExcept(claim.Actor.Sequence))
            {
                var elapsed = (claim.OccurredAt ?? this.clock.GetUtcNow()) - aggregate.StartedAt;
                resolvedLoot = (this.giantDragons ?? throw new InvalidOperationException("Dragon reward rules unavailable."))
                    .ResolveTimeReward(timeReward, elapsed, killedActor.PendingLoot);
                this.logger.LogInformation("Giant dragon time reward run={RunId} dungeon={DungeonId} actor={Actor} elapsedMs={ElapsedMs} item={ItemId} sharedCount={Count}",
                    aggregate.RunId, aggregate.Layout.DungeonId, claim.Actor.Sequence, elapsed.TotalMilliseconds,
                    timeReward.Material.ItemId, resolvedLoot.Where(x => x.ItemId == timeReward.Material.ItemId
                        && x.OwnerCharacterId is null && x.Delivery == RolledDropDelivery.Ground).Sum(x => x.Count));
            }
            var added = cell.CommitActorDeath(
                claim.Actor.Sequence,
                rolled => rolled.OwnerCharacterId is not { } owner
                    || aggregate.IsMember(owner), resolvedLoot);
            if (added is null)
                return null;
            if (killedActor is { IsMonster: true })
            {
                aggregate.RecordKill(new DungeonKillRecord(
                    claim.Actor,
                    killedActor.Actor.Code,
                    aggregate.Layout.LevelOf(killedActor.Actor.Level),
                    (MonsterRank)killedActor.Actor.ActorType));
            }
            cell.SuppressEnemies(autoKilled);
            if (cell.IsCleared)
                cell.Mechanisms = cell.Mechanisms.Select(state => this.mechanismRules.Complete(state,
                    claim.OccurredAt ?? this.clock.GetUtcNow())).ToArray();
            this.mechanismWake?.Wake();
            aggregate.RuntimeRevision++;
            var run = aggregate.ViewFor(claim.CharacterId);
            return new DungeonActorDeathChange(
                run,
                claim.Actor,
                aggregate.RuntimeRevision,
                cell.Revision,
                added,
                cell.Snapshot().IsCleared,
                [.. aggregate.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)])
            {
                AutoKilledActorSequences = autoKilled,
            };
        }
    }

    public bool ReleaseActorDeath(DungeonActorDeathClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ReleaseActorDeath));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new DeathClaimKey(claim.RunId, claim.Actor);
            if (!this.TryRemoveClaim(key, claim.Token)
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate))
            {
                return false;
            }

            return true;
        }
    }

    public DungeonParticipantKillEffectClaim? TryClaimParticipantKillEffect(
        DungeonActorDeathClaim death,
        CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryClaimParticipantKillEffect));
        ArgumentNullException.ThrowIfNull(death);
        lock (this.gate)
        {
            var deathKey = new DeathClaimKey(death.RunId, death.Actor);
            if (!this.deathClaims.TryGetValue(deathKey, out var deathToken)
                || deathToken != death.Token
                || !this.runsById.TryGetValue(death.RunId, out var aggregate)
                || !aggregate.Participants.Any(
                    participant => participant.CharacterId == characterId)
                || !aggregate.IsMember(characterId))
            {
                return null;
            }

            var effect = new ParticipantEffectKey(death.Actor, characterId);
            if (aggregate.CompletedKillEffects.Contains(effect))
                return null;

            var key = new ParticipantEffectClaimKey(death.RunId, effect);
            if (this.participantEffectClaims.ContainsKey(key))
                return null;

            var token = Interlocked.Increment(ref this.nextClaim);
            this.participantEffectClaims.Add(key, token);
            return new DungeonParticipantKillEffectClaim(death, characterId, token);
        }
    }

    public bool CommitParticipantKillEffect(DungeonParticipantKillEffectClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(CommitParticipantKillEffect));
        return this.CommitParticipantKillEffect(claim, null);
    }

    public bool CommitParticipantKillEffect(DungeonParticipantKillEffectClaim claim, ExperienceAward? award)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(CommitParticipantKillEffect));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var effect = new ParticipantEffectKey(claim.Death.Actor, claim.CharacterId);
            var key = new ParticipantEffectClaimKey(claim.Death.RunId, effect);
            if (!this.TryRemoveParticipantEffectClaim(key, claim.Token)
                || !this.runsById.TryGetValue(claim.Death.RunId, out var aggregate))
            {
                return false;
            }

            if (!aggregate.CompletedKillEffects.Add(effect)) return false;
            if (award is not null && claim.Death.ActorState is { IsMonster: true } actor)
            {
                var summary = aggregate.KillExperiences.GetValueOrDefault(claim.CharacterId) ?? new();
                aggregate.KillExperiences[claim.CharacterId] = summary.Record(new(
                    claim.Death.Actor, actor.Actor.Code, (MonsterRank)actor.Actor.ActorType,
                    actor.IsNamedMonster, award.Breakdown));
            }
            return true;
        }
    }

    public bool ReleaseParticipantKillEffect(DungeonParticipantKillEffectClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ReleaseParticipantKillEffect));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var effect = new ParticipantEffectKey(claim.Death.Actor, claim.CharacterId);
            return this.TryRemoveParticipantEffectClaim(
                new ParticipantEffectClaimKey(claim.Death.RunId, effect), claim.Token);
        }
    }

    public DungeonRunState? TrySettle(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TrySettle));
        var claim = this.TryClaimSettlement(characterId);
        return claim is null ? null : this.CommitSettlement(claim);
    }

    public DungeonSettlementClaim? TryClaimSettlement(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryClaimSettlement));
        return this.TryClaimSettlementCore(characterId, expectedRunId: null);
    }

    public DungeonSettlementClaim? TryClaimSettlement(
        CharacterId characterId,
        DungeonRunId expectedRunId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryClaimSettlement));
        return this.TryClaimSettlementCore(characterId, expectedRunId);
    }

    private DungeonSettlementClaim? TryClaimSettlementCore(
        CharacterId characterId,
        DungeonRunId? expectedRunId)
    {
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || !aggregate.Cells[aggregate.CurrentCell].ClearOpen)
                return null;
            if (expectedRunId is { } runId && aggregate.RunId != runId)
                return null;

            var progress = aggregate.ProgressFor(characterId);
            if (progress.IsSettled)
                return null;

            var key = new SettlementClaimKey(aggregate.RunId, characterId);
            if (this.settlementClaims.ContainsKey(key))
                return null;

            var token = Interlocked.Increment(ref this.nextClaim);
            this.settlementClaims.Add(key, token);
            return new DungeonSettlementClaim(
                characterId, aggregate.ViewFor(characterId), token, aggregate.RunId);
        }
    }

    public DungeonRunState? CommitSettlement(DungeonSettlementClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(CommitSettlement));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new SettlementClaimKey(claim.RunId, claim.CharacterId);
            if (!this.TryRemoveSettlementClaim(key, claim.Token)
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || !aggregate.IsMember(claim.CharacterId))
            {
                return null;
            }

            var progress = aggregate.ProgressFor(claim.CharacterId);
            if (progress.IsSettled)
                return null;

            progress.IsSettled = true;
            return aggregate.ViewFor(claim.CharacterId);
        }
    }

    public bool ReleaseSettlement(DungeonSettlementClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ReleaseSettlement));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            return this.TryRemoveSettlementClaim(
                new SettlementClaimKey(claim.RunId, claim.CharacterId), claim.Token);
        }
    }

    public DungeonCardLayoutChange? TryOpenCardLayout(
        CharacterId characterId,
        DungeonRunId expectedRunId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryOpenCardLayout));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId)
            {
                return null;
            }

            var progress = aggregate.ProgressFor(characterId);
            if (!progress.IsSettled
                || progress.ClearRewardCards is null
                || progress.CardLayoutOpen)
            {
                return null;
            }

            progress.CardLayoutOpen = true;
            aggregate.RuntimeRevision++;
            return new DungeonCardLayoutChange(
                aggregate.RunId,
                aggregate.RuntimeRevision,
                characterId,
                [characterId]);
        }
    }

    public DungeonRunState? FixClearRewardCards(
        CharacterId characterId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(FixClearRewardCards));
        return this.FixClearRewardCardsCore(
            characterId, expectedRunId: null, free, paid, paidFlipCost);
    }

    public DungeonRunState? FixClearRewardCards(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost,
        ClearRewardCardFace? blackDiamond = null)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(FixClearRewardCards));
        return this.FixClearRewardCardsCore(characterId, expectedRunId, free, paid, paidFlipCost, blackDiamond);
    }

    private DungeonRunState? FixClearRewardCardsCore(
        CharacterId characterId,
        DungeonRunId? expectedRunId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost,
        ClearRewardCardFace? blackDiamond = null)
    {
        ArgumentNullException.ThrowIfNull(free);
        ArgumentNullException.ThrowIfNull(paid);
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate)
                return null;
            if (expectedRunId is { } runId && aggregate.RunId != runId)
                return null;

            var progress = aggregate.ProgressFor(characterId);
            progress.ClearRewardCards ??= new ClearRewardCardSet(
                free, paid, ImmutableHashSet<ClearRewardCardId>.Empty, paidFlipCost)
            { BlackDiamond = blackDiamond };
            return aggregate.ViewFor(characterId);
        }
    }

    public ClearRewardCardSlot? TryClaimClearRewardCard(
        CharacterId characterId,
        ClearRewardCardKind kind,
        int index)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryClaimClearRewardCard));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate)
                return null;

            var progress = aggregate.ProgressFor(characterId);
            if (progress.ClearRewardCards is not { } cards
                || cards.SlotAt(kind, index) is not { } slot)
            {
                return null;
            }

            var id = new ClearRewardCardId(kind, index);
            if (cards.Claimed.Contains(id))
                return null;

            progress.ClearRewardCards = cards with { Claimed = cards.Claimed.Add(id) };
            return slot;
        }
    }

    public DungeonClearRewardCardClaim? TryReserveClearRewardCard(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        ClearRewardCardKind kind,
        int index)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryReserveClearRewardCard));
        lock (this.gate)
        {
            if (this.RunOf(characterId) is not { } aggregate
                || aggregate.RunId != expectedRunId)
            {
                return null;
            }

            var progress = aggregate.ProgressFor(characterId);
            var card = new ClearRewardCardId(kind, index);
            if (progress.ClearRewardCards is not { } cards
                || !progress.IsSettled
                || !progress.CardLayoutOpen
                || cards.SlotAt(kind, index) is not { } slot
                || cards.Claimed.Contains(card)
                || aggregate.CardSelections.Any(selection =>
                    selection.Kind == kind && selection.CharacterId == characterId))
            {
                return null;
            }

            var key = new ClearRewardCardClaimKey(aggregate.RunId, characterId, card);
            if (this.preparedClearRewardCardClaims.Contains(key)
                || this.clearRewardCardClaims.ContainsKey(key)
                || this.clearRewardCardClaims.Any(entry =>
                    entry.Key.RunId == aggregate.RunId
                    && entry.Key.Card.Kind == kind
                    && entry.Value.CharacterId == characterId))
            {
                return null;
            }

            var token = Interlocked.Increment(ref this.nextClaim);
            this.clearRewardCardClaims.Add(key, new ClearRewardReservation(characterId, token));
            return new DungeonClearRewardCardClaim(
                characterId,
                aggregate.ViewFor(characterId),
                card,
                slot,
                token,
                aggregate.RunId);
        }
    }

    public DungeonCardSelectionCommit CommitClearRewardCard(
        DungeonClearRewardCardClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(CommitClearRewardCard));
        ArgumentNullException.ThrowIfNull(claim);
        var transaction = this.transactions?.Current;
        if (transaction is null)
        {
            var prepared = this.TryPrepareClearRewardCard(claim);
            if (prepared is null)
            {
                _ = this.ReleaseClearRewardCard(claim);
                return DungeonCardSelectionCommit.Completed(null);
            }

            return DungeonCardSelectionCommit.Completed(
                this.CommitPreparedClearRewardCard(prepared));
        }

        var participant = transaction.Enlist(
            new ClearRewardCardCommitIdentity(
                this, claim.RunId, claim.Card, claim.CharacterId, claim.Token),
            () => new ClearRewardCardCommitParticipant(this, claim));
        return participant.Receipt;
    }

    private PreparedClearRewardCard? TryPrepareClearRewardCard(
        DungeonClearRewardCardClaim claim)
    {
        lock (this.gate)
        {
            var key = new ClearRewardCardClaimKey(claim.RunId, claim.CharacterId, claim.Card);
            if (!this.clearRewardCardClaims.TryGetValue(key, out var reservation)
                || reservation.CharacterId != claim.CharacterId
                || reservation.Token != claim.Token
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || !aggregate.IsMember(claim.CharacterId)
                || this.preparedClearRewardCardClaims.Contains(key))
            {
                return null;
            }

            var progress = aggregate.ProgressFor(claim.CharacterId);
            if (progress.ClearRewardCards is not { } cards
                || !progress.IsSettled
                || !progress.CardLayoutOpen
                || cards.Claimed.Contains(claim.Card)
                || aggregate.CardSelections.Any(selection =>
                    selection.Kind == claim.Card.Kind
                    && selection.CharacterId == claim.CharacterId)
                || !this.preparedClearRewardCardClaims.Add(key))
            {
                return null;
            }

            return new PreparedClearRewardCard(
                key, claim.CharacterId, claim.Token, aggregate,
                aggregate.Participants.Single(participant =>
                    participant.CharacterId == claim.CharacterId).Slot);
        }
    }

    private DungeonCardSelectionChange CommitPreparedClearRewardCard(
        PreparedClearRewardCard prepared)
    {
        lock (this.gate)
        {
            // Prepare captured the exact run and reservation. Durable inventory/gold commits first;
            // this process-local publication is deliberately non-failing afterwards, even if the
            // participant disconnected and the aggregate left the live directory in between.
            _ = this.TryRemoveClearRewardCardClaim(prepared.Key, prepared.Token);
            _ = this.preparedClearRewardCardClaims.Remove(prepared.Key);
            var progress = prepared.Aggregate.ProgressFor(prepared.CharacterId);
            var cards = progress.ClearRewardCards
                ?? throw new InvalidOperationException(
                    "A prepared clear-card commit lost its frozen card layout.");
            progress.ClearRewardCards = cards with
            {
                Claimed = cards.Claimed.Add(prepared.Key.Card),
            };
            prepared.Aggregate.CardSelections.Add(new DungeonCardSelection(
                prepared.Key.Card.Kind,
                prepared.Slot,
                prepared.CharacterId,
                prepared.Slot)
            {
                Gold = cards.FaceOf(prepared.Key.Card.Kind).Gold,
                Item = cards.SlotAt(prepared.Key.Card.Kind, prepared.Key.Card.Index)?.Item,
            });
            // A free-card confirmation is private until every current participant has confirmed.
            // The wire index is not the shared display column; the party seat is.
            var ready = prepared.Key.Card.Kind is ClearRewardCardKind.Paid
                || prepared.Aggregate.Participants.All(participant =>
                    prepared.Aggregate.CardSelections.Any(selection =>
                        selection.Kind is ClearRewardCardKind.Free
                        && selection.CharacterId == participant.CharacterId));
            if (ready)
                prepared.Aggregate.RevealedCardKinds.Add(prepared.Key.Card.Kind);
            prepared.Aggregate.RuntimeRevision++;
            return new DungeonCardSelectionChange(
                prepared.Aggregate.RunId,
                prepared.Aggregate.RuntimeRevision,
                prepared.CharacterId,
                ready ? prepared.Aggregate.PublishedCardSelections() : [],
                [.. prepared.Aggregate.Participants
                    .Where(participant =>
                    {
                        var participantProgress = prepared.Aggregate.ProgressFor(
                            participant.CharacterId);
                        return participantProgress.IsSettled && participantProgress.CardLayoutOpen;
                    })
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);
        }
    }

    private void RollbackPreparedClearRewardCard(
        DungeonClearRewardCardClaim claim,
        PreparedClearRewardCard? prepared)
    {
        lock (this.gate)
        {
            if (prepared is { } value)
                _ = this.preparedClearRewardCardClaims.Remove(value.Key);
            _ = this.TryRemoveClearRewardCardClaim(
                new ClearRewardCardClaimKey(claim.RunId, claim.CharacterId, claim.Card), claim.Token);
        }
    }

    public bool ReleaseClearRewardCard(DungeonClearRewardCardClaim claim)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(ReleaseClearRewardCard));
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new ClearRewardCardClaimKey(claim.RunId, claim.CharacterId, claim.Card);
            return !this.preparedClearRewardCardClaims.Contains(key)
                && this.TryRemoveClearRewardCardClaim(key, claim.Token);
        }
    }

    // Callers retain the admitted-session transition lease through this async exit.
    public async ValueTask<bool> EndAsync(CharacterId characterId, DungeonRunId expectedRunId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(EndAsync));
        RunDeparture? departure;
        lock (this.gate)
            departure = this.Depart(characterId, expectedRunId);
        if (departure is null)
            return false;
        await this.RecordDeparturesAsync(departure).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<IReadOnlyList<CharacterId>> EndRunAsync(DungeonRunId runId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(EndRunAsync));
        RunDeparture departure;
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var aggregate))
                return [];
            departure = new(aggregate.RunId, aggregate.Layout.DungeonId, FailsOnDeparture(aggregate),
                this.EndRun(aggregate));
        }
        await this.RecordDeparturesAsync(departure).ConfigureAwait(false);
        return departure.Characters;
    }

    public async ValueTask<DungeonRunState?> TryEndPartyWipeAsync(DungeonRunId runId, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryEndPartyWipeAsync));
        var ended = TryEndPartyWipe(runId, now);
        if (ended is not null && this.lifecycle is not null)
            foreach (var member in ended.Participants)
                await this.lifecycle.RecordDepartureAsync(new(member.CharacterId, ended.RunId, ended.Layout.DungeonId)).ConfigureAwait(false);
        if (ended is not null && ended.Layout.AppliesFailureWeakness && this.weakness is not null)
            foreach (var member in ended.Participants)
                await this.weakness.For(member.CharacterId).SetWeakness().ConfigureAwait(false);
        return ended;
    }

    public async ValueTask<DungeonRunState?> TryEndClearReturnAsync(DungeonRunId runId, DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(TryEndClearReturnAsync));
        var ended = TryEndClearReturn(runId, now);
        if (ended is not null && this.lifecycle is not null)
            foreach (var member in ended.Participants)
                await this.lifecycle.RecordDepartureAsync(new(member.CharacterId, ended.RunId, ended.Layout.DungeonId)).ConfigureAwait(false);
        return ended;
    }

    public bool End(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(End));
        lock (this.gate)
        {
            // A character who already left the party no longer reaches the run through it; the run
            // still holding their state is the one they are leaving.
            var aggregate = this.RunOf(characterId)
                ?? this.runsById.Values.FirstOrDefault(run => run.HoldsRunState(characterId));
            return aggregate is not null && this.Depart(characterId, aggregate.RunId) is not null;
        }
    }

    public bool End(CharacterId characterId, DungeonRunId expectedRunId)
    {
        GameplayWriteAssertion.Writing(nameof(DungeonRunDomainService) + "." + nameof(End));
        lock (this.gate)
            return this.Depart(characterId, expectedRunId) is not null;
    }

    /// <summary>
    /// One character's departure from a run. Null when the run is gone, the character holds no state
    /// in it, or the character is still a member beside others: membership is the party's, so a
    /// member leaves the party before leaving the run. The last member's departure ends the run.
    /// </summary>
    private RunDeparture? Depart(CharacterId characterId, DungeonRunId runId)
    {
        if (!this.runsById.TryGetValue(runId, out var aggregate) || !aggregate.HoldsRunState(characterId))
            return null;
        var members = aggregate.Participants;
        var isMember = members.Any(member => member.CharacterId == characterId);
        if (isMember && members.Length > 1)
            return null;
        var failed = FailsOnDeparture(aggregate);
        IReadOnlyList<CharacterId> departed = isMember || members.Length == 0
            ? this.EndRun(aggregate)
            : [this.Release(aggregate, characterId)];
        return new(aggregate.RunId, aggregate.Layout.DungeonId, failed, departed);
    }

    private static bool FailsOnDeparture(DungeonRunAggregate aggregate) =>
        aggregate.Layout.AppliesFailureWeakness && !aggregate.Cells[aggregate.CurrentCell].ClearOpen;

    private async ValueTask RecordDeparturesAsync(RunDeparture departure)
    {
        foreach (var characterId in departure.Characters)
        {
            if (this.lifecycle is not null)
                await this.lifecycle.RecordDepartureAsync(
                    new(characterId, departure.RunId, departure.DungeonId)).ConfigureAwait(false);
            if (departure.Failed && this.weakness is not null)
                await this.weakness.For(characterId).SetWeakness().ConfigureAwait(false);
        }
    }

    private sealed record RunDeparture(
        DungeonRunId RunId, int DungeonId, bool Failed, IReadOnlyList<CharacterId> Characters);

    /// <summary>
    /// What leaving does to one character on every path (Spec RQ-008): the occupancy, recovery,
    /// pet-combat and room marks the run holds for them, and their pending settlement and card
    /// reservations. Lifecycle and failure weakness belong to the paths that record them.
    /// </summary>
    /// <remarks>
    /// The character's progress stays: a clear-card commit prepared before they left still
    /// completes against it. It is only marked released, so the departure happens once.
    /// </remarks>
    private CharacterId Release(DungeonRunAggregate aggregate, CharacterId characterId)
    {
        aggregate.ProgressFor(characterId).Released = true;
        this.participation?.ReleaseCommitted(characterId, new(DungeonParticipationMode.Ordinary, aggregate.RunId.Value));
        this.weakness?.For(characterId).ResumeRecovery();
        this.creatures?.ChangeCombat(characterId, false);
        aggregate.ReadyParticipants.Remove(characterId);
        aggregate.DeadParticipants.Remove(characterId);
        aggregate.EntryRevision++;
        foreach (var key in this.settlementClaims.Keys
                     .Where(key => key.RunId == aggregate.RunId && key.CharacterId == characterId)
                     .ToArray())
        {
            this.settlementClaims.Remove(key);
        }
        foreach (var key in this.clearRewardCardClaims
                     .Where(entry => entry.Key.RunId == aggregate.RunId
                         && entry.Value.CharacterId == characterId
                         && !this.preparedClearRewardCardClaims.Contains(entry.Key))
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            this.clearRewardCardClaims.Remove(key);
        }
        return characterId;
    }

    /// <summary>Releases everyone still holding state in the run, then removes it.</summary>
    private IReadOnlyList<CharacterId> EndRun(DungeonRunAggregate aggregate)
    {
        var departing = aggregate.Unreleased();
        foreach (var characterId in departing)
            _ = this.Release(aggregate, characterId);
        this.Remove(aggregate);
        return departing;
    }

    /// <summary>The run character lookups reach: the one bound to the character's current party.</summary>
    private DungeonRunAggregate? RunOf(CharacterId characterId) =>
        this.runsByParty.TryGetValue(this.parties.KeyOf(characterId), out var aggregate)
            && aggregate.IsMember(characterId)
                ? aggregate
                : null;

    private void Publish(DungeonRunAggregate aggregate)
    {
        this.runsById.Add(aggregate.RunId, aggregate);
        this.runsByParty.Add(aggregate.PartyKey, aggregate);
    }

    private void Remove(DungeonRunAggregate aggregate)
    {
        this.runsById.Remove(aggregate.RunId);
        if (this.runsByParty.GetValueOrDefault(aggregate.PartyKey) == aggregate)
            this.runsByParty.Remove(aggregate.PartyKey);
        this.mechanismWake?.Wake();
        this.clearReturnDeadlines.Remove(aggregate.RunId);
        this.wipeDeadlines.Remove(aggregate.RunId);
        this.ClearDeathClaims(aggregate.RunId);
        this.ClearParticipantEffectClaims(aggregate.RunId);
        this.ClearSettlementClaims(aggregate.RunId);
        this.ClearGroundDropClaims(aggregate.RunId);
        this.ClearClearRewardCardClaims(aggregate.RunId);
    }

    private bool TryRemoveClaim(DeathClaimKey key, long token) =>
        this.deathClaims.TryGetValue(key, out var current)
        && current == token
        && this.deathClaims.Remove(key);

    private bool TryRemoveParticipantEffectClaim(ParticipantEffectClaimKey key, long token) =>
        this.participantEffectClaims.TryGetValue(key, out var current)
        && current == token
        && this.participantEffectClaims.Remove(key);

    private bool TryRemoveSettlementClaim(SettlementClaimKey key, long token) =>
        this.settlementClaims.TryGetValue(key, out var current)
        && current == token
        && this.settlementClaims.Remove(key);

    private bool TryRemoveGroundDropClaim(GroundDropClaimKey key, long token) =>
        this.groundDropClaims.TryGetValue(key, out var current)
        && current == token
        && this.groundDropClaims.Remove(key);

    private PreparedGroundDrop PrepareGroundDrop(DungeonGroundDropClaim claim)
    {
        lock (this.gate)
        {
            var key = new GroundDropClaimKey(claim.RunId, claim.Drop, claim.CharacterId);
            if (!this.groundDropClaims.TryGetValue(key, out var token)
                || token != claim.Token
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || aggregate.CurrentCell != claim.Drop.Cell
                || aggregate.RoomVersion != claim.RoomVersion
                || !aggregate.Cells.TryGetValue(claim.Drop.Cell, out var cell)
                || cell.Revision != claim.CellRevision
                || cell.FindGroundItem(claim.Drop.Number) is null
                || !this.preparedGroundDropClaims.Add(key))
            {
                throw new InvalidOperationException(
                    "The ground-drop reservation changed before its transaction prepared.");
            }

            return new PreparedGroundDrop(key, aggregate);
        }
    }

    private DungeonPickupChange CommitPreparedGroundDrop(
        PreparedGroundDrop prepared,
        DungeonPickupKind kind)
    {
        lock (this.gate)
        {
            // Prepare owns the only reservation for this run/drop. Even if the last participant
            // disconnects after prepare and the aggregate is detached from the directory, this
            // captured aggregate remains the transaction's exact target and commit is infallible.
            _ = this.groundDropClaims.Remove(prepared.Key);
            _ = this.preparedGroundDropClaims.Remove(prepared.Key);
            var cell = prepared.Aggregate.Cells[prepared.Key.Drop.Cell];
            _ = cell.RemoveGroundItem(prepared.Key.Drop.Number)
                ?? throw new InvalidOperationException(
                    "A prepared ground item must still exist when its transaction commits.");
            prepared.Aggregate.RuntimeRevision++;
            return new DungeonPickupChange(
                prepared.Aggregate.RunId,
                prepared.Aggregate.RuntimeRevision,
                prepared.Key.CharacterId,
                prepared.Key.Drop,
                kind,
                [.. prepared.Aggregate.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)],
                prepared.Aggregate.RoomVersion,
                cell.Revision);
        }
    }

    private void RollbackPreparedGroundDrop(
        DungeonGroundDropClaim claim,
        PreparedGroundDrop? prepared)
    {
        lock (this.gate)
        {
            if (prepared is { } value)
                _ = this.preparedGroundDropClaims.Remove(value.Key);
            _ = this.TryRemoveGroundDropClaim(
                new GroundDropClaimKey(claim.RunId, claim.Drop, claim.CharacterId), claim.Token);
        }
    }

    private bool TryRemoveClearRewardCardClaim(ClearRewardCardClaimKey key, long token) =>
        this.clearRewardCardClaims.TryGetValue(key, out var current)
        && current.Token == token
        && this.clearRewardCardClaims.Remove(key);

    private void ClearDeathClaims(DungeonRunId runId)
    {
        foreach (var key in this.deathClaims.Keys.Where(key => key.RunId == runId).ToArray())
            this.deathClaims.Remove(key);
    }

    private void ClearParticipantEffectClaims(DungeonRunId runId)
    {
        foreach (var key in this.participantEffectClaims.Keys
                     .Where(key => key.RunId == runId).ToArray())
        {
            this.participantEffectClaims.Remove(key);
        }
    }

    private void ClearSettlementClaims(DungeonRunId runId)
    {
        foreach (var key in this.settlementClaims.Keys
                     .Where(key => key.RunId == runId).ToArray())
        {
            this.settlementClaims.Remove(key);
        }
    }

    private void ClearGroundDropClaims(DungeonRunId runId)
    {
        foreach (var key in this.groundDropClaims.Keys
                     .Where(key => key.RunId == runId).ToArray())
        {
            this.groundDropClaims.Remove(key);
        }
    }

    private void ClearClearRewardCardClaims(DungeonRunId runId)
    {
        foreach (var key in this.clearRewardCardClaims.Keys
                     .Where(key => key.RunId == runId
                         && !this.preparedClearRewardCardClaims.Contains(key)).ToArray())
        {
            this.clearRewardCardClaims.Remove(key);
        }
    }

    private readonly record struct DeathClaimKey(DungeonRunId RunId, RoomActorId Actor);
    private readonly record struct ParticipantEffectKey(RoomActorId Actor, CharacterId CharacterId);
    private readonly record struct ParticipantEffectClaimKey(
        DungeonRunId RunId, ParticipantEffectKey Effect);
    private readonly record struct SettlementClaimKey(DungeonRunId RunId, CharacterId CharacterId);
    private readonly record struct GroundDropClaimKey(
        DungeonRunId RunId,
        GroundDropId Drop,
        CharacterId CharacterId);
    private readonly record struct GroundDropCommitIdentity(
        DungeonRunDomainService Owner,
        DungeonRunId RunId,
        GroundDropId Drop,
        CharacterId CharacterId,
        long Token);
    private sealed record PreparedGroundDrop(
        GroundDropClaimKey Key,
        DungeonRunAggregate Aggregate);
    private readonly record struct ClearRewardCardClaimKey(
        DungeonRunId RunId,
        CharacterId CharacterId,
        ClearRewardCardId Card);
    private readonly record struct ClearRewardReservation(CharacterId CharacterId, long Token);
    private readonly record struct ClearRewardCardCommitIdentity(
        DungeonRunDomainService Owner,
        DungeonRunId RunId,
        ClearRewardCardId Card,
        CharacterId CharacterId,
        long Token);
    private sealed record PreparedClearRewardCard(
        ClearRewardCardClaimKey Key,
        CharacterId CharacterId,
        long Token,
        DungeonRunAggregate Aggregate,
        byte Slot);

    private sealed class GroundDropCommitParticipant(
        DungeonRunDomainService owner,
        DungeonGroundDropClaim claim,
        DungeonPickupKind kind) : ITransactionParticipant
    {
        private PreparedGroundDrop? prepared;

        public bool CommitCanFail => false;

        public DungeonPickupChange? Change { get; private set; }

        public DungeonPickupCommit Receipt => new(() => this.Change);

        public void RequireKind(DungeonPickupKind expected)
        {
            if (expected != kind)
                throw new InvalidOperationException(
                    "One ground-drop reservation cannot be committed as two pickup kinds.");
        }

        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.prepared = owner.PrepareGroundDrop(claim);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            this.Change = owner.CommitPreparedGroundDrop(
                this.prepared ?? throw new InvalidOperationException(
                    "A ground-drop commit must be prepared before it is applied."),
                kind);
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            owner.RollbackPreparedGroundDrop(claim, this.prepared);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ClearRewardCardCommitParticipant(
        DungeonRunDomainService owner,
        DungeonClearRewardCardClaim claim) : ITransactionParticipant
    {
        private PreparedClearRewardCard? prepared;

        public bool CommitCanFail => false;

        public DungeonCardSelectionChange? Change { get; private set; }

        public DungeonCardSelectionCommit Receipt => new(() => this.Change);

        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.prepared = owner.TryPrepareClearRewardCard(claim)
                ?? throw new InvalidOperationException(
                    "The clear-card reservation changed before its transaction prepared.");
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            this.Change = owner.CommitPreparedClearRewardCard(
                this.prepared ?? throw new InvalidOperationException(
                    "A clear-card commit must be prepared before it is applied."));
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            owner.RollbackPreparedClearRewardCard(claim, this.prepared);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DungeonRunAggregate
    {
        private readonly IPartyService parties;
        private readonly ImmutableArray<DungeonRunParticipant> entryParticipants;
        // The entry roster: accounts of who entered, and the bound that keeps anyone else out of the
        // membership. It is not the membership; the party answers that.
        private readonly ImmutableDictionary<CharacterId, AccountId> entryAccounts;
        // Fixed to the entrants at entry; only each entrant's progress changes.
        private readonly ImmutableDictionary<CharacterId, ParticipantProgress> progressByCharacter;
        private readonly List<DungeonKillRecord> kills = [];
        private int nextGroundItemNumber = 1;

        public DungeonRunAggregate(
            DungeonRunId runId,
            PartyKey partyKey,
            IPartyService parties,
            ImmutableArray<DungeonRunParticipant> participants,
            DungeonLayout layout,
            DungeonDifficulty difficulty,
            DateTimeOffset startedAt,
            DungeonCellSeed initialCell)
        {
            this.RunId = runId;
            this.PartyKey = partyKey;
            this.parties = parties;
            this.entryParticipants = participants;
            this.Layout = layout;
            this.Difficulty = difficulty;
            this.StartedAt = startedAt;
            this.CurrentCell = layout.StartCell;
            this.Cells = new Dictionary<MazeCell, DungeonCellAggregate>
            {
                [initialCell.Cell] = this.CreateCell(initialCell),
            };
            this.VisitedCells = ImmutableHashSet.Create(layout.StartCell);
            this.entryAccounts = participants.ToImmutableDictionary(
                participant => participant.CharacterId, participant => participant.AccountId);
            this.progressByCharacter = participants.ToImmutableDictionary(
                participant => participant.CharacterId,
                _ => new ParticipantProgress());
        }

        // A21 resolves ground objects across retained rooms by number alone.
        // Keep the wire identity unique for this run, even after pickup or a room revisit.
        public DungeonCellAggregate CreateCell(DungeonCellSeed seed) =>
            new(seed, this.AllocateGroundItemNumbers);

        private int AllocateGroundItemNumbers(int count)
        {
            if (count > ushort.MaxValue - this.nextGroundItemNumber + 1)
                throw new InvalidOperationException(
                    "The dungeon run exhausted its stable ground-item number space.");
            var first = this.nextGroundItemNumber;
            this.nextGroundItemNumber += count;
            return first;
        }

        public DungeonRunId RunId { get; }
        public PartyKey PartyKey { get; }
        /// <summary>The run's members: its party's current members who entered it, with the party's slots and leader.</summary>
        public ImmutableArray<DungeonRunParticipant> Participants
        {
            get
            {
                var membership = this.parties.FindByKey(this.PartyKey);
                if (membership is null)
                    return [];
                return [.. membership.Members
                    .Where(member => this.entryAccounts.ContainsKey(member.CharacterId))
                    .Select(member => new DungeonRunParticipant(
                        this.entryAccounts[member.CharacterId],
                        member.CharacterId,
                        member.Slot,
                        member.CharacterId == membership.LeaderCharacterId))];
            }
        }
        /// <summary>Everyone who entered, in entry order.</summary>
        public IEnumerable<CharacterId> EntrantIds =>
            this.entryParticipants.Select(participant => participant.CharacterId);
        public DungeonRunParticipant Leader =>
            this.Participants.Single(participant => participant.IsLeader);
        public DateTimeOffset? ClearedAt { get; set; }
        public DungeonLayout Layout { get; }
        public DungeonDifficulty Difficulty { get; }
        public DateTimeOffset StartedAt { get; }
        public MazeCell CurrentCell { get; set; }
        public int CurrentRoomMapId => this.Cells[this.CurrentCell].MapId;
        public Dictionary<MazeCell, DungeonCellAggregate> Cells { get; }
        public long RoomVersion { get; set; }
        public long EntryRevision { get; set; } = 1;
        public long RuntimeRevision { get; set; }
        public ImmutableHashSet<MazeCell> VisitedCells { get; set; }
        public Dictionary<CharacterId, DungeonKillExperienceSummary> KillExperiences { get; } = [];

        public HashSet<ParticipantEffectKey> CompletedKillEffects { get; } = [];
        // Derived from committed deaths: no second mutable element-state container to drift.
        public IEnumerable<int> KilledMonsterCodes => this.kills.Select(x => x.MonsterCode);
        public HashSet<CharacterId> BuffParticipants { get; } = [];
        public HashSet<CharacterId> ReadyParticipants { get; } = [];

        public HashSet<CharacterId> DeadParticipants { get; } = [];
        public List<DungeonCardSelection> CardSelections { get; } = [];
        public HashSet<ClearRewardCardKind> RevealedCardKinds { get; } = [];

        public void RecordKill(DungeonKillRecord kill)
        {
            if (this.kills.Any(existing => existing.Actor == kill.Actor))
                throw new InvalidOperationException("One dungeon actor cannot die twice.");
            this.kills.Add(kill);
        }

        public IReadOnlyList<DungeonCardSelection> PublishedCardSelections() =>
            this.PublishedCardSelections(this.Participants);

        private IReadOnlyList<DungeonCardSelection> PublishedCardSelections(
            ImmutableArray<DungeonRunParticipant> members) =>
            [.. this.CardSelections
                .Where(selection => this.RevealedCardKinds.Contains(selection.Kind)
                    && members.Any(member => member.CharacterId == selection.CharacterId))
                .OrderBy(selection => selection.Kind)
                .ThenBy(selection => selection.Index)];

        public bool IsMember(CharacterId characterId) =>
            this.Participants.Any(participant => participant.CharacterId == characterId);

        /// <summary>Whether the character entered this run and has not departed it yet.</summary>
        public bool HoldsRunState(CharacterId characterId) =>
            this.progressByCharacter.TryGetValue(characterId, out var progress) && !progress.Released;

        /// <summary>Everyone still holding state in this run: current members first, by slot.</summary>
        public CharacterId[] Unreleased() =>
            [.. this.Participants.OrderBy(participant => participant.Slot)
                .Select(participant => participant.CharacterId)
                .Concat(this.entryParticipants.OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId))
                .Distinct()
                .Where(this.HoldsRunState)];

        public ParticipantProgress ProgressFor(CharacterId characterId) =>
            this.progressByCharacter[characterId];

        public DungeonEntryChange EntryChange(
            DungeonEntryChangeKind kind,
            CharacterId viewer) =>
            new(
                kind,
                this.ViewFor(viewer),
                this.EntryRevision,
                [.. this.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);

        public DungeonRunState ViewFor(CharacterId characterId)
        {
            var progress = this.ProgressFor(characterId);
            var members = this.Participants;
            return new DungeonRunState(
                this.Layout,
                this.CurrentCell,
                this.CurrentRoomMapId,
                this.VisitedCells,
                this.StartedAt,
                this.Difficulty,
                progress.IsSettled,
                progress.ClearRewardCards,
                this.RunId,
                this.RoomVersion,
                members.Single(participant => participant.CharacterId == characterId),
                members,
                this.EntryRevision,
                this.ReadyParticipants.ToImmutableHashSet(),
                this.Cells[this.CurrentCell].ClearOpen,
                this.RuntimeRevision,
                this.PublishedCardSelections(members),
                progress.CardLayoutOpen,
                this.Cells[this.CurrentCell].Snapshot())
            {
                PartyKey = this.PartyKey,
                DeadParticipants = this.DeadParticipants.ToImmutableHashSet(),
                EntryPurposeOwner = this.entryParticipants.Single(participant => participant.IsLeader),
                Kills = this.kills.ToImmutableArray(),
                ClearedAt = this.ClearedAt,
                ClearedCells = this.Cells.Where(x => x.Value.IsCleared).Select(x => x.Key).ToImmutableHashSet(),
                KillExperience = this.KillExperiences.GetValueOrDefault(characterId) ?? new(),
            };
        }
    }

    private sealed class DungeonCellAggregate
    {
        private readonly DungeonCellObjectState[] objects;
        public IReadOnlyList<DungeonRoomMechanismState> Mechanisms { get; set; } = [];
        public bool MechanismBlocksClear { get; set; }
        public readonly Dictionary<int, long> ObjectReservations = [];
        public readonly Dictionary<int, Queue<int>> ObjectQueues;
        private readonly Dictionary<ushort, DungeonCellActorState> actors;
        private readonly Dictionary<int, DungeonCellHellGroupState> hellGroups;
        private readonly Dictionary<ushort, DungeonGroundItem> groundItems;
        private readonly Func<int, int> allocateGroundItemNumbers;

        public DungeonCellAggregate(DungeonCellSeed seed, Func<int, int> allocateGroundItemNumbers)
        {
            this.MechanismBlocksClear = seed.MechanismBlocksClear;
            this.Mechanisms = seed.Mechanisms.Select(definition => new DungeonRoomMechanismState(definition)).ToArray();
            this.objects = seed.Objects.Select(obj => obj with { PendingLoot = obj.PendingLoot.ToArray() }).ToArray();
            this.ObjectQueues = this.objects.Select((value, index) => (value, index))
                .Where(entry => !entry.value.IsConsumed)
                .GroupBy(entry => entry.value.Object.Code)
                .ToDictionary(group => group.Key, group => new Queue<int>(group.Select(entry => entry.index)));
            this.actors = seed.Actors.ToDictionary(
                actor => actor.Sequence,
                actor => actor with { PendingLoot = actor.PendingLoot.ToArray() });
            this.hellGroups = seed.HellGroups.ToDictionary(group => group.GroupIndex,
                group => group with { ActorSequences = group.ActorSequences.ToArray(), PendingLoot = group.PendingLoot.ToArray() });
            foreach (var group in this.hellGroups.Values)
                if (group.ActorSequences.Count == 0 || group.ActorSequences.Distinct().Count() != group.ActorSequences.Count
                    || group.ActorSequences.Any(sequence => !this.actors.TryGetValue(sequence, out var actor)
                        || !actor.IsHellTemplate || actor.HellGroupIndex != group.GroupIndex))
                    throw new InvalidDataException("Hell reward group membership does not match cell actors.");
            if (this.actors.Values.Any(actor => actor.IsHellTemplate && (actor.HellGroupIndex is not { } id
                || !this.hellGroups.TryGetValue(id, out var group) || !group.ActorSequences.Contains(actor.Sequence))))
                throw new InvalidDataException("Hell cell actor has no reward group.");
            this.allocateGroundItemNumbers = allocateGroundItemNumbers;
            var first = allocateGroundItemNumbers(seed.GroundItems.Count);
            this.groundItems = seed.GroundItems
                .Select((item, index) => item with { Number = checked((ushort)(first + index)) })
                .ToDictionary(item => item.Number);
            this.Cell = seed.Cell;
            this.MapId = seed.MapId;
            this.FirstActorSequence = seed.FirstActorSequence;
        }

        public MazeCell Cell { get; }
        public int MapId { get; }
        public ushort FirstActorSequence { get; }
        public long Revision { get; set; }
        public bool ClearOpen { get; set; }
        public bool IsCleared => !this.MechanismBlocksClear && this.actors.Values.Where(x => x.BlocksClear).All(x => x.IsDead);
        public bool WillClearAfterDeath(ushort sequence, IReadOnlyList<ushort> suppressed) =>
            !this.MechanismBlocksClear && this.actors.Values.All(actor => !actor.BlocksClear || actor.IsDead
                || actor.Sequence == sequence || suppressed.Contains(actor.Sequence));
        public bool HasLivingBossExcept(ushort sequence) => this.actors.Values.Any(actor =>
            actor.Sequence != sequence && actor.BlocksClear && actor.Actor.IsBoss && !actor.IsDead);

        public DungeonCellObjectState ObjectAt(int index) => this.objects[index];

        public bool HasUnopenedRewardObject(int dungeonId, int objectIndex, ISealedTreasureRuleService rules) =>
            this.objects.Any(x => x.Object.Id.Section == DungeonObjectSection.SpecialPassive
                && x.Object.Id.Index == objectIndex && !x.IsConsumed
                && rules.RequiresDestroyedObject(dungeonId, x.Object.Code));

        public void ConsumeObject(int index)
        {
            this.objects[index] = this.objects[index] with { IsConsumed = true, PendingLoot = [] };
            this.Revision++;
        }

        public void AddActors(IReadOnlyList<DungeonCellActorState> additions)
        {
            foreach (var actor in additions) this.actors.Add(actor.Sequence, actor);
            this.Revision++;
        }

        public DungeonCellActorState? FindActor(ushort sequence) =>
            this.actors.GetValueOrDefault(sequence);

        public DungeonGroundItem? FindGroundItem(ushort number) =>
            this.groundItems.GetValueOrDefault(number);

        public DungeonGroundItem? RemoveGroundItem(ushort number)
        {
            if (!this.groundItems.Remove(number, out var item))
                return null;
            this.Revision++;
            return item;
        }

        public bool CanClaimDeath(ushort sequence) =>
            this.actors.TryGetValue(sequence, out var actor) && !actor.IsDead
                && actor.Actor.Declaration?.IsDummy != true;

        public IReadOnlyList<DungeonGroundItem>? CommitActorDeath(
            ushort sequence,
            Func<RolledDrop, bool> mayMaterialize, IReadOnlyList<RolledDrop>? resolvedLoot = null)
        {
            if (!this.actors.TryGetValue(sequence, out var actor) || actor.IsDead)
                return null;

            var completedGroup = actor.HellGroupIndex is { } groupId
                && this.hellGroups.TryGetValue(groupId, out var group) && !group.IsSettled
                && group.ActorSequences.All(member => member == sequence || this.actors[member].IsDead)
                    ? group : null;
            var rewards = (resolvedLoot ?? actor.PendingLoot).Concat(completedGroup is not null && actor.EligibleForHellGroupReward
                ? completedGroup.PendingLoot : []);
            var added = this.AppendGroundItems(rewards
                .Where(rolled => rolled.Delivery == RolledDropDelivery.Ground)
                .Where(mayMaterialize)
                .Select(rolled =>
                    new DungeonGroundItemDraft(
                        rolled.ItemId,
                        rolled.Count,
                        rolled.Durability,
                        rolled.Kind == DropItemKind.Equipment,
                        DungeonGroundItemSource.FromActor(sequence),
                        rolled.Rarity,
                        rolled.Kind,
                        rolled.OwnerCharacterId)).ToArray());

            this.actors[sequence] = actor with { IsDead = true, PendingLoot = [] };
            if (completedGroup is not null)
                this.hellGroups[completedGroup.GroupIndex] = completedGroup with { IsSettled = true, PendingLoot = [] };
            this.Revision++;
            return added;
        }

        // Part of the final boss death change: no separate ground roll, reward, or revision.
        public IReadOnlyList<ushort> SuppressibleEnemies(Func<ushort, bool> canSuppress) => this.actors.Values
                .Where(actor => actor.BlocksClear && !actor.Actor.IsBoss
                    && actor.Actor.Declaration?.Source != DungeonActorSpawnSource.SpecialPassiveObject
                    && !actor.IsDead && canSuppress(actor.Sequence))
                .OrderBy(actor => actor.Sequence)
                .Select(actor => actor.Sequence)
                .ToArray();

        public void SuppressEnemies(IReadOnlyList<ushort> sequences)
        {
            foreach (var sequence in sequences)
                this.actors[sequence] = this.actors[sequence] with { IsDead = true, PendingLoot = [] };
        }

        public IReadOnlyList<DungeonGroundItem> AppendGroundItems(
            IReadOnlyList<DungeonGroundItemDraft> items)
        {
            if (items.Any(draft => draft.Count <= 0))
                throw new ArgumentOutOfRangeException(nameof(items), "Ground item count must be positive.");
            var first = this.allocateGroundItemNumbers(items.Count);

            var added = new List<DungeonGroundItem>(items.Count);
            for (var index = 0; index < items.Count; index++)
            {
                var draft = items[index];
                added.Add(new DungeonGroundItem(
                    checked((ushort)(first + index)),
                    draft.ItemId,
                    draft.Count,
                    draft.Durability,
                    draft.IsEquipment,
                    draft.Source,
                    draft.Rarity,
                    draft.Kind,
                    draft.OwnerCharacterId));
            }

            foreach (var item in added)
                this.groundItems.Add(item.Number, item);
            return added;
        }

        public DungeonCellState Snapshot() => new(
            this.Cell,
            this.MapId,
            this.Revision,
            this.FirstActorSequence,
            [.. this.actors.Values.OrderBy(actor => actor.Sequence)
                .Select(actor => actor with { PendingLoot = actor.PendingLoot.ToArray() })],
            [.. this.groundItems.Values.OrderBy(item => item.Number)],
            this.ClearOpen)
        {
            MechanismBlocksClear = this.MechanismBlocksClear,
            Mechanisms = this.Mechanisms.ToArray(),
            Objects = this.objects.Select(obj => obj with { PendingLoot = obj.PendingLoot.ToArray() }).ToArray(),
            HellGroups = this.hellGroups.Values.OrderBy(group => group.GroupIndex)
                .Select(group => group with { ActorSequences = group.ActorSequences.ToArray(), PendingLoot = group.PendingLoot.ToArray() }).ToArray(),
        };
    }

    private sealed class ParticipantProgress
    {
        public bool IsSettled { get; set; }
        public ClearRewardCardSet? ClearRewardCards { get; set; }
        public bool CardLayoutOpen { get; set; }
        /// <summary>Set once the character has departed; the progress itself is kept.</summary>
        public bool Released { get; set; }
    }
}
