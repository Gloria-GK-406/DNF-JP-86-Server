using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World.IllusionTower;

internal sealed class IllusionTowerDomainService(
    IIllusionTowerRuleService rules, ISelectedSessionDomainService sessions,
    ICharacterDomainService characters, ICharacterBusinessInventoryDomainService inventory,
    IMaterialConsumptionDomainService materials, IMaterialConsumptionRuleService materialRules,
    IDungeonRunQueryDomainService ordinary, DomainTransactionScope scope, TimeProvider clock,
    IIllusionTowerRewardDomainService rewards, TowerInventoryDomainService towerInventory,
    IEventService events, IDungeonParticipationDomainService participation)
    : IIllusionTowerDomainService
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, IllusionTowerDgn> runs = [];
    private readonly Dictionary<CharacterId, Guid> membership = [];
    private bool ValidSession(SelectedSessionIdentity session) => sessions.IsInWorld(session);
    private bool InOrdinary(CharacterId id) => ordinary.Find(id) is not null;

    private Changes Pending => scope.Current!.Enlist(this, () => new Changes(this));
    private IllusionTowerDgn? Lookup(CharacterId id)
    {
        if (scope.Current is not null) return Pending.Find(id);
        lock (sync) return membership.TryGetValue(id, out var runId) ? runs[runId] : null;
    }
    public IllusionTowerSnapshot? Find(SelectedSessionIdentity session)
    {
        var run = Lookup(session.CharacterId);
        return run is not null && run.Members.Any(x => x.Session == session) ? run.Snapshot() : null;
    }
    public bool IsInTower(CharacterId characterId) => Lookup(characterId) is not null;

    // The caller's unit (or the session cleanup's own step) settles this; it is never opened here.
    private ValueTask<T> Execute<T>(Func<ValueTask<T>> action, CancellationToken token)
    {
        if (scope.Current is null) throw new InvalidOperationException("Illusion tower changes require the caller's transaction.");
        return action();
    }

    public bool TryReserveOrdinaryEntry(IReadOnlyList<CharacterId> members)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(TryReserveOrdinaryEntry));
        if (members.Any(x => IsInTower(x) || participation.Find(x) is { Mode: not DungeonParticipationMode.Ordinary })) return false;
        if (scope.Current is not null) Pending.OrdinaryEntries.UnionWith(members);
        return true;
    }

    public ValueTask<TowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members,
        SelectedSessionIdentity leader, uint seed, CancellationToken token, int dungeonId)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(EnterAsync));
        return Execute(async () =>
    {
        TowerEntryResult Refuse(string reason, byte? slot = null) => new(reason, null, []) { BlockedSlot = slot };
        if (members.Count is < 1 or > 4 || !members.Any(x => x.Session == leader)
            || members.Select(x => x.Session.AccountId).Distinct().Count() != members.Count
            || members.Select(x => x.Session.CharacterId).Distinct().Count() != members.Count
            || members.Select(x => x.Session.WireUserId).Distinct().Count() != members.Count || members.Any(x => x.Slot > 3)
            || members.Select(x => x.Slot).Distinct().Count() != members.Count) return Refuse("InvalidParty");
        var definition = await rules.GetDefinitionAsync(token, dungeonId).ConfigureAwait(false);
        var plans = new List<(SelectedSessionIdentity Session, List<MaterialCost> Costs)>();
        foreach (var candidate in members)
        {
            var member = candidate.Session;
            if (!sessions.IsInWorld(member)) return Refuse("NoSession");
            if (participation.Find(member.CharacterId) is not null || IsInTower(member.CharacterId) || ordinary.Find(member.CharacterId) is not null || Pending.OrdinaryEntries.Contains(member.CharacterId)) return Refuse("AlreadyInDungeon");
            var progress = await characters.Get(member.CharacterId).ProgressAsync(token).ConfigureAwait(false);
            if (progress is null || progress.Level < definition.MinimumLevel) return Refuse("LevelTooLow", candidate.Slot);
            var costs = new List<MaterialCost>();
            var store = inventory.GetStore(member.CharacterId, BusinessInventoryStoreKind.Item);
            foreach (var requirement in definition.RequiredItems)
            {
                var remaining = requirement.Count;
                foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
                    BusinessInventorySpace.Material, BusinessInventorySpace.Quest, BusinessInventorySpace.ExpertMaterial })
                {
                    if (store.FindContainer(space) is not { } container) continue;
                    foreach (var item in await container.GetAllAsync(token).ConfigureAwait(false))
                    {
                        if (!materialRules.IsConsumable(item, requirement.ItemId)) continue;
                        var count = Math.Min(remaining, item.Count);
                        if (count > 0 && requirement.Consume) costs.Add(new(new(space, item.SlotIndex), requirement.ItemId, count));
                        remaining -= count;
                        if (remaining == 0) break;
                    }
                    if (remaining == 0) break;
                }
                if (remaining != 0) return Refuse("InsufficientMaterial", candidate.Slot);
            }
            plans.Add((member, costs));
        }
        var first = await rules.CreateRoomAsync(1, seed, token, dungeonId).ConfigureAwait(false);
        var run = new IllusionTowerDgn
        {
            RunId = Guid.NewGuid(), DungeonId = definition.DungeonId, UsesTowerInventory = definition.UsesTowerInventory, LevelCount = definition.Rooms.Count,
            Seed = seed, StartedAt = clock.GetUtcNow(),
            Members = members.Select(x => new TowerMember(x.Session, x.Slot, x.Session == leader, false)).ToImmutableArray(),
            Room = null!, ReservedUids = members.Select(x => x.Session.WireUserId).ToImmutableHashSet()
        };
        run.SetRoom(first, 1);
        if (!participation.TryClaim(members.Select(x => x.Session.CharacterId).ToArray(),
            new(DungeonParticipationMode.IllusionTower, run.RunId))) return Refuse("AlreadyInDungeon");
        var debits = new List<TowerMemberDebit>();
        foreach (var (session, costs) in plans)
        {
            if (costs.Count == 0) continue;
            var consumed = await materials.ConsumeAsync(session, costs, token).ConfigureAwait(false);
            // A refusal after another member's debit is an aborted use case, never a successful no-op.
            if (!consumed.Success) throw new InvalidOperationException($"Tower material plan became invalid: {consumed.Rejection}.");
            debits.Add(new(session, consumed.Debits));
        }
        foreach (var member in members)
        {
            if (run.UsesTowerInventory)
                towerInventory
                    .Enter(member.Session.CharacterId, run.RunId);
            Pending.AuthenticatedSessions.Add(member.Session);
        }
        Pending.Add(run);
        return new TowerEntryResult(null, run.Snapshot(), debits);
    }, token);
    }

    private IllusionTowerDgn? Match(TowerActionContext context, bool leaving = false)
    {
        var run = Lookup(context.Session.CharacterId);
        if (run is null || run.RunId != context.RunId || run.Room.State.Version != context.RoomVersion
            || !run.Members.Any(x => x.Session == context.Session)
            || (!leaving && !sessions.IsInWorld(context.Session))) return null;
        if (!leaving && scope.Current is { IsReadOnly: false }) Pending.AuthenticatedSessions.Add(context.Session);
        return run;
    }
    private ValueTask<TowerActionResult> Mutate(TowerActionContext context,
        Func<IllusionTowerDgn, TowerActionResult> action, CancellationToken token, bool leaving = false) => Execute(() =>
    {
        var current = Match(context, leaving);
        if (current is null) return ValueTask.FromResult(new TowerActionResult(false, null));
        var next = current.Clone();
        var result = action(next);
        if (result.Changed) Pending.Put(current, next);
        return ValueTask.FromResult(result);
    }, token);
    public ValueTask<TowerActionResult> LoadAsync(TowerActionContext context, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(LoadAsync));
        return Mutate(context, run => run.Load(context.Session), token);
    }
    public ValueTask<TowerActionResult> StartAsync(TowerActionContext context, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(StartAsync));
        return Mutate(context, run => run.Start(context.Session, clock.GetUtcNow()), token);
    }
    public ValueTask<TowerActionResult> KillAsync(TowerActionContext context, ushort actorUid, IEffectFootprintRecorder footprint, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(KillAsync));
        return RewardingMutation(context, run => run.Kill(context.Session, actorUid, clock.GetUtcNow()), footprint, token);
    }
    public ValueTask<TowerPickupResult> PickupAsync(TowerActionContext context, ushort groundUid, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(PickupAsync));
        return Execute(async () =>
    {
        var current = Match(context);
        TowerPickupResult Refuse(bool noRoom = false) => new(new(false, current?.Snapshot()), groundUid, null, noRoom);
        if (current is null || !current.UsesTowerInventory || current.Members.Any(x => x.Session == context.Session && x.Dead)
            || current.Room.State.Phase is not (TowerRoomPhase.Fighting or TowerRoomPhase.Cleared)) return Refuse();
        var ground = current.Room.State.Items.SingleOrDefault(x => x.Uid == groundUid && x.Available && !x.Claimed);
        if (ground is null) return Refuse();
        var update = await towerInventory
            .AddAsync(context.Session, ground.ItemId, token).ConfigureAwait(false);
        if (update is null) return Refuse(noRoom: true);
        var next = current.Clone();
        if (!next.Room.Claim(groundUid)) throw new InvalidOperationException("Tower ground claim changed during pickup.");
        Pending.Put(current, next);
        return new TowerPickupResult(next.Result(true), groundUid, update);
    }, token);
    }
    public ValueTask<TowerActionResult> NextLevelAsync(TowerActionContext context, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(NextLevelAsync));
        return Execute(async () =>
    {
        var current = Match(context);
        if (current is null) return new TowerActionResult(false, null);
        if (!current.CanAdvance(context.Session)) return current.Result(false);
        var level = current.Room.State.Level + 1;
        var prepared = await rules.CreateRoomAsync(level, unchecked(current.Seed + (uint)level - 1), token, current.DungeonId).ConfigureAwait(false);
        var next = current.Clone();
        var result = next.NextLevel(prepared);
        Pending.Put(current, next);
        return result;
    }, token);
    }
    public ValueTask<TowerActionResult> DieAsync(TowerActionContext context, IEffectFootprintRecorder footprint, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(DieAsync));
        return RewardingMutation(context, run => run.Die(context.Session, clock.GetUtcNow()), footprint, token);
    }
    public ValueTask<TowerActionResult> LeaveAsync(TowerActionContext context, bool disconnected, IEffectFootprintRecorder footprint, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(LeaveAsync));
        return RewardingMutation(context, run => run.Leave(context.Session, disconnected, clock.GetUtcNow()), footprint, token, leaving: true);
    }
    public ValueTask<TowerActionResult> SynchronizeLeaderAsync(TowerActionContext context,
        SelectedSessionIdentity leader, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(SynchronizeLeaderAsync));
        return Mutate(context, run => run.SynchronizeLeader(leader), token);
    }
    public ValueTask<TowerActionResult> MarkSettledAsync(TowerActionContext context, TowerSettlementReceipt receipt, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(MarkSettledAsync));
        return Mutate(context, run => run.Settle(receipt, clock.GetUtcNow(), rules.DefaultReturnDelay), token);
    }

    public ValueTask<TowerActionResult> SelectReturnAsync(TowerActionContext context, byte option, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(SelectReturnAsync));
        TimeSpan? delay;
        try { delay = rules.ReturnDelay(option); }
        catch (ArgumentOutOfRangeException) { return ValueTask.FromResult(new TowerActionResult(false, Find(context.Session))); }
        return Mutate(context, run => run.SelectReturn(context.Session, option, clock.GetUtcNow(), delay), token);
    }

    public IReadOnlyList<TowerActionContext> FindReturnsDue(DateTimeOffset now)
    {
        lock (sync)
        {
            return runs.Values
                .Where(run => run.ReturnDueAt is { } due && due <= now && run.Completion is { Settled: true })
                .Select(run => run.Members.FirstOrDefault(member => member.IsLeader) is { } leader
                    ? new TowerActionContext(leader.Session, run.RunId, run.Room.State.Version)
                    : null)
                .Where(context => context is not null)
                .Cast<TowerActionContext>()
                .ToArray();
        }
    }

    public async ValueTask<bool> SupportsDungeonAsync(int dungeonId, CancellationToken token) =>
        rules.Supports(dungeonId) && (await rules.GetDefinitionAsync(token, dungeonId).ConfigureAwait(false)).DungeonId == dungeonId;

    // A read: the settlement projection asks it after the action's unit has committed, and a
    // result choice asks it inside its own unit. It opens nothing either way.
    public async ValueTask<bool> CanContinueAsync(TowerActionContext context, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDomainService) + "." + nameof(CanContinueAsync));
        var run = Match(context);
        if (run?.Completion is not { Settled: true }) return false;
        var definition = await rules.GetDefinitionAsync(token, run.DungeonId).ConfigureAwait(false);
        var members = run.Members.Where(x => ValidSession(x.Session)).ToArray();
        if (members.Length == 0) return false;
        foreach (var member in members)
        {
            if (scope.Current is { IsReadOnly: false }) Pending.AuthenticatedSessions.Add(member.Session);
            var store = inventory.GetStore(member.Session.CharacterId, BusinessInventoryStoreKind.Item);
            foreach (var requirement in definition.RequiredItems)
            {
                long held = 0;
                foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable,
                    BusinessInventorySpace.Material, BusinessInventorySpace.Quest, BusinessInventorySpace.ExpertMaterial })
                    if (store.FindContainer(space, isDefault: true) is { } container)
                        foreach (var item in await container.GetAllAsync(token).ConfigureAwait(false))
                            if (materialRules.IsConsumable(item, requirement.ItemId)) held += item.Count;
                if (held < requirement.Count) return false;
            }
        }
        return true;
    }

    private ValueTask<TowerActionResult> RewardingMutation(TowerActionContext context,
        Func<IllusionTowerDgn, TowerActionResult> action, IEffectFootprintRecorder footprint, CancellationToken token, bool leaving = false) => Execute(async () =>
    {
        var current = Match(context, leaving);
        if (current is null) return new TowerActionResult(false, null);
        footprint.DemandEffects(new(context.Session.AccountId, context.Session.CharacterId, context.Session));
        var next = current.Clone();
        var result = action(next);
        if (!result.Changed) return result;
        if (current.Room.State.Phase != TowerRoomPhase.Cleared && next.Room.State.Phase == TowerRoomPhase.Cleared)
        {
            var participants = next.Members.Where(x => ValidSession(x.Session)).ToArray();
            foreach (var member in participants) Pending.AuthenticatedSessions.Add(member.Session);
            await events
                .EnqueueAsync(new DungeonMapCleared(new(next.RunId), next.DungeonId, DungeonDifficulty.Normal,
                    new(0, checked((byte)(next.Room.State.Level - 1))), next.Room.State.MapId, next.Room.State.Version,
                    next.Room.State.ClearedAt!.Value, [.. participants.Select(member =>
                        new DungeonRunParticipant(member.Session.AccountId, member.Session.CharacterId, member.Slot, member.IsLeader))]),
                    token).ConfigureAwait(false);
        }
        var wasSettled = current.Completion is { Settled: true };
        var killRewards = ImmutableArray<TowerKillReward>.Empty;
        // Stale identities must not block cleanup, nor receive another session's awards.
        var eligible = next.Snapshot() with { Members = next.Members.Where(x => ValidSession(x.Session)).ToImmutableArray() };
        foreach (var member in eligible.Members) Pending.AuthenticatedSessions.Add(member.Session);
        if (result.KilledActor is { } actor)
            killRewards = await rewards.KillAsync(eligible, actor, footprint, token).ConfigureAwait(false);
        if (next.Completion is { Settled: false, Reason: TowerEndReason.Completed or TowerEndReason.Defeated })
            next.Settle(await rewards.SettleAsync(eligible, next.Seed, footprint, token).ConfigureAwait(false),
                clock.GetUtcNow(), rules.DefaultReturnDelay);
        footprint.DemandEffects(new(context.Session.AccountId, context.Session.CharacterId, context.Session));
        if (current.UsesTowerInventory)
        {
            foreach (var departed in current.Members.Where(member => !next.Members.Any(x => x.Session == member.Session)))
                towerInventory
                    .Leave(departed.Session.CharacterId, current.RunId);
        }
        footprint.DemandEffects(new(context.Session.AccountId, context.Session.CharacterId, context.Session));
        foreach (var departed in current.Members.Where(member => !next.Members.Any(x => x.Session == member.Session)))
            participation.Release(departed.Session.CharacterId, new(DungeonParticipationMode.IllusionTower, current.RunId));
        footprint.DemandEffects(new(context.Session.AccountId, context.Session.CharacterId, context.Session));
        Pending.Put(current, next);
        return next.Result(true, result.KilledActor) with
        {
            KillRewards = killRewards,
            Drops = result.Drops,
            SettlementCreated = !wasSettled && next.Completion is { Settled: true },
        };
    }, token);

    private sealed class Changes(IllusionTowerDomainService owner) : ITransactionParticipant
    {
        private readonly Dictionary<Guid, (IllusionTowerDgn? Before, IllusionTowerDgn After)> changes = [];
        public HashSet<CharacterId> OrdinaryEntries { get; } = [];
        public HashSet<SelectedSessionIdentity> AuthenticatedSessions { get; } = [];
        public bool CommitCanFail => false;
        public IllusionTowerDgn? Find(CharacterId id)
        {
            var staged = changes.Values.FirstOrDefault(x => x.After.Members.Any(m => m.Session.CharacterId == id)).After;
            if (staged is not null) return staged;
            lock (owner.sync)
            {
                if (!owner.membership.TryGetValue(id, out var runId)) return null;
                return changes.ContainsKey(runId) ? null : owner.runs[runId];
            }
        }
        public void Add(IllusionTowerDgn next) => changes.Add(next.RunId, (null, next));
        public void Put(IllusionTowerDgn previous, IllusionTowerDgn next) => changes[next.RunId] =
            (changes.TryGetValue(next.RunId, out var existing) ? existing.Before : previous, next);
        public ValueTask PrepareAsync(CancellationToken token)
        {
            lock (owner.sync)
            {
                if (AuthenticatedSessions.Any(x => !owner.ValidSession(x)))
                    throw new InvalidOperationException("Tower session changed before commit.");
                foreach (var (runId, (before, after)) in changes)
                {
                    if (owner.runs.GetValueOrDefault(runId) != before) throw new InvalidOperationException("Tower changed before commit.");
                    foreach (var member in after.Members)
                    {
                        if (owner.membership.TryGetValue(member.Session.CharacterId, out var owned) && owned != runId
                            && (!changes.TryGetValue(owned, out var releasing) || releasing.After.Members.Any(x => x.Session.CharacterId == member.Session.CharacterId)))
                            throw new InvalidOperationException("Tower member changed before commit.");
                        if (before is null && owner.InOrdinary(member.Session.CharacterId))
                            throw new InvalidOperationException("Tower admission/session changed before commit.");
                    }
                }
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken token)
        {
            lock (owner.sync)
            {
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
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
}
