using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands.Pipeline;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World.BossTower;

internal sealed class BossTowerDomainService(
    IBossTowerRuleService rules, ISelectedSessionDomainService sessions,
    ICharacterDomainService characters, ICharacterBusinessInventoryDomainService inventory,
    IMaterialConsumptionRuleService materialRules,
    IDungeonAdmissionDomainService admission, IEventManagementDomainService activities,
    IIndependentDropRuleService independent, IItemDeliveryDomainService delivery, IQuestProgressDomainService quests,
    IDungeonRunDomainService ordinary, DomainTransactionScope scope, TransactionalExecutionGate executionGate, TimeProvider clock,
    IBossTowerRewardDomainService? rewards = null,
    IEventService? events = null, IDungeonParticipationDomainService? participation = null,
    ILogger<BossTowerDomainService>? logger = null)
    : IBossTowerDomainService
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, BossTowerRun> runs = [];
    private readonly Dictionary<CharacterId, Guid> membership = [];
    private readonly Dictionary<Guid, Changes> reservations = [];
    private bool ValidSession(SelectedSessionIdentity session) => sessions.IsInWorld(session);
    private bool InOrdinary(CharacterId id) => ordinary.Find(id) is not null;

    private Changes Pending => scope.Current!.Enlist(this, () => new Changes(this));
    private BossTowerRun? Lookup(CharacterId id)
    {
        if (scope.Current is { IsReadOnly: false }) return Pending.Find(id);
        lock (sync) return membership.TryGetValue(id, out var runId) ? runs[runId] : null;
    }
    public BossTowerSnapshot? Find(SelectedSessionIdentity session)
    {
        var run = Lookup(session.CharacterId);
        if (run is not null && scope.Current is { IsReadOnly: false })
        { Pending.AuthenticatedSessions.Add(session); Pending.Observe(run); }
        return run is not null && run.Members.Any(x => x.Session == session) ? run.Snapshot() : null;
    }
    public bool IsInTower(CharacterId characterId) => Lookup(characterId) is not null;

    private async ValueTask<T> Execute<T>(Func<ValueTask<T>> action, CancellationToken token)
    {
        if (scope.Current is not null) return await action().ConfigureAwait(false);
        await using var admission = await executionGate.AcquireAsync(token).ConfigureAwait(false);
        await using var transaction = scope.Begin();
        var result = await action().ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return result;
    }

    public bool Supports(int dungeonId) => rules.Supports(dungeonId);
    public ValueTask<BossTowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members,
        SelectedSessionIdentity leader, uint seed, CancellationToken token, byte requestMode = 0, byte difficulty = 0,
        byte practiceMode = 0) => Execute(async () =>
    {
        BossTowerEntryResult Refuse(string reason, byte? slot = null, DungeonEntryFailure? failure = null) =>
            new(reason, null, []) { BlockedSlot = slot, Failure = failure };
        if (requestMode != 0 || difficulty != 0 || practiceMode != 0 || members.Count < 1 || members.Count > rules.PartyLimit
            || !members.Any(x => x.Session == leader)
            || members.Select(x => x.Session.AccountId).Distinct().Count() != members.Count
            || members.Select(x => x.Session.CharacterId).Distinct().Count() != members.Count
            || members.Select(x => x.Session.WireUserId).Distinct().Count() != members.Count || members.Any(x => x.Slot > 3)
            || members.Select(x => x.Slot).Distinct().Count() != members.Count) return Refuse("InvalidParty");
        if (!(await activities.ListAsync(clock.GetUtcNow(), token).ConfigureAwait(false)).Entries
            .Any(x => x.EventId == rules.EventId && x.Status == "active")) return Refuse("ActivityClosed", failure: DungeonEntryFailure.OutsideOpeningHours);
        var definition = await rules.DefinitionAsync(token).ConfigureAwait(false);
        foreach (var candidate in members)
        {
            if (!ValidSession(candidate.Session)) return Refuse("NoSession", candidate.Slot);
            if (IsInTower(candidate.Session.CharacterId) || ordinary.Find(candidate.Session.CharacterId) is not null
                || participation?.Find(candidate.Session.CharacterId) is not null) return Refuse("AlreadyInDungeon", candidate.Slot);
        }
        var participants = members.Select(x => new DungeonRunParticipant(x.Session.AccountId, x.Session.CharacterId,
            x.Slot, x.Session == leader)).ToImmutableArray();
        var context = new DungeonEntryContext(definition.Admission.DungeonId, DungeonEntryFacts.None, 0, 0, participants);
        var options = new DungeonAdmissionOptions { RequireMinimumLevel = true, ConsumesFatigue = false };
        var decision = await admission.PrepareAsync(context, options, token).ConfigureAwait(false);
        if (decision.Batch is null) return Refuse(decision.Refusal?.Failure.ToString() ?? "AdmissionRefused",
            decision.Refusal?.BlockedSlot, decision.Refusal?.Failure);
        // Prepare the map before charging. Any later failure aborts the shared command transaction.
        var first = await rules.CreateRoomAsync(1, seed, token).ConfigureAwait(false);
        var run = new BossTowerRun
        {
            RunId = Guid.NewGuid(), DungeonId = definition.Admission.DungeonId, LevelCount = definition.Stages.Count,
            Seed = seed, StartedAt = clock.GetUtcNow(),
            Members = members.Select(x => new TowerMember(x.Session, x.Slot, x.Session == leader, false)).ToImmutableArray(),
            Room = null!, ReservedUids = members.Select(x => x.Session.WireUserId).ToImmutableHashSet()
        };
        run.SetRoom(first, 1, clock.GetUtcNow() + rules.LoadingTimeout);
        try
        {
            var consumed = await admission.ConsumeAsync(decision.Batch, token).ConfigureAwait(false);
            if (consumed.Receipt is null) { scope.Current!.RequireRollback(); return Refuse(consumed.Refusal?.Failure.ToString() ?? "PaymentRefused"); }
            if (admission.BeginEntry(consumed.Receipt, participants, context.DungeonId) is { } refused)
            { scope.Current!.RequireRollback(); return Refuse(refused.Failure?.ToString() ?? "EntryRefused"); }
            if (participation is not null && !participation.TryClaim(members.Select(x => x.Session.CharacterId).ToArray(),
                new(DungeonParticipationMode.BossTower, run.RunId))) throw new InvalidOperationException("Tower participation changed during admission.");
            foreach (var member in members) Pending.AuthenticatedSessions.Add(member.Session);
            Pending.Add(run);
            return new BossTowerEntryResult(null, run.Snapshot(), consumed.Receipt.InventoryChanges);
        }
        catch { scope.Current!.RequireRollback(); throw; }
    }, token);

    private BossTowerRun? Match(BossTowerActionContext context, bool leaving = false)
    {
        var run = Lookup(context.Session.CharacterId);
        if (run is null || run.RunId != context.RunId || run.Room.State.Version != context.RoomVersion
            || !run.Members.Any(x => x.Session == context.Session)
            || (!leaving && !sessions.IsInWorld(context.Session))) return null;
        if (!leaving) Pending.AuthenticatedSessions.Add(context.Session);
        return run;
    }
    private ValueTask<BossTowerActionResult> Mutate(BossTowerActionContext context,
        Func<BossTowerRun, BossTowerActionResult> action, CancellationToken token, bool leaving = false) => Execute(() =>
    {
        var current = Match(context, leaving);
        if (current is null) return ValueTask.FromResult(new BossTowerActionResult(false, null));
        var next = current.Clone();
        var result = action(next);
        if (result.Changed) Pending.Put(current, next);
        return ValueTask.FromResult(result);
    }, token);
    public ValueTask<BossTowerActionResult> LoadAsync(BossTowerActionContext context, CancellationToken token) =>
        Mutate(context, run => run.Load(context.Session), token);
    public ValueTask<BossTowerActionResult> StartAsync(BossTowerActionContext context, CancellationToken token) =>
        Mutate(context, run => run.Start(context.Session, clock.GetUtcNow()), token);
    public ValueTask<BossTowerActionResult> KillAsync(BossTowerActionContext context, ushort actorUid, CancellationToken token) =>
        RewardingMutation(context, run => run.Kill(context.Session, actorUid, clock.GetUtcNow()), token);
    public ValueTask<BossTowerPickupResult> PickupAsync(BossTowerActionContext context, ushort groundUid, CancellationToken token) => Execute(async () =>
    {
        var current = Match(context);
        BossTowerPickupResult Refuse(bool noRoom = false) => new(new(false, current?.Snapshot()), groundUid, noRoom);
        if (current is null || current.Members.Any(x => x.Session == context.Session && x.Dead)
            || current.Room.State.Phase is not (TowerRoomPhase.Fighting or TowerRoomPhase.Cleared)) return Refuse();
        var ground = current.GroundItems.FirstOrDefault(x => x.Uid == groundUid && !x.Claimed
            && (x.Rolled.OwnerCharacterId is null || x.Rolled.OwnerCharacterId == context.Session.CharacterId));
        if (ground is null) return Refuse();
        var next = current.Clone();
        var granted = await GrantDropAsync(context.Session, ground.Rolled, token).ConfigureAwait(false);
        if (!next.ClaimDrop(groundUid)) throw new InvalidOperationException("Tower ground claim changed during pickup.");
        Pending.Put(current, next);
        return new BossTowerPickupResult(next.Result(true) with { InventoryUpdates = granted.Updates, GoldBalance = granted.Gold, QuestProgress = granted.QuestProgress }, groundUid);
    }, token);
    public ValueTask<BossTowerActionResult> NextLevelAsync(BossTowerActionContext context, byte targetX, byte targetY, CancellationToken token) => Execute(async () =>
    {
        var current = Match(context);
        if (current is null)
        {
            logger?.LogWarning("Boss tower move refused character={CharacterId} run={RunId} roomVersion={RoomVersion} target=({X},{Y}) reason=SessionOrRunMismatch",
                context.Session.CharacterId.Value, context.RunId, context.RoomVersion, targetX, targetY);
            return new BossTowerActionResult(false, null);
        }
        var targetAccepted = rules.IsNextStageTarget(current.Room.State.Level, targetX, targetY);
        if (!targetAccepted || !current.CanAdvance(context.Session))
        {
            var reason = !targetAccepted ? "TargetCoordinateMismatch"
                : current.Completion is not null ? "RunCompleted"
                : current.Room.State.Phase != TowerRoomPhase.Cleared ? "RoomNotCleared" : "NotLeader";
            logger?.LogWarning("Boss tower move refused character={CharacterId} run={RunId} roomVersion={RoomVersion} stage={Stage} map={MapId} phase={Phase} target=({X},{Y}) remainingBlocking={RemainingBlocking} deadActors={DeadActors}/{ActorCount} reason={Reason}",
                context.Session.CharacterId.Value, current.RunId, current.Room.State.Version, current.Room.State.Level,
                current.Room.State.MapId, current.Room.State.Phase, targetX, targetY,
                current.Room.State.Actors.Count(x => x.Seed.BlocksClear && !x.Dead),
                current.Room.State.Actors.Count(x => x.Dead), current.Room.State.Actors.Length, reason);
            return current.Result(false);
        }
        var level = current.Room.State.Level + 1;
        var prepared = await rules.CreateRoomAsync(level, unchecked(current.Seed + (uint)level - 1), token).ConfigureAwait(false);
        var next = current.Clone();
        var result = next.NextLevel(prepared, clock.GetUtcNow() + rules.LoadingTimeout);
        Pending.Put(current, next);
        return result;
    }, token);
    public ValueTask<BossTowerActionResult> DieAsync(BossTowerActionContext context, CancellationToken token) =>
        RewardingMutation(context, run => run.Die(context.Session, clock.GetUtcNow()), token);
    public ValueTask<BossTowerActionResult> LeaveAsync(BossTowerActionContext context, bool disconnected, CancellationToken token) =>
        RewardingMutation(context, run => run.Leave(context.Session, disconnected, clock.GetUtcNow()), token, leaving: true);
    public ValueTask<BossTowerActionResult> SynchronizeLeaderAsync(BossTowerActionContext context,
        SelectedSessionIdentity leader, CancellationToken token) =>
        Mutate(context, run => run.SynchronizeLeader(leader), token);
    public ValueTask<BossTowerActionResult> SelectResultAsync(BossTowerActionContext context, byte option, CancellationToken token) => Execute(async () =>
    {
        if (option > 2 || option == 0 && !await CanRetryAsync(context, token).ConfigureAwait(false)) return new(false, Find(context.Session));
        return await Mutate(context, run => run.SelectReturn(context.Session, option, clock.GetUtcNow(),
            option == 2 ? TimeSpan.Zero : rules.ResultTimeout), token).ConfigureAwait(false);
    }, token);
    public ValueTask<BossTowerActionResult> ReleaseForSelectionAsync(BossTowerActionContext context, int requestedDungeonId, CancellationToken token) => Execute(async () =>
    {
        var current = Match(context);
        if (current?.Completion is not { Settled: true }
            || !current.Members.Any(member => member.Session == context.Session && member.IsLeader)
            || current.PendingResultChoice is not (0 or 1)
            || current.PendingResultChoice == 0 && requestedDungeonId != current.DungeonId
            || current.PendingResultChoice == 1 && requestedDungeonId != 0) return new(false, current?.Snapshot());
        var next = current.Clone();
        foreach (var member in current.Members)
        {
            if (ValidSession(member.Session)) Pending.AuthenticatedSessions.Add(member.Session);
            participation?.Release(member.Session.CharacterId, new(DungeonParticipationMode.BossTower, current.RunId));
            next.Leave(member.Session, false, clock.GetUtcNow());
        }
        Pending.Put(current, next);
        return next.Result(true);
    }, token);
    public bool CanShop(SelectedSessionIdentity session, int npcId, int shopId)
    {
        if (!ValidSession(session) || !rules.CanTrade(npcId, shopId)) return false;
        var run = Lookup(session.CharacterId);
        if (run is not null && scope.Current is { IsReadOnly: false })
        { Pending.AuthenticatedSessions.Add(session); Pending.Observe(run); }
        return run is not null && run.Members.Any(x => x.Session == session && !x.Dead)
            && run.NpcIds.Contains(npcId) && run.Completion is null
            && run.Room.State.Phase is TowerRoomPhase.Fighting or TowerRoomPhase.Cleared;
    }

    public IReadOnlyList<BossTowerActionContext> FindReturnsDue(DateTimeOffset now)
    {
        lock (sync)
        {
            return runs.Values
                .Where(run => run.ReturnDueAt is { } due && due <= now && run.Completion is { Settled: true }
                    || run.Room.State.Phase == TowerRoomPhase.Loading && run.LoadDueAt <= now && run.Completion is null)
                .Select(run => run.Members.FirstOrDefault(member => member.IsLeader) is { } leader
                    ? new BossTowerActionContext(leader.Session, run.RunId, run.Room.State.Version)
                    : null)
                .Where(context => context is not null)
                .Cast<BossTowerActionContext>()
                .ToArray();
        }
    }

    public ValueTask<bool> CanRetryAsync(BossTowerActionContext context, CancellationToken token) => Execute(async () =>
    {
        var run = Match(context);
        if (run?.Completion is not { Settled: true } || !run.Members.Any(x => x.Session == context.Session && x.IsLeader)) return false;
        if (!(await activities.ListAsync(clock.GetUtcNow(), token).ConfigureAwait(false)).Entries
            .Any(x => x.EventId == rules.EventId && x.Status == "active")) return false;
        var definition = await rules.DefinitionAsync(token).ConfigureAwait(false);
        foreach (var member in run.Members)
        {
            if (!ValidSession(member.Session)) return false;
            var progress = await characters.Get(member.Session.CharacterId).ProgressAsync(token).ConfigureAwait(false);
            if (progress is null || progress.Level < definition.Admission.MinimumRequiredLevel) return false;
            long held = 0;
            var store = inventory.GetStore(member.Session.CharacterId, BusinessInventoryStoreKind.Item);
            foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
                BusinessInventorySpace.Material, BusinessInventorySpace.Quest, BusinessInventorySpace.ExpertMaterial })
                if (store.FindContainer(space, isDefault: true) is { } container)
                    foreach (var item in await container.GetAllAsync(token).ConfigureAwait(false))
                        if (materialRules.IsConsumable(item, rules.TicketItemId)) held += item.Count;
            if (held < 1) return false;
        }
        return true;
    }, token);

    private async ValueTask<(ImmutableArray<TowerInventoryUpdate> Updates, int? Gold, QuestProgressSnapshot? QuestProgress)> GrantDropAsync(
        SelectedSessionIdentity session, RolledDrop drop, CancellationToken token)
    {
        ImmutableArray<TowerInventoryUpdate> updates = [];
        int? gold = null;
        if (drop.IsGold) gold = await characters.Get(session.CharacterId).AddGoldAsync(drop.Count, token).ConfigureAwait(false);
        else
        {
            var receipt = await delivery.DeliverAsync(session.AccountId, session.CharacterId,
                [new(drop.ItemId, drop.Count) { Durability = drop.Durability }], "领主之塔掉落", token, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
            updates = receipt.Inventory.Select(x => new TowerInventoryUpdate(x.Slot, x.Snapshot)).ToImmutableArray();
        }
        var report = await quests.ReportAsync(session.AccountId, session.CharacterId, [new QuestHoldingsChanged()], token).ConfigureAwait(false);
        return (updates, gold, report.NotificationSnapshot);
    }

    private ValueTask<BossTowerActionResult> RewardingMutation(BossTowerActionContext context,
        Func<BossTowerRun, BossTowerActionResult> action, CancellationToken token, bool leaving = false) => Execute(async () =>
    {
        var current = Match(context, leaving);
        if (current is null) return new BossTowerActionResult(false, null);
        var next = current.Clone();
        var result = action(next);
        if (!result.Changed) return result;
        if (current.Room.State.Phase != TowerRoomPhase.Cleared && next.Room.State.Phase == TowerRoomPhase.Cleared)
        {
            var participants = next.Members.Where(x => ValidSession(x.Session)).ToArray();
            foreach (var member in participants) Pending.AuthenticatedSessions.Add(member.Session);
            await (events ?? throw new InvalidOperationException("Map-clear events are unavailable."))
                .EnqueueAsync(new DungeonMapCleared(new(next.RunId), next.DungeonId, DungeonDifficulty.Normal,
                    new(checked((byte)(next.Room.State.Level % 2)), 0), next.Room.State.MapId, next.Room.State.Version,
                    next.Room.State.ClearedAt!.Value, [.. participants.Select(member =>
                        new DungeonRunParticipant(member.Session.AccountId, member.Session.CharacterId, member.Slot, member.IsLeader))]),
                    token).ConfigureAwait(false);
        }
        var wasSettled = current.Completion is { Settled: true };
        var killRewards = ImmutableArray<TowerKillReward>.Empty;
        if (rewards is not null)
        {
            // Stale identities must not block cleanup, nor receive another session's awards.
            var eligible = next.Snapshot() with { Members = next.Members.Where(x => ValidSession(x.Session)).ToImmutableArray() };
            foreach (var member in eligible.Members) Pending.AuthenticatedSessions.Add(member.Session);
            if (result.KilledActor is { } actor)
                killRewards = await rewards.KillAsync(eligible, actor, token).ConfigureAwait(false);

        }
        var produced = ImmutableArray<BossTowerGroundItem>.Empty;
        var autoUpdates = ImmutableArray.CreateBuilder<TowerInventoryUpdate>();
        if (result.KilledActor is { } killed)
        {
            var leader = next.Members.FirstOrDefault(x => x.IsLeader && ValidSession(x.Session));
            if (leader is not null)
            {
                var dropContext = new MonsterDropContext(new(next.RunId), next.DungeonId, unchecked((int)next.Seed),
                    new(checked((byte)(next.Room.State.Level % 2)), 0), next.Room.State.MapId, DungeonDifficulty.Normal,
                    killed.Uid, killed.Seed.Level, killed.Seed.ActorType, leader.Session.CharacterId,
                    next.Members.Select(x => new MonsterDropParticipant(x.Session.AccountId, x.Session.CharacterId)).ToImmutableArray())
                    { SourceType = killed.Seed.ActorType is >= 5 and <= 8 ? IndependentDropSourceType.APC : IndependentDropSourceType.Monster };
                var drops = await independent.DrawAsync(new(killed.Seed.TemplateId, dropContext), token, growthBonusPercent: 0).ConfigureAwait(false);
                var definition = await rules.DefinitionAsync(token).ConfigureAwait(false);
                foreach (var drop in drops)
                    if (definition.AutoRootItemIds.Contains(drop.ItemId))
                    {
                        var recipient = drop.OwnerCharacterId is { } owner ? next.Members.FirstOrDefault(x => x.Session.CharacterId == owner) : leader;
                        if (recipient is null || !ValidSession(recipient.Session)) continue;
                        Pending.AuthenticatedSessions.Add(recipient.Session);
                        var granted = await GrantDropAsync(recipient.Session, drop, token).ConfigureAwait(false);
                        var rewardIndex = -1;
                        for (var i = 0; i < killRewards.Length; i++) if (killRewards[i].Session == recipient.Session) { rewardIndex = i; break; }
                        if (rewardIndex >= 0)
                            killRewards = killRewards.SetItem(rewardIndex, killRewards[rewardIndex] with
                            { InventoryUpdates = killRewards[rewardIndex].InventoryUpdates.AddRange(granted.Updates), QuestProgress = granted.QuestProgress });
                    }
                    else next.AddDrops(killed.Uid, [drop]);
                produced = next.GroundItems.Where(x => x.ActorUid == killed.Uid && !x.Claimed).ToImmutableArray();
            }
        }
        if (rewards is not null && next.Completion is { Settled: false, Reason: TowerEndReason.Completed or TowerEndReason.Defeated })
        {
            var eligible = next.Snapshot() with { Members = next.Members.Where(x => ValidSession(x.Session)).ToImmutableArray() };
            next.Settle(await rewards.SettleAsync(eligible, next.Seed, token).ConfigureAwait(false), clock.GetUtcNow(), rules.ResultTimeout);
        }
        foreach (var departed in current.Members.Where(member => !next.Members.Any(x => x.Session == member.Session)))
            participation?.Release(departed.Session.CharacterId, new(DungeonParticipationMode.BossTower, current.RunId));
        Pending.Put(current, next);
        return next.Result(true, result.KilledActor) with
        {
            KillRewards = killRewards,
            Drops = produced,
            InventoryUpdates = autoUpdates.ToImmutable(),
            SettlementCreated = !wasSettled && next.Completion is { Settled: true },
        };
    }, token);

    private sealed class Changes(BossTowerDomainService owner) : ITransactionParticipant
    {
        private readonly Dictionary<Guid, (BossTowerRun? Before, BossTowerRun After)> changes = [];
        private Dictionary<Guid, BossTowerRun?> ObservedRuns { get; } = [];
        private IEnumerable<Guid> ReservedKeys => changes.Keys.Union(ObservedRuns.Keys);
        public void Observe(BossTowerRun run) => ObservedRuns.TryAdd(run.RunId,
            changes.TryGetValue(run.RunId, out var own) ? own.Before : run);
        public HashSet<SelectedSessionIdentity> AuthenticatedSessions { get; } = [];
        public bool CommitCanFail => false;
        public BossTowerRun? Find(CharacterId id)
        {
            var staged = changes.Values.FirstOrDefault(x => x.After.Members.Any(m => m.Session.CharacterId == id)).After;
            if (staged is not null) return staged;
            lock (owner.sync)
            {
                if (!owner.membership.TryGetValue(id, out var runId)) return null;
                return changes.ContainsKey(runId) ? null : owner.runs[runId];
            }
        }
        public void Add(BossTowerRun next) => changes.Add(next.RunId, (null, next));
        public void Put(BossTowerRun previous, BossTowerRun next) => changes[next.RunId] =
            (changes.TryGetValue(next.RunId, out var existing) ? existing.Before : previous, next);
        public ValueTask PrepareAsync(CancellationToken token)
        {
            lock (owner.sync)
            {
                if (ObservedRuns.Any(x => owner.runs.GetValueOrDefault(x.Key) != x.Value
                    || owner.reservations.TryGetValue(x.Key, out var reserved) && !ReferenceEquals(reserved, this)))
                    throw new InvalidOperationException("Boss tower shop room changed before commit.");
                if (AuthenticatedSessions.Any(x => !owner.ValidSession(x)))
                    throw new InvalidOperationException("Tower session changed before commit.");
                foreach (var (runId, (before, after)) in changes)
                {
                    if (owner.runs.GetValueOrDefault(runId) != before
                        || owner.reservations.TryGetValue(runId, out var reserved) && !ReferenceEquals(reserved, this)) throw new InvalidOperationException("Tower changed before commit.");
                    foreach (var member in after.Members)
                    {
                        if (owner.membership.TryGetValue(member.Session.CharacterId, out var owned) && owned != runId
                            && (!changes.TryGetValue(owned, out var releasing) || releasing.After.Members.Any(x => x.Session.CharacterId == member.Session.CharacterId)))
                            throw new InvalidOperationException("Tower member changed before commit.");
                        if (before is null && owner.InOrdinary(member.Session.CharacterId))
                            throw new InvalidOperationException("Tower admission/session changed before commit.");
                    }
                }
                foreach (var id in ReservedKeys) owner.reservations[id] = this;
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken token)
        {
            lock (owner.sync)
            {
                foreach (var id in ReservedKeys)
                    if (owner.reservations.GetValueOrDefault(id) == this) owner.reservations.Remove(id);
                foreach (var (before, _) in changes.Values)
                    if (before is not null)
                        foreach (var member in before.Members) owner.membership.Remove(member.Session.CharacterId);
                foreach (var (runId, (_, after)) in changes)
                {
                    if (after.Members.IsEmpty) owner.runs.Remove(runId);
                    else
                    {
                        owner.runs[runId] = after;
                        foreach (var member in after.Members) owner.membership[member.Session.CharacterId] = runId;
                    }
                }
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken token)
        {
            lock (owner.sync) foreach (var id in ReservedKeys)
                if (owner.reservations.GetValueOrDefault(id) == this) owner.reservations.Remove(id);
            return ValueTask.CompletedTask;
        }
    }
}
