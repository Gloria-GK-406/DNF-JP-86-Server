using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunStore
{
    internal sealed partial class CombatService
    {
        public bool IsActorDeathClaimCurrent(DungeonActorDeathClaim claim)
        {
            lock (store.gate) return store.ClaimsOf(claim.RunId)?.Deaths.GetValueOrDefault(claim.Actor) == claim.Token
                && store.runsById.TryGetValue(claim.RunId, out var run) && run.IsMember(claim.CharacterId)
                && run.RoomVersion == claim.Run.RoomVersion && run.CurrentCell == claim.Actor.Cell;
        }

        public bool IsParticipantKillEffectClaimCurrent(DungeonParticipantKillEffectClaim claim)
        {
            lock (store.gate) return IsActorDeathClaimCurrent(claim.Death)
                && store.ClaimsOf(claim.Death.RunId)?.KillEffects.GetValueOrDefault(
                    new ParticipantEffectKey(claim.Death.Actor, claim.CharacterId)) == claim.Token
                && store.runsById.TryGetValue(claim.Death.RunId, out var run) && run.IsMember(claim.CharacterId);
        }

        public DungeonActorDeathClaim? TryClaimActorDeath(
            CharacterId characterId,
            ushort actorSequence)
        {
            GameplayWriteAssertion.Writing(nameof(CombatService) + "." + nameof(TryClaimActorDeath));
            lock (this.store.gate)
            {
                if (this.store.RunOf(characterId) is not { } aggregate)
                    return null;

                if (aggregate.LostLandExploration?.HasExpired(this.clock.GetUtcNow()) == true) return null;
                var actor = new RoomActorId(aggregate.CurrentCell, actorSequence);
                var cell = aggregate.Cells[aggregate.CurrentCell];
                if (!cell.CanClaimDeath(actorSequence))
                    return null;

                if (aggregate.Claims.Deaths.ContainsKey(actor))
                    return null;

                var token = this.store.NextClaim();
                aggregate.Claims.Deaths.Add(actor, token);
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
            GameplayWriteAssertion.Writing(nameof(CombatService) + "." + nameof(CommitActorDeath));
            return this.CommitActorDeathChange(claim)?.Run;
        }

        public bool WillClearMap(DungeonActorDeathClaim claim)
        {
            lock (this.store.gate)
            {
                if (!this.IsActorDeathClaimCurrent(claim)) return false;
                var aggregate = this.store.runsById[claim.RunId];
                var cell = aggregate.Cells[claim.Actor.Cell];
                if (cell.IsCleared) return false;
                var suppressed = this.SuppressedAfterDeath(aggregate, cell, claim);
                return cell.WillClearAfterDeath(claim.Actor.Sequence, suppressed);
            }
        }

        private IReadOnlyList<ushort> SuppressedAfterDeath(DungeonRunAggregate aggregate, DungeonCellAggregate cell, DungeonActorDeathClaim claim) =>
            !cell.MechanismBlocksClear && aggregate.Layout.Mode is not DungeonRunMode.Altar
            && cell.FindActor(claim.Actor.Sequence) is { BlocksClear: true, Actor.IsBoss: true }
            && aggregate.Layout.KindOf(claim.Actor.Cell) == DungeonRoomKind.Boss
            && !cell.HasLivingBossExcept(claim.Actor.Sequence)
                ? cell.SuppressibleEnemies(sequence => !aggregate.Claims.Deaths.ContainsKey(
                    new RoomActorId(claim.Actor.Cell, sequence))) : [];

        public DungeonActorDeathChange? CommitActorDeathChange(DungeonActorDeathClaim claim)
        {
            GameplayWriteAssertion.Writing(nameof(CombatService) + "." + nameof(CommitActorDeathChange));
            ArgumentNullException.ThrowIfNull(claim);
            lock (this.store.gate)
            {
                if (!this.TryRemoveClaim(claim.RunId, claim.Actor, claim.Token)
                    || !this.store.runsById.TryGetValue(claim.RunId, out var aggregate)
                    || !aggregate.IsMember(claim.CharacterId))
                {
                    return null;
                }

                var cell = aggregate.Cells[claim.Actor.Cell];
                var killedActor = cell.FindActor(claim.Actor.Sequence);
                var previousFever = aggregate.LostLandFever;
                var autoKilled = this.SuppressedAfterDeath(aggregate, cell, claim);
                IReadOnlyList<RolledDrop>? resolvedLoot = null;
                if (aggregate.Layout.Mode is DungeonRunMode.GiantDragon { TimeReward: { } timeReward }
                    && killedActor is { IsMonster: true, BlocksClear: true, Actor.IsBoss: true }
                    && aggregate.Layout.KindOf(claim.Actor.Cell) == DungeonRoomKind.Boss
                    && !cell.HasLivingBossExcept(claim.Actor.Sequence))
                {
                    var elapsed = (claim.OccurredAt ?? this.clock.GetUtcNow()) - aggregate.StartedAt;
                    resolvedLoot = this.giantDragons
                        .ResolveTimeReward(timeReward, elapsed, killedActor.PendingLoot);
                    this.logger.LogInformation("Giant dragon time reward run={RunId} dungeon={DungeonId} actor={Actor} elapsedMs={ElapsedMs} item={ItemId} sharedCount={Count}",
                        aggregate.RunId, aggregate.Layout.DungeonId, claim.Actor.Sequence, elapsed.TotalMilliseconds,
                        timeReward.Material.ItemId, resolvedLoot.Where(x => x.ItemId == timeReward.Material.ItemId
                            && x.OwnerCharacterId is null && x.Delivery == RolledDropDelivery.Ground).Sum(x => x.Count));
                }
                // A committed kill is the only once-per-run fact. Freeze rolls in the cell, then
                // select the shared boss bonus here so pre-created alternative rooms cannot both pay.
                var bonus = aggregate.Layout.Mode is DungeonRunMode.LostLand && killedActor?.LostLandBonus is { } frozen
                    && !aggregate.KilledMonsterCodes.Any(frozen.SharedBossCodes.Contains)
                        ? frozen.Items : [];
                var added = cell.CommitActorDeath(
                    claim.Actor.Sequence,
                    rolled => rolled.OwnerCharacterId is not { } owner
                        || aggregate.IsMember(owner), resolvedLoot, bonus);
                if (added is null)
                    return null;
                if (killedActor is { IsMonster: true })
                {
                    aggregate.RecordKill(new DungeonKillRecord(
                        claim.Actor,
                        killedActor.Actor.Code,
                        aggregate.Layout.LevelOf(killedActor.Actor.Level),
                        (MonsterRank)killedActor.Actor.ActorType));
                    if (aggregate.Layout.Mode is DungeonRunMode.LostLand { Plan: var exploration } && previousFever is not null)
                        aggregate.LostLandFever = this.lostLandFever.Award(previousFever, killedActor,
                            claim.OccurredAt ?? this.clock.GetUtcNow(), exploration.FeverDuration);
                    if (aggregate.LostLandFever?.ActiveUntil != previousFever?.ActiveUntil) this.deadlines.Wake(DungeonDeadlineKind.LostLandFever);
                }
                cell.SuppressEnemies(autoKilled);
                if (cell.IsCleared)
                    cell.Mechanisms = cell.Mechanisms.Select(state => this.mechanismRules.Complete(state,
                        claim.OccurredAt ?? this.clock.GetUtcNow())).ToArray();
                this.deadlines.Wake(DungeonDeadlineKind.RoomMechanism);
                // Altar rounds and guardian waves read the living actors when their deadline is evaluated.
                if (aggregate.Layout.Mode is DungeonRunMode.Altar) this.deadlines.Wake(DungeonDeadlineKind.Altar);
                else if (aggregate.Layout.Mode is DungeonRunMode.Guardian) this.deadlines.Wake(DungeonDeadlineKind.GuardianAltar);
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
                    LostLandFeverPercent = aggregate.LostLandFever != previousFever
                        ? aggregate.LostLandFever?.Percent : null,
                };
            }
        }

        public bool ReleaseActorDeath(DungeonActorDeathClaim claim)
        {
            GameplayWriteAssertion.Writing(nameof(CombatService) + "." + nameof(ReleaseActorDeath));
            ArgumentNullException.ThrowIfNull(claim);
            lock (this.store.gate)
            {
                if (!this.TryRemoveClaim(claim.RunId, claim.Actor, claim.Token)
                    || !this.store.runsById.TryGetValue(claim.RunId, out var aggregate))
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
            GameplayWriteAssertion.Writing(nameof(CombatService) + "." + nameof(TryClaimParticipantKillEffect));
            ArgumentNullException.ThrowIfNull(death);
            lock (this.store.gate)
            {
                if (!this.store.runsById.TryGetValue(death.RunId, out var aggregate)
                    || !aggregate.Claims.Deaths.TryGetValue(death.Actor, out var deathToken)
                    || deathToken != death.Token
                    || !aggregate.Participants.Any(
                        participant => participant.CharacterId == characterId)
                    || !aggregate.IsMember(characterId))
                {
                    return null;
                }

                var effect = new ParticipantEffectKey(death.Actor, characterId);
                if (aggregate.CompletedKillEffects.Contains(effect))
                    return null;

                if (aggregate.Claims.KillEffects.ContainsKey(effect))
                    return null;

                var token = this.store.NextClaim();
                aggregate.Claims.KillEffects.Add(effect, token);
                return new DungeonParticipantKillEffectClaim(death, characterId, token);
            }
        }

        public bool CommitParticipantKillEffect(DungeonParticipantKillEffectClaim claim)
        {
            GameplayWriteAssertion.Writing(nameof(CombatService) + "." + nameof(CommitParticipantKillEffect));
            return this.CommitParticipantKillEffect(claim, null);
        }

        public bool CommitParticipantKillEffect(DungeonParticipantKillEffectClaim claim, ExperienceAward? award)
        {
            GameplayWriteAssertion.Writing(nameof(CombatService) + "." + nameof(CommitParticipantKillEffect));
            ArgumentNullException.ThrowIfNull(claim);
            lock (this.store.gate)
            {
                var effect = new ParticipantEffectKey(claim.Death.Actor, claim.CharacterId);
                if (!this.TryRemoveParticipantEffectClaim(claim.Death.RunId, effect, claim.Token)
                    || !this.store.runsById.TryGetValue(claim.Death.RunId, out var aggregate))
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
            GameplayWriteAssertion.Writing(nameof(CombatService) + "." + nameof(ReleaseParticipantKillEffect));
            ArgumentNullException.ThrowIfNull(claim);
            lock (this.store.gate)
            {
                var effect = new ParticipantEffectKey(claim.Death.Actor, claim.CharacterId);
                return this.TryRemoveParticipantEffectClaim(claim.Death.RunId, effect, claim.Token);
            }
        }

        private bool TryRemoveClaim(DungeonRunId runId, RoomActorId actor, long token) =>
            this.store.ClaimsOf(runId) is { } claims
            && claims.Deaths.TryGetValue(actor, out var current)
            && current == token
            && claims.Deaths.Remove(actor);

        private bool TryRemoveParticipantEffectClaim(DungeonRunId runId, ParticipantEffectKey effect, long token) =>
            this.store.ClaimsOf(runId) is { } claims
            && claims.KillEffects.TryGetValue(effect, out var current)
            && current == token
            && claims.KillEffects.Remove(effect);
        public DungeonObjectDestructionClaim? ClaimObjectDestruction(
            CharacterId characterId, DungeonRunId runId, long roomVersion, int objectCode)
        {
            GameplayWriteAssertion.Writing(nameof(CombatService) + "." + nameof(ClaimObjectDestruction));
            var transaction = this.transactions.Current
                ?? throw new InvalidOperationException("Object rewards require an ambient transaction.");
            lock (this.store.gate)
            {
                if (objectCode <= 0 || this.store.RunOf(characterId) is not { } run
                    || run.RunId != runId || run.RoomVersion != roomVersion)
                    return null;
                if (run.LostLandExploration?.HasExpired(this.clock.GetUtcNow()) == true) return null;
                var cell = run.Cells[run.CurrentCell];
                if (!this.sealedTreasureRules.AllowsObjectDestruction(run.Layout.DungeonId, run.KilledMonsterCodes, objectCode))
                    return null;
                if (!cell.ObjectQueues.TryGetValue(objectCode, out var queue) || queue.Count == 0
                    || cell.ObjectReservations.ContainsKey(objectCode))
                    return null;
                var index = queue.Peek();
                var token = this.store.NextClaim();
                transaction.Enlist((this, runId, cell.Cell, objectCode, token),
                    () => new ObjectRewardParticipant(this, run, cell, characterId,
                        roomVersion, objectCode, index, token));
                cell.ObjectReservations.Add(objectCode, token);
                return new(objectCode, index,
                    cell.ObjectAt(index).PendingLoot.ToArray());
            }
        }

        private sealed class ObjectRewardParticipant(
            CombatService owner, DungeonRunAggregate run, DungeonCellAggregate cell,
            CharacterId characterId, long roomVersion, int code, int index, long token)
            : ITransactionParticipant
        {
            public bool CommitCanFail => false;
            public ValueTask PrepareAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (owner.store.gate)
                {
                    if (!owner.store.runsById.TryGetValue(run.RunId, out var active)
                        || !ReferenceEquals(active, run) || !run.IsMember(characterId)
                        || run.CurrentCell != cell.Cell || run.RoomVersion != roomVersion
                        || cell.ObjectReservations.GetValueOrDefault(code) != token
                        || cell.ObjectQueues[code].Peek() != index)
                        throw new InvalidOperationException("The object reward reservation is no longer current.");
                }
                return ValueTask.CompletedTask;
            }

            public ValueTask CommitAsync(CancellationToken cancellationToken)
            {
                lock (owner.store.gate)
                {
                    // The captured Cell remains the target even if its run left the live directory
                    // after prepare. No fallible work follows the durable inventory commit.
                    cell.ConsumeObject(index);
                    cell.ObjectQueues[code].Dequeue();
                    cell.ObjectReservations.Remove(code);
                    run.RuntimeRevision++;
                }
                return ValueTask.CompletedTask;
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken)
            {
                lock (owner.store.gate)
                {
                    if (cell.ObjectReservations.GetValueOrDefault(code) == token)
                        cell.ObjectReservations.Remove(code);
                }
                return ValueTask.CompletedTask;
            }
        }
        public bool StageDynamicCell(DungeonRunId runId, long roomVersion, long cellRevision,
            IReadOnlyList<DungeonCellActorState> actors, bool mechanismBlocksClear)
        {
            GameplayWriteAssertion.Writing(nameof(CombatService) + "." + nameof(StageDynamicCell));
            var transaction = this.transactions.Current
                ?? throw new InvalidOperationException("Dynamic cells require a transaction.");
            if (transaction.IsReadOnly) throw new InvalidOperationException("Dynamic cells require a writable transaction.");
            lock (this.store.gate)
            {
                if (!this.store.runsById.TryGetValue(runId, out var run) || run.RoomVersion != roomVersion) return false;
                var cell = run.Cells[run.CurrentCell];
                if (cell.Revision != cellRevision || actors.Select(x => x.Sequence).Distinct().Count() != actors.Count
                    || actors.Any(x => x.Sequence == 0 || cell.FindActor(x.Sequence) is not null || x.IsDead)) return false;
                var staged = transaction.Enlist((this, runId, roomVersion, "dynamic-cell"),
                    () => new DynamicCellCommit(this, run, cell, roomVersion, cellRevision, actors.ToArray(), mechanismBlocksClear));
                return staged.Matches(actors, mechanismBlocksClear, cellRevision);
            }
        }

        private sealed class DynamicCellCommit(CombatService owner, DungeonRunAggregate run,
            DungeonCellAggregate cell, long roomVersion, long revision, IReadOnlyList<DungeonCellActorState> actors,
            bool blocksClear) : ITransactionParticipant
        {
            public bool CommitCanFail => false;
            public bool Matches(IReadOnlyList<DungeonCellActorState> batch, bool blocked, long expectedRevision) =>
                blocked == blocksClear && expectedRevision == revision && actors.SequenceEqual(batch);
            public ValueTask PrepareAsync(CancellationToken ct)
            {
                lock (owner.store.gate)
                    if (!owner.store.runsById.TryGetValue(run.RunId, out var current) || current != run
                        || run.RoomVersion != roomVersion || run.Cells[run.CurrentCell] != cell || cell.Revision != revision)
                        throw new InvalidOperationException("Dynamic actor cell changed before commit.");
                return ValueTask.CompletedTask;
            }
            public ValueTask CommitAsync(CancellationToken ct)
            {
                lock (owner.store.gate)
                {
                    cell.AddActors(actors);
                    cell.MechanismBlocksClear = blocksClear;
                    run.RuntimeRevision++;
                    if (!blocksClear && cell.IsCleared && run.Layout.Mode is DungeonRunMode.Altar && run.CurrentCell == run.Layout.BossCell)
                        owner.store.TryOpenClear(run.Participants.First().CharacterId, run.RunId, roomVersion, owner.clock.GetUtcNow());
                }
                return ValueTask.CompletedTask;
            }
            public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        }
    }
}
