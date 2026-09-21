using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.World;

/// <summary>Keeps process-local dungeon runs as shared aggregates with participant views.</summary>
internal sealed class DungeonRunDomainService : IDungeonRunDomainService
{
    private readonly IDomainTransactionAccessor? transactions;
    private readonly IPartyService? parties;
    private readonly object gate = new();
    private readonly Dictionary<DungeonRunId, DateTimeOffset> wipeDeadlines = [];
    private readonly Dictionary<CharacterId, DungeonRunAggregate> runsByCharacter = new();
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

    public DungeonRunDomainService()
    {
    }

    public DungeonRunDomainService(IDomainTransactionAccessor transactions) =>
        this.transactions = transactions;

    public DungeonRunDomainService(
        IDomainTransactionAccessor transactions,
        IPartyService parties)
    {
        this.transactions = transactions;
        this.parties = parties;
    }

    internal DungeonRunDomainService(IPartyService parties) => this.parties = parties;

    public DungeonEntryChange PreparePartyEntry(
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        DungeonDifficulty difficulty,
        DungeonCellSeed initialCell,
        DungeonRunId runId,
        DateTimeOffset startedAt)
    {
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
        var partyKey = this.parties is null
            ? PartyKey.Virtual(entryLeader.CharacterId)
            : this.parties.FindParty(new PartyCharacterSnapshot(
                entryLeader.AccountId, entryLeader.CharacterId, 0, null)).Key;
        if (this.parties is not null)
        {
            var currentParty = this.parties.FindByKey(partyKey)
                ?? throw new InvalidOperationException("The entry party no longer exists.");
            if (!currentParty.Members.Select(member => member.CharacterId)
                    .ToHashSet().SetEquals(frozen.Select(participant => participant.CharacterId)))
                throw new InvalidOperationException("The entry roster no longer matches the party.");
        }
        var aggregate = new DungeonRunAggregate(
            runId, partyKey, this.parties, frozen, layout, difficulty, startedAt, initialCell);

        lock (this.gate)
        {
            foreach (var participant in frozen)
                this.DetachParticipant(participant.CharacterId);

            this.runsById.Add(aggregate.RunId, aggregate);
            foreach (var participant in frozen)
                this.runsByCharacter.Add(participant.CharacterId, aggregate);

            return aggregate.EntryChange(
                DungeonEntryChangeKind.Prepared,
                aggregate.Leader.CharacterId);
        }
    }

    public bool StageRevival(CharacterId payer, CharacterId target, DungeonRunId runId)
    {
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
        aggregate.IsActiveMember(payer) && aggregate.IsActiveMember(target)
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

    public IReadOnlyList<DungeonRunId> FindExpiredPartyWipes(DateTimeOffset now)
    {
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
            var members = aggregate.Participants.Where(member => aggregate.ActiveParticipants.Contains(member.CharacterId)).ToArray();
            var snapshot = aggregate.ViewFor(members[0].CharacterId) with { Participants = members };
            foreach (var member in aggregate.ActiveParticipants.ToArray())
                this.DetachParticipant(member);
            return snapshot;
        }
    }

    private static bool IsPartyWiped(DungeonRunAggregate aggregate)
    {
        var members = aggregate.Participants.Where(member => aggregate.ActiveParticipants.Contains(member.CharacterId)).ToArray();
        return !aggregate.Cells[aggregate.CurrentCell].ClearOpen
            && members.Length > 0
            && members.All(member => !aggregate.ProgressFor(member.CharacterId).IsSettled
                && aggregate.DeadParticipants.Contains(member.CharacterId));
    }

    public DungeonRunState? TryMarkParticipantDead(
        CharacterId characterId, DungeonRunId expectedRunId, long expectedRoomVersion)
    {
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || !aggregate.IsActiveMember(characterId)
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
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || !aggregate.IsActiveMember(characterId)
                || !aggregate.ReadyParticipants.Add(characterId))
            {
                return null;
            }

            aggregate.EntryRevision++;
            return aggregate.EntryChange(DungeonEntryChangeKind.ParticipantReady, characterId);
        }
    }

    public DungeonEntryChange? FindEntryChange(CharacterId characterId)
    {
        lock (this.gate)
        {
            return this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                && aggregate.IsActiveMember(characterId)
                    ? aggregate.EntryChange(DungeonEntryChangeKind.Snapshot, characterId)
                    : null;
        }
    }

    public bool TryAbortEntry(
        DungeonRunId expectedRunId,
        long expectedEntryRevision,
        IReadOnlyList<CharacterId> expectedRecipients)
    {
        ArgumentNullException.ThrowIfNull(expectedRecipients);
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(expectedRunId, out var aggregate)
                || aggregate.EntryRevision != expectedEntryRevision
                || !aggregate.ActiveParticipants.SetEquals(expectedRecipients))
            {
                return false;
            }

            foreach (var characterId in expectedRecipients.ToArray())
                _ = this.DetachParticipant(characterId);
            return true;
        }
    }

    public DungeonRunState? Find(CharacterId characterId)
    {
        lock (this.gate)
        {
            return this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                && aggregate.IsActiveMember(characterId)
                ? aggregate.ViewFor(characterId)
                : null;
        }
    }

    public DungeonRunState? Find(DungeonRunId runId)
    {
        lock (this.gate)
        {
            if (!this.runsById.TryGetValue(runId, out var aggregate))
                return null;

            var active = aggregate.Participants
                .Where(participant => aggregate.ActiveParticipants.Contains(
                    participant.CharacterId))
                .OrderBy(participant => participant.Slot)
                .FirstOrDefault();
            return active is null ? null : aggregate.ViewFor(active.CharacterId);
        }
    }

    public DungeonRunState? TryMove(CharacterId characterId, MazeCell target)
    {
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
        if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
            || !this.IsCurrentLeader(aggregate, characterId)
            || (expectedRunId is { } runId && aggregate.RunId != runId)
            || (expectedRoomVersion is { } version && aggregate.RoomVersion != version)
            || this.deathClaims.Keys.Any(key =>
                key.RunId == aggregate.RunId && key.Actor.Cell == aggregate.CurrentCell)
            || this.groundDropClaims.Keys.Any(key =>
                key.RunId == aggregate.RunId && key.Drop.Cell == aggregate.CurrentCell)
            || this.preparedGroundDropClaims.Any(key =>
                key.RunId == aggregate.RunId && key.Drop.Cell == aggregate.CurrentCell)
            || Math.Abs(target.X - aggregate.CurrentCell.X)
                + Math.Abs(target.Y - aggregate.CurrentCell.Y) != 1
            || !aggregate.Layout.Rooms.TryGetValue(target, out var mapId)
            || (preparedCell is not null
                && (preparedCell.Cell != target || preparedCell.MapId != mapId)))
        {
            return null;
        }

        if (!aggregate.Cells.ContainsKey(target))
        {
            if (preparedCell is null || !preparedCell.IsComplete)
                return null;
            aggregate.Cells.Add(
                target,
                new DungeonCellAggregate(preparedCell));
        }
        aggregate.CurrentCell = target;
        aggregate.VisitedCells = aggregate.VisitedCells.Add(target);
        aggregate.RoomVersion++;
        aggregate.EntryRevision++;
        aggregate.ReadyParticipants.Clear();
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
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || !aggregate.IsActiveMember(characterId))
            {
                return null;
            }

            var drop = new GroundDropId(aggregate.CurrentCell, groundDropNumber);
            var cell = aggregate.Cells[aggregate.CurrentCell];
            var item = cell.FindGroundItem(groundDropNumber);
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
                    .Where(participant => aggregate.ActiveParticipants.Contains(
                        participant.CharacterId))
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
        ArgumentNullException.ThrowIfNull(items);
        lock (this.gate)
        {
            if (items.Count == 0
                || !this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || !aggregate.IsActiveMember(characterId))
            {
                return null;
            }

            var cell = aggregate.Cells[aggregate.CurrentCell];
            if (cell.Revision != expectedCellRevision)
                return null;
            var eligible = items.Where(item => item.OwnerCharacterId is not { } owner
                || aggregate.IsActiveMember(owner)).ToArray();
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
                    .Where(participant => aggregate.ActiveParticipants.Contains(
                        participant.CharacterId))
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);
        }
    }

    public DungeonClearChange? TryOpenClear(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion)
    {
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || !aggregate.IsActiveMember(characterId)
                || aggregate.Cells[aggregate.CurrentCell].ClearOpen)
            {
                return null;
            }

            var cell = aggregate.Cells[aggregate.CurrentCell];
            cell.ClearOpen = true;
            cell.Revision++;
            aggregate.RuntimeRevision++;
            return new DungeonClearChange(
                aggregate.RunId,
                aggregate.RuntimeRevision,
                [.. aggregate.Participants
                    .Where(participant => aggregate.ActiveParticipants.Contains(
                        participant.CharacterId))
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);
        }
    }

    public DungeonActorDeathClaim? TryClaimActorDeath(
        CharacterId characterId,
        ushort actorSequence)
    {
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || !aggregate.IsActiveMember(characterId))
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
                cell.FindActor(actorSequence));
        }
    }

    public DungeonRunState? CommitActorDeath(DungeonActorDeathClaim claim) =>
        this.CommitActorDeathChange(claim)?.Run;

    public DungeonActorDeathChange? CommitActorDeathChange(DungeonActorDeathClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new DeathClaimKey(claim.RunId, claim.Actor);
            if (!this.TryRemoveClaim(key, claim.Token)
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || !aggregate.IsActiveMember(claim.CharacterId))
            {
                return null;
            }

            var cell = aggregate.Cells[claim.Actor.Cell];
            var killedActor = cell.FindActor(claim.Actor.Sequence);
            var added = cell.CommitActorDeath(
                claim.Actor.Sequence,
                rolled => rolled.OwnerCharacterId is not { } owner
                    || aggregate.IsActiveMember(owner));
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
            var autoKilled = killedActor is { IsMonster: true, Actor.ActorType: (byte)MonsterRank.Boss }
                && aggregate.Layout.KindOf(claim.Actor.Cell) == DungeonRoomKind.Boss
                    ? cell.SuppressRemainingMonsters(sequence =>
                        !this.deathClaims.ContainsKey(new DeathClaimKey(
                            claim.RunId, new RoomActorId(claim.Actor.Cell, sequence))))
                    : [];
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
                    .Where(participant => aggregate.ActiveParticipants.Contains(
                        participant.CharacterId))
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)])
            {
                AutoKilledActorSequences = autoKilled,
            };
        }
    }

    public bool ReleaseActorDeath(DungeonActorDeathClaim claim)
    {
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
        ArgumentNullException.ThrowIfNull(death);
        lock (this.gate)
        {
            var deathKey = new DeathClaimKey(death.RunId, death.Actor);
            if (!this.deathClaims.TryGetValue(deathKey, out var deathToken)
                || deathToken != death.Token
                || !this.runsById.TryGetValue(death.RunId, out var aggregate)
                || !aggregate.Participants.Any(
                    participant => participant.CharacterId == characterId)
                || !aggregate.IsActiveMember(characterId))
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

            return aggregate.CompletedKillEffects.Add(effect);
        }
    }

    public bool ReleaseParticipantKillEffect(DungeonParticipantKillEffectClaim claim)
    {
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
        var claim = this.TryClaimSettlement(characterId);
        return claim is null ? null : this.CommitSettlement(claim);
    }

    public DungeonSettlementClaim? TryClaimSettlement(CharacterId characterId)
        => this.TryClaimSettlementCore(characterId, expectedRunId: null);

    public DungeonSettlementClaim? TryClaimSettlement(
        CharacterId characterId,
        DungeonRunId expectedRunId) =>
        this.TryClaimSettlementCore(characterId, expectedRunId);

    private DungeonSettlementClaim? TryClaimSettlementCore(
        CharacterId characterId,
        DungeonRunId? expectedRunId)
    {
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || !aggregate.IsActiveMember(characterId)
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
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new SettlementClaimKey(claim.RunId, claim.CharacterId);
            if (!this.TryRemoveSettlementClaim(key, claim.Token)
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || !aggregate.IsActiveMember(claim.CharacterId))
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
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || aggregate.RunId != expectedRunId
                || !aggregate.IsActiveMember(characterId))
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
        => this.FixClearRewardCardsCore(
            characterId, expectedRunId: null, free, paid, paidFlipCost);

    public DungeonRunState? FixClearRewardCards(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost) =>
        this.FixClearRewardCardsCore(characterId, expectedRunId, free, paid, paidFlipCost);

    private DungeonRunState? FixClearRewardCardsCore(
        CharacterId characterId,
        DungeonRunId? expectedRunId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost)
    {
        ArgumentNullException.ThrowIfNull(free);
        ArgumentNullException.ThrowIfNull(paid);
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || !aggregate.IsActiveMember(characterId))
                return null;
            if (expectedRunId is { } runId && aggregate.RunId != runId)
                return null;

            var progress = aggregate.ProgressFor(characterId);
            progress.ClearRewardCards ??= new ClearRewardCardSet(
                free, paid, ImmutableHashSet<ClearRewardCardId>.Empty, paidFlipCost);
            return aggregate.ViewFor(characterId);
        }
    }

    public ClearRewardCardSlot? TryClaimClearRewardCard(
        CharacterId characterId,
        ClearRewardCardKind kind,
        int index)
    {
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || !aggregate.IsActiveMember(characterId))
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
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || aggregate.RunId != expectedRunId
                || !aggregate.IsActiveMember(characterId))
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
                || !aggregate.IsActiveMember(claim.CharacterId)
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
                prepared.Slot));
            // A free-card confirmation is private until every current participant has confirmed.
            // The wire index is not the shared display column; the party seat is.
            var ready = prepared.Key.Card.Kind is ClearRewardCardKind.Paid
                || prepared.Aggregate.ActiveParticipants.All(characterId =>
                    prepared.Aggregate.CardSelections.Any(selection =>
                        selection.Kind is ClearRewardCardKind.Free
                        && selection.CharacterId == characterId));
            if (ready)
                prepared.Aggregate.RevealedCardKinds.Add(prepared.Key.Card.Kind);
            prepared.Aggregate.RuntimeRevision++;
            return new DungeonCardSelectionChange(
                prepared.Aggregate.RunId,
                prepared.Aggregate.RuntimeRevision,
                prepared.CharacterId,
                ready ? prepared.Aggregate.PublishedCardSelections() : [],
                [.. prepared.Aggregate.Participants
                    .Where(participant => prepared.Aggregate.ActiveParticipants.Contains(
                        participant.CharacterId))
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
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new ClearRewardCardClaimKey(claim.RunId, claim.CharacterId, claim.Card);
            return !this.preparedClearRewardCardClaims.Contains(key)
                && this.TryRemoveClearRewardCardClaim(key, claim.Token);
        }
    }

    public bool End(CharacterId characterId)
    {
        lock (this.gate)
        {
            return this.DetachParticipant(characterId);
        }
    }

    public bool End(CharacterId characterId, DungeonRunId expectedRunId)
    {
        lock (this.gate)
        {
            return this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                && aggregate.RunId == expectedRunId
                && this.DetachParticipant(characterId);
        }
    }

    private bool DetachParticipant(CharacterId characterId)
    {
        if (!this.runsByCharacter.Remove(characterId, out var aggregate))
            return false;

        aggregate.ActiveParticipants.Remove(characterId);
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
        if (aggregate.ActiveParticipants.Count == 0)
        {
            this.runsById.Remove(aggregate.RunId);
            this.wipeDeadlines.Remove(aggregate.RunId);
            this.ClearDeathClaims(aggregate.RunId);
            this.ClearParticipantEffectClaims(aggregate.RunId);
            this.ClearSettlementClaims(aggregate.RunId);
            this.ClearGroundDropClaims(aggregate.RunId);
            this.ClearClearRewardCardClaims(aggregate.RunId);
        }

        return true;
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
                    .Where(participant => prepared.Aggregate.ActiveParticipants.Contains(
                        participant.CharacterId))
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
        private readonly IPartyService? parties;
        private readonly ImmutableArray<DungeonRunParticipant> entryParticipants;
        private readonly Dictionary<CharacterId, DungeonRunParticipant> participantsByCharacter;
        private readonly Dictionary<CharacterId, ParticipantProgress> progressByCharacter;
        private readonly List<DungeonKillRecord> kills = [];

        public DungeonRunAggregate(
            DungeonRunId runId,
            PartyKey partyKey,
            IPartyService? parties,
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
                [initialCell.Cell] = new DungeonCellAggregate(initialCell),
            };
            this.VisitedCells = ImmutableHashSet.Create(layout.StartCell);
            this.participantsByCharacter = participants.ToDictionary(
                participant => participant.CharacterId);
            this.progressByCharacter = participants.ToDictionary(
                participant => participant.CharacterId,
                _ => new ParticipantProgress());
            this.ActiveParticipants = [.. this.participantsByCharacter.Keys];
        }

        public DungeonRunId RunId { get; }
        public PartyKey PartyKey { get; }
        public ImmutableArray<DungeonRunParticipant> Participants
        {
            get
            {
                if (this.parties is null)
                    return this.entryParticipants;
                var membership = this.parties.FindByKey(this.PartyKey);
                if (membership is null)
                    return [];
                return [.. membership.Members
                    .Where(member => this.ActiveParticipants.Contains(member.CharacterId)
                        && this.participantsByCharacter.ContainsKey(member.CharacterId))
                    .Select(member => new DungeonRunParticipant(
                        this.participantsByCharacter[member.CharacterId].AccountId,
                        member.CharacterId,
                        member.Slot,
                        member.CharacterId == membership.LeaderCharacterId))];
            }
        }
        public DungeonRunParticipant Leader =>
            this.Participants.Single(participant => participant.IsLeader);
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
        public HashSet<ParticipantEffectKey> CompletedKillEffects { get; } = [];
        public HashSet<CharacterId> ActiveParticipants { get; }
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
            [.. this.CardSelections
                .Where(selection => this.RevealedCardKinds.Contains(selection.Kind)
                    && this.ActiveParticipants.Contains(selection.CharacterId))
                .OrderBy(selection => selection.Kind)
                .ThenBy(selection => selection.Index)];

        public bool IsActiveMember(CharacterId characterId) =>
            this.ActiveParticipants.Contains(characterId)
            && this.Participants.Any(participant => participant.CharacterId == characterId);

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
                    .Where(participant => this.ActiveParticipants.Contains(participant.CharacterId))
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);

        public DungeonRunState ViewFor(CharacterId characterId)
        {
            var progress = this.ProgressFor(characterId);
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
                this.Participants.Single(participant => participant.CharacterId == characterId),
                this.Participants,
                this.EntryRevision,
                this.ReadyParticipants.ToImmutableHashSet(),
                this.Cells[this.CurrentCell].ClearOpen,
                this.RuntimeRevision,
                this.PublishedCardSelections(),
                progress.CardLayoutOpen,
                this.Cells[this.CurrentCell].Snapshot())
            {
                PartyKey = this.PartyKey,
                DeadParticipants = this.DeadParticipants.ToImmutableHashSet(),
                EntryPurposeOwner = this.entryParticipants.Single(participant => participant.IsLeader),
                Kills = this.kills.ToImmutableArray(),
            };
        }
    }

    private sealed class DungeonCellAggregate(DungeonCellSeed seed)
    {
        private readonly Dictionary<ushort, DungeonCellActorState> actors =
            seed.Actors.ToDictionary(
                actor => actor.Sequence,
                actor => actor with { PendingLoot = actor.PendingLoot.ToArray() });
        private readonly Dictionary<ushort, DungeonGroundItem> groundItems =
            seed.GroundItems.ToDictionary(item => item.Number);
        private int nextGroundItemNumber = seed.GroundItems.Count == 0
            ? 1
            : seed.GroundItems.Max(item => item.Number) + 1;

        public MazeCell Cell { get; } = seed.Cell;
        public int MapId { get; } = seed.MapId;
        public ushort FirstActorSequence { get; } = seed.FirstActorSequence;
        public long Revision { get; set; }
        public bool ClearOpen { get; set; }

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
            this.actors.TryGetValue(sequence, out var actor) && !actor.IsDead;

        public IReadOnlyList<DungeonGroundItem>? CommitActorDeath(
            ushort sequence,
            Func<RolledDrop, bool> mayMaterialize)
        {
            if (!this.actors.TryGetValue(sequence, out var actor) || actor.IsDead)
                return null;

            var added = this.AppendGroundItems(actor.PendingLoot
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
            this.Revision++;
            return added;
        }

        // Part of the boss death change: no separate ground roll, reward, or revision.
        public IReadOnlyList<ushort> SuppressRemainingMonsters(Func<ushort, bool> canSuppress)
        {
            var sequences = this.actors.Values
                .Where(actor => actor.IsMonster && !actor.IsDead && canSuppress(actor.Sequence))
                .OrderBy(actor => actor.Sequence)
                .Select(actor => actor.Sequence)
                .ToArray();
            foreach (var sequence in sequences)
                this.actors[sequence] = this.actors[sequence] with { IsDead = true, PendingLoot = [] };
            return sequences;
        }

        public IReadOnlyList<DungeonGroundItem> AppendGroundItems(
            IReadOnlyList<DungeonGroundItemDraft> items)
        {
            if (items.Count > 0
                && this.nextGroundItemNumber + items.Count - 1 > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    "The dungeon cell exhausted its stable ground-item number space.");
            }

            var added = new List<DungeonGroundItem>(items.Count);
            for (var index = 0; index < items.Count; index++)
            {
                var draft = items[index];
                if (draft.Count <= 0)
                    throw new ArgumentOutOfRangeException(nameof(items), "Ground item count must be positive.");
                added.Add(new DungeonGroundItem(
                    checked((ushort)(this.nextGroundItemNumber + index)),
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
            this.nextGroundItemNumber += added.Count;
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
            this.ClearOpen);
    }

    private sealed class ParticipantProgress
    {
        public bool IsSettled { get; set; }
        public ClearRewardCardSet? ClearRewardCards { get; set; }
        public bool CardLayoutOpen { get; set; }
    }
}
