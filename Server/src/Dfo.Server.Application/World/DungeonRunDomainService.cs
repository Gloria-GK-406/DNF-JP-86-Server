using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

/// <summary>Keeps process-local dungeon runs as shared aggregates with participant views.</summary>
internal sealed class DungeonRunDomainService : IDungeonRunDomainService
{
    private readonly object gate = new();
    private readonly Dictionary<CharacterId, DungeonRunAggregate> runsByCharacter = new();
    private readonly Dictionary<DungeonRunId, DungeonRunAggregate> runsById = new();
    private readonly Dictionary<DeathClaimKey, long> deathClaims = new();
    private readonly Dictionary<ParticipantEffectClaimKey, long> participantEffectClaims = new();
    private readonly Dictionary<SettlementClaimKey, long> settlementClaims = new();
    private readonly Dictionary<GroundDropClaimKey, long> groundDropClaims = new();
    private readonly Dictionary<ClearRewardCardClaimKey, long> clearRewardCardClaims = new();
    private long nextClaim;

    public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, byte difficulty)
    {
        return this.BeginParty(
            [new DungeonRunParticipant(
                new AccountId(0), characterId, Slot: 0, IsLeader: true)],
            layout,
            difficulty);
    }

    public DungeonRunState BeginParty(
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        byte difficulty) => this.PreparePartyEntry(participants, layout, difficulty).Run;

    public DungeonEntryChange PreparePartyEntry(
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        byte difficulty)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(layout);
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
        var aggregate = new DungeonRunAggregate(
            DungeonRunId.New(), frozen, layout, difficulty, DateTimeOffset.UtcNow);

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
                || !aggregate.ActiveParticipants.Contains(characterId)
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
                && aggregate.ActiveParticipants.Contains(characterId)
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
                ? aggregate.ViewFor(characterId)
                : null;
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
            return this.TryMoveCore(characterId, expectedRunId, expectedRoomVersion, target);
        }
    }

    private DungeonRunState? TryMoveCore(
        CharacterId characterId,
        DungeonRunId? expectedRunId,
        long? expectedRoomVersion,
        MazeCell target)
    {
        if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
            || !aggregate.Participant(characterId).IsLeader
            || (expectedRunId is { } runId && aggregate.RunId != runId)
            || (expectedRoomVersion is { } version && aggregate.RoomVersion != version)
            || this.deathClaims.Keys.Any(key =>
                key.RunId == aggregate.RunId && key.Actor.Cell == aggregate.CurrentCell)
            || Math.Abs(target.X - aggregate.CurrentCell.X)
                + Math.Abs(target.Y - aggregate.CurrentCell.Y) != 1
            || !aggregate.Layout.Rooms.TryGetValue(target, out var mapId))
        {
            return null;
        }

        aggregate.CurrentCell = target;
        aggregate.CurrentRoomMapId = mapId;
        aggregate.VisitedCells = aggregate.VisitedCells.Add(target);
        aggregate.RoomVersion++;
        aggregate.EntryRevision++;
        aggregate.ReadyParticipants.Clear();
        return aggregate.ViewFor(characterId);
    }

    public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber)
    {
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate))
                return null;

            var id = new GroundDropId(aggregate.CurrentCell, groundDropNumber);
            if (aggregate.TakenGroundDrops.Contains(id))
                return null;

            aggregate.TakenGroundDrops = aggregate.TakenGroundDrops.Add(id);
            return aggregate.ViewFor(characterId);
        }
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
                || aggregate.RoomVersion != expectedRoomVersion)
            {
                return null;
            }

            var drop = new GroundDropId(aggregate.CurrentCell, groundDropNumber);
            var key = new GroundDropClaimKey(aggregate.RunId, drop, characterId);
            if (aggregate.TakenGroundDrops.Contains(drop)
                || this.groundDropClaims.Keys.Any(candidate =>
                    candidate.RunId == aggregate.RunId && candidate.Drop == drop))
                return null;

            var token = Interlocked.Increment(ref this.nextClaim);
            this.groundDropClaims.Add(key, token);
            return new DungeonGroundDropClaim(
                characterId, aggregate.ViewFor(characterId), drop, token, aggregate.RunId);
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
            if (!this.TryRemoveGroundDropClaim(key, claim.Token)
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || aggregate.TakenGroundDrops.Contains(claim.Drop))
            {
                return null;
            }

            aggregate.TakenGroundDrops = aggregate.TakenGroundDrops.Add(claim.Drop);
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
                    .Select(participant => participant.CharacterId)]);
        }
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

    public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence)
    {
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate))
                return null;

            var actor = new RoomActorId(aggregate.CurrentCell, actorSequence);
            if (aggregate.DeadActors.Contains(actor))
                return null;

            aggregate.DeadActors = aggregate.DeadActors.Add(actor);
            aggregate.CommittedActorDeaths.Add(actor);
            return aggregate.ViewFor(characterId);
        }
    }

    public DungeonActorDeathClaim? TryClaimActorDeath(
        CharacterId characterId,
        ushort actorSequence)
    {
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate))
                return null;

            var actor = new RoomActorId(aggregate.CurrentCell, actorSequence);
            if (aggregate.CommittedActorDeaths.Contains(actor))
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
                aggregate.RunId);
        }
    }

    public DungeonRunState? CommitActorDeath(DungeonActorDeathClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new DeathClaimKey(claim.RunId, claim.Actor);
            if (!this.TryRemoveClaim(key, claim.Token)
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || !aggregate.ActiveParticipants.Contains(claim.CharacterId))
            {
                return null;
            }

            aggregate.DeadActors = aggregate.DeadActors.Add(claim.Actor);
            aggregate.CommittedActorDeaths.Add(claim.Actor);
            return aggregate.ViewFor(claim.CharacterId);
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
                || !aggregate.ActiveParticipants.Contains(characterId))
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
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate))
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
                || !aggregate.ActiveParticipants.Contains(claim.CharacterId))
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
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate))
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
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate))
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
                || aggregate.RunId != expectedRunId)
            {
                return null;
            }

            var progress = aggregate.ProgressFor(characterId);
            var card = new ClearRewardCardId(kind, index);
            if (progress.ClearRewardCards is not { } cards
                || cards.SlotAt(kind, index) is not { } slot
                || cards.Claimed.Contains(card))
            {
                return null;
            }

            var key = new ClearRewardCardClaimKey(aggregate.RunId, characterId, card);
            if (this.clearRewardCardClaims.ContainsKey(key))
                return null;

            var token = Interlocked.Increment(ref this.nextClaim);
            this.clearRewardCardClaims.Add(key, token);
            return new DungeonClearRewardCardClaim(
                characterId,
                aggregate.ViewFor(characterId),
                card,
                slot,
                token,
                aggregate.RunId);
        }
    }

    public bool CommitClearRewardCard(DungeonClearRewardCardClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            var key = new ClearRewardCardClaimKey(claim.RunId, claim.CharacterId, claim.Card);
            if (!this.TryRemoveClearRewardCardClaim(key, claim.Token)
                || !this.runsById.TryGetValue(claim.RunId, out var aggregate)
                || !aggregate.ActiveParticipants.Contains(claim.CharacterId))
            {
                return false;
            }

            var progress = aggregate.ProgressFor(claim.CharacterId);
            if (progress.ClearRewardCards is not { } cards
                || cards.Claimed.Contains(claim.Card))
            {
                return false;
            }

            progress.ClearRewardCards = cards with { Claimed = cards.Claimed.Add(claim.Card) };
            return true;
        }
    }

    public bool ReleaseClearRewardCard(DungeonClearRewardCardClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        lock (this.gate)
        {
            return this.TryRemoveClearRewardCardClaim(
                new ClearRewardCardClaimKey(claim.RunId, claim.CharacterId, claim.Card),
                claim.Token);
        }
    }

    public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(
        CharacterId characterId,
        int declaredDropCount,
        ushort actorSequence,
        IReadOnlyList<RolledDrop> rolled)
    {
        ArgumentNullException.ThrowIfNull(rolled);
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate))
                return [];

            var actor = new RoomActorId(aggregate.CurrentCell, actorSequence);
            return this.AllocateMonsterDropsCore(aggregate, declaredDropCount, actor, rolled);
        }
    }

    public IReadOnlyList<MonsterDrop>? TryAllocateMonsterDrops(
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        RoomActorId actor,
        int declaredDropCount,
        IReadOnlyList<RolledDrop> rolled)
    {
        ArgumentNullException.ThrowIfNull(rolled);
        lock (this.gate)
        {
            if (!this.runsByCharacter.TryGetValue(characterId, out var aggregate)
                || aggregate.RunId != expectedRunId
                || aggregate.RoomVersion != expectedRoomVersion
                || aggregate.CurrentCell != actor.Cell)
            {
                return null;
            }

            return this.AllocateMonsterDropsCore(
                aggregate, declaredDropCount, actor, rolled);
        }
    }

    private IReadOnlyList<MonsterDrop> AllocateMonsterDropsCore(
        DungeonRunAggregate aggregate,
        int declaredDropCount,
        RoomActorId actor,
        IReadOnlyList<RolledDrop> rolled)
    {
        if (aggregate.AllocatedDropActors.Contains(actor))
        {
            return aggregate.MonsterDrops
                .Where(drop => drop.Cell == actor.Cell && drop.ActorSequence == actor.Sequence)
                .Select(drop => drop.Drop)
                .ToArray();
        }

        aggregate.AllocatedDropActors.Add(actor);
        if (rolled.Count == 0)
            return [];

        var next = aggregate.NextMonsterDropNumber(actor.Cell, declaredDropCount);
        var numbered = new List<MonsterDrop>(rolled.Count);
        var allocated = aggregate.MonsterDrops.ToBuilder();
        foreach (var piece in rolled)
        {
            var drop = new MonsterDrop(next++, piece);
            numbered.Add(drop);
            allocated.Add(new AllocatedMonsterDrop(actor.Cell, actor.Sequence, drop));
        }

        aggregate.MonsterDrops = allocated.ToImmutable();
        return numbered;
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
        aggregate.EntryRevision++;
        foreach (var key in this.settlementClaims.Keys
                     .Where(key => key.RunId == aggregate.RunId && key.CharacterId == characterId)
                     .ToArray())
        {
            this.settlementClaims.Remove(key);
        }
        foreach (var key in this.clearRewardCardClaims.Keys
                     .Where(key => key.RunId == aggregate.RunId && key.CharacterId == characterId)
                     .ToArray())
        {
            this.clearRewardCardClaims.Remove(key);
        }
        if (aggregate.ActiveParticipants.Count == 0)
        {
            this.runsById.Remove(aggregate.RunId);
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

    private bool TryRemoveClearRewardCardClaim(ClearRewardCardClaimKey key, long token) =>
        this.clearRewardCardClaims.TryGetValue(key, out var current)
        && current == token
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
                     .Where(key => key.RunId == runId).ToArray())
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
    private readonly record struct ClearRewardCardClaimKey(
        DungeonRunId RunId,
        CharacterId CharacterId,
        ClearRewardCardId Card);

    private sealed class DungeonRunAggregate
    {
        private readonly Dictionary<CharacterId, DungeonRunParticipant> participantsByCharacter;
        private readonly Dictionary<CharacterId, ParticipantProgress> progressByCharacter;

        public DungeonRunAggregate(
            DungeonRunId runId,
            ImmutableArray<DungeonRunParticipant> participants,
            DungeonLayout layout,
            byte difficulty,
            DateTimeOffset startedAt)
        {
            this.RunId = runId;
            this.Participants = participants;
            this.Layout = layout;
            this.Difficulty = difficulty;
            this.StartedAt = startedAt;
            this.CurrentCell = layout.StartCell;
            this.CurrentRoomMapId = layout.StartRoomMapId;
            this.VisitedCells = ImmutableHashSet.Create(layout.StartCell);
            this.participantsByCharacter = participants.ToDictionary(
                participant => participant.CharacterId);
            this.progressByCharacter = participants.ToDictionary(
                participant => participant.CharacterId,
                _ => new ParticipantProgress());
            this.ActiveParticipants = [.. this.participantsByCharacter.Keys];
        }

        public DungeonRunId RunId { get; }
        public ImmutableArray<DungeonRunParticipant> Participants { get; }
        public DungeonRunParticipant Leader =>
            this.Participants.Single(participant => participant.IsLeader);
        public DungeonLayout Layout { get; }
        public byte Difficulty { get; }
        public DateTimeOffset StartedAt { get; }
        public MazeCell CurrentCell { get; set; }
        public int CurrentRoomMapId { get; set; }
        public long RoomVersion { get; set; }
        public long EntryRevision { get; set; } = 1;
        public long RuntimeRevision { get; set; }
        public ImmutableHashSet<MazeCell> VisitedCells { get; set; }
        public ImmutableHashSet<GroundDropId> TakenGroundDrops { get; set; } = [];
        public ImmutableHashSet<RoomActorId> DeadActors { get; set; } = [];
        public HashSet<RoomActorId> CommittedActorDeaths { get; } = [];
        public HashSet<ParticipantEffectKey> CompletedKillEffects { get; } = [];
        public HashSet<RoomActorId> AllocatedDropActors { get; } = [];
        public ImmutableArray<AllocatedMonsterDrop> MonsterDrops { get; set; } = [];
        public HashSet<CharacterId> ActiveParticipants { get; }
        public HashSet<CharacterId> ReadyParticipants { get; } = [];

        public DungeonRunParticipant Participant(CharacterId characterId) =>
            this.participantsByCharacter[characterId];

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

        public ushort NextMonsterDropNumber(MazeCell cell, int declaredDropCount)
        {
            var highest = declaredDropCount;
            foreach (var allocated in this.MonsterDrops)
            {
                if (allocated.Cell == cell && allocated.Drop.Number > highest)
                    highest = allocated.Drop.Number;
            }

            return checked((ushort)(highest + 1));
        }

        public DungeonRunState ViewFor(CharacterId characterId)
        {
            var progress = this.ProgressFor(characterId);
            return new DungeonRunState(
                this.Layout,
                this.CurrentCell,
                this.CurrentRoomMapId,
                this.VisitedCells,
                this.TakenGroundDrops,
                this.DeadActors,
                this.StartedAt,
                this.Difficulty,
                progress.IsSettled,
                this.MonsterDrops,
                progress.ClearRewardCards,
                this.RunId,
                this.RoomVersion,
                this.Participant(characterId),
                this.Participants,
                this.EntryRevision,
                this.ReadyParticipants.ToImmutableHashSet());
        }
    }

    private sealed class ParticipantProgress
    {
        public bool IsSettled { get; set; }
        public ClearRewardCardSet? ClearRewardCards { get; set; }
    }
}
