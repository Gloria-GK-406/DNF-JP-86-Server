using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Creatures;

/// <summary>One shared online clock directory; persisted values never accrue offline time.</summary>
internal sealed class CreatureRuntimeDomainService(ICreatureDomainStore store, ICreatureRuleService rules,
    IDomainTransactionAccessor transactions, ISelectedSessionDomainService sessions,
    TimeProvider clock, ICharacterBusinessInventoryDomainService? inventories = null)
    : ICreatureRuntimeDomainService, ICreatureInstanceDomainService
{
    public ICreatureCollection Of(CharacterId characterId) => new CreatureCollection(() =>
        CreateContext(transactions.Current ?? throw new InvalidOperationException(
            "Creature instances require a use case."), characterId));

    private CreatureInstanceContext CreateContext(IDomainTransaction tx, CharacterId characterId, bool leaving = false) =>
        new(characterId, tx, store.SessionIn(tx),
            inventories?.GetStore(characterId, BusinessInventoryStoreKind.Creature), rules, clock,
            sessions.FindByCharacter(characterId) is { } session ? new InstanceRuntime(this, sessions, tx, session, leaving) : null);

    private DateTimeOffset Now => clock.GetUtcNow();
    private readonly object gate = new();
    private readonly Dictionary<CharacterId, Entry> entries = [];
    // Null means committed. Keep the key until session exit so repeated clear events are inert.
    private readonly Dictionary<SelectedSessionIdentity, Dictionary<(DungeonRunId Run, MazeCell Cell), RoomReward?>> roomRewards = [];
    private readonly Queue<CreaturePendingProjection> projections = new();
    public IReadOnlyList<CreaturePendingProjection> DrainProjections()
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(DrainProjections));
        lock (gate)
        {
            var result = projections.ToArray();
            projections.Clear();
            return result;
        }
    }
    public bool IsEquipped(SelectedSessionIdentity session, int itemUid)
    {
        lock (gate) return entries.TryGetValue(session.CharacterId, out var entry)
            && entry.Session == session && entry.Worn == itemUid;
    }
    private sealed record Entry(SelectedSessionIdentity Session, DateTimeOffset At,
        DateTimeOffset Due, bool Combat, int? Worn, IReadOnlyDictionary<int, Balance> Stomachs)
    {
        public IReadOnlySet<(DungeonRunId Run, MazeCell Cell)> AwardedRooms { get; init; } = new HashSet<(DungeonRunId, MazeCell)>();
        public IReadOnlySet<int> TownRevivals { get; init; } = new HashSet<int>();
    }
    private sealed record Balance(double Value, int Baseline, DateTimeOffset At);
    private sealed record RoomReward(DungeonRunId Run, MazeCell Cell, int Amount, int? CreatureKey, bool ResolveInitialPet);

    public void QueueRoomExperience(SelectedSessionIdentity session, DungeonRunId runId, MazeCell cell, int amount)
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(QueueRoomExperience));
        if (amount <= 0) return;
        lock (gate)
        {
            if (!entries.TryGetValue(session.CharacterId, out var entry) || entry.Session != session) return;
            if (!roomRewards.TryGetValue(session, out var queued)) roomRewards[session] = queued = [];
            queued.TryAdd((runId, cell), new(runId, cell, amount, entry.Worn, entry.Stomachs.Count == 0));
        }
    }

    public void Enter(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(Enter));
        lock (gate)
        {
            foreach (var old in roomRewards.Keys.Where(x => x.CharacterId == session.CharacterId).ToArray())
                roomRewards.Remove(old);
            entries[session.CharacterId] = new(session, clock.GetUtcNow(), clock.GetUtcNow(), false, null,
                new Dictionary<int, Balance>());
        }
    }

    public void DiscardExact(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(DiscardExact));
        lock (gate)
        {
            if (entries.TryGetValue(session.CharacterId, out var entry) && entry.Session == session) entries.Remove(session.CharacterId);
            roomRewards.Remove(session);
            var retained = projections.Where(x => x.Session != session).ToArray();
            projections.Clear();
            foreach (var projection in retained) projections.Enqueue(projection);
        }
    }

    public void ChangeCombat(CharacterId characterId, bool combat)
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(ChangeCombat));
        lock (gate)
        {
            if (!entries.TryGetValue(characterId, out var entry) || entry.Combat == combat) return;
            var now = clock.GetUtcNow();
            var balances = entry.Stomachs.ToDictionary(x => x.Key,
                x => x.Value with { Value = PetInstance.AdvanceBalance(x.Value.Value, now - x.Value.At,
                    entry.Combat && entry.Worn == x.Key), At = now });
            var revivals = entry.TownRevivals.ToHashSet();
            if (!combat && entry.Worn is { } worn && balances.TryGetValue(worn, out var balance) && balance.Value <= 0)
            {
                balances[worn] = balance with { Value = 1 };
                revivals.Add(worn);
            }
            entries[characterId] = entry with
            {
                At = now, Due = now, Combat = combat,
                Stomachs = balances, TownRevivals = revivals
            };
        }
    }

    public void Invalidate(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(Invalidate));
        lock (gate)
            if (entries.TryGetValue(characterId, out var entry))
                entries[characterId] = entry with { Due = clock.GetUtcNow() };
    }

    public IReadOnlyList<SelectedSessionIdentity> DueSessions()
    {
        var now = clock.GetUtcNow();
        lock (gate) return entries.Values.Where(x => x.Due <= now
            || roomRewards.TryGetValue(x.Session, out var rewards) && rewards.Values.Any(r => r is not null))
            .Select(x => x.Session).ToArray();
    }

    public async ValueTask<IReadOnlyList<CreatureRuntimeChange>> AdvanceAsync(SelectedSessionIdentity session,
        CancellationToken cancellationToken, bool? combat = null)
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(AdvanceAsync));
        return await WithinAsync(tx => AdvanceCoreAsync(tx, session, combat, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<CreatureRuntimeChange>> AdvanceCoreAsync(IDomainTransaction tx,
        SelectedSessionIdentity session, bool? combat, CancellationToken ct, bool leaving = false)
    {
        // Leaving settles what the session still holds even after its connection was revoked, as
        // teardown does; everything else needs the session in the world.
        if (leaving ? sessions.StandingOf(session) is null : !sessions.IsInWorld(session)) return [];
        var pending = tx.Enlist(this, () => new Pending(this));
        var previous = pending.Read(session.CharacterId);
        if (previous is null || previous.Session != session) return [];
        var now = clock.GetUtcNow();
        var pets = store.SessionIn(tx);
        var all = await pets.ReadAllAsync(session.CharacterId, ct).ConfigureAwait(false);
        var current = await pets.ReadCurrentAsync(session.CharacterId, ct).ConfigureAwait(false);
        var fighting = combat ?? previous.Combat;
        var context = CreateContext(tx, session.CharacterId, leaving);
        var next = new Dictionary<int, Balance>();
        var changed = new List<CreatureRuntimeChange>();
        var nextDelay = TimeSpan.FromMinutes(6);
        foreach (var state in all)
        {
            var definition = await rules.GetAsync(state.ItemTemplateId, ct).ConfigureAwait(false);
            var cached = previous.Stomachs.GetValueOrDefault(state.ItemUid);
            var matches = cached?.Baseline == state.Stomach;
            var oldValue = matches ? cached!.Value : state.Stomach;
            var elapsed = matches ? now - cached!.At : TimeSpan.Zero;
            var equipped = current?.ItemUid == state.ItemUid;
            var reference = await context.FindAsync(state.ItemUid, ct).ConfigureAwait(false);
            if (reference is null) continue;
            var updated = await new PetInstance(reference, context).AdvanceStomachAsync(state,
                oldValue, elapsed, previous.Combat && previous.Worn == state.ItemUid,
                equipped && !fighting, previous.TownRevivals.Contains(state.ItemUid), ct).ConfigureAwait(false);
            var value = updated.ExactStomach;
            next[state.ItemUid] = new(value, updated.Stomach, now);
            if (definition?.HungerEnabled != false)
            {
                var consuming = fighting && equipped;
                var seconds = consuming && value > 0
                    ? Math.Max(1, (value - Math.Floor(value)) * 60)
                    : !consuming && value < 100 ? Math.Max(1, (Math.Floor(value) + 1 - value) * 360) : 360;
                nextDelay = TimeSpan.FromSeconds(Math.Min(nextDelay.TotalSeconds, seconds));
            }
            if (state.Stomach != updated.Stomach || (oldValue > 0) != (value > 0)
                || previous.TownRevivals.Contains(state.ItemUid))
            {
                changed.Add(new(state with { ExactStomach = previous.TownRevivals.Contains(state.ItemUid) ? 0 : oldValue }, updated, equipped));
            }
        }
        pending.Write(session.CharacterId, new(session, now, now + nextDelay, fighting,
            current?.ItemUid, next) { AwardedRooms = previous.AwardedRooms });
        pending.Notify(session, changed);
        // The clear event is recorded before any fallible I/O. Both award and removal from
        // this queue commit together, so a failed transaction remains due for retry.
        RoomReward[] rewards;
        lock (gate) rewards = roomRewards.TryGetValue(session, out var queue)
            ? queue.Values.OfType<RoomReward>().ToArray() : [];
        foreach (var reward in rewards)
        {
            if (pending.IsAcknowledged(session, reward)) continue;
            var uid = reward.ResolveInitialPet ? current?.ItemUid : reward.CreatureKey;
            var pet = uid is { } id ? await pets.ReadAsync(session.CharacterId, id, ct).ConfigureAwait(false) : null;
            if (pet is not null)
                await GainCoreAsync(tx, session, pet, reward.Amount, ct, leaving).ConfigureAwait(false);
            pending.Acknowledge(session, reward);
        }
        return changed;
    }

    public async ValueTask<CreatureTransformationReceipt?> HatchAsync(SelectedSessionIdentity session,
        BusinessSlot eggSlot, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(HatchAsync));
        if (!sessions.IsInWorld(session)
            || eggSlot.Space != BusinessInventorySpace.CreatureInventory || eggSlot.Index is < 0 or > 139) return null;
        var tx = transactions.Current ?? throw new InvalidOperationException("Hatching requires a use case.");
        var context = CreateContext(tx, session.CharacterId);
        var container = context.Inventory?.FindContainer(eggSlot.Space);
        var item = container is null ? null : await container.GetAsync(eggSlot.Index, ct).ConfigureAwait(false);
        if (item is null || item.Snapshot.LegacyKind != 5 || item.ItemUid is null) return null;
        var egg = await Of(session.CharacterId).FindEggAsync(item.ItemUid!.Value.Value, ct).ConfigureAwait(false);
        var born = egg is null ? null : await egg.BirthAsync(ct).ConfigureAwait(false);
        return (born as PetInstance)?.BirthReceipt;
    }

    public async ValueTask<CreatureRuntimeChange?> GainExperienceAsync(SelectedSessionIdentity session,
        int amount, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(GainExperienceAsync));
        if (!sessions.IsInWorld(session)) return null;
        var pet = await Of(session.CharacterId).CurrentPetAsync(ct).ConfigureAwait(false);
        return pet is null ? null : await pet.AddExpAsync(amount, ct).ConfigureAwait(false);
    }

    private async ValueTask<CreatureRuntimeChange?> GainCoreAsync(IDomainTransaction tx,
        SelectedSessionIdentity session, CreatureState pet, int amount, CancellationToken ct, bool leaving = false)
    {
        var rule = await rules.GetAsync(pet.ItemTemplateId, ct).ConfigureAwait(false);
        if (rule is null || rule.IsEgg) return null;
        var context = CreateContext(tx, session.CharacterId, leaving);
        var reference = await context.FindAsync(pet.ItemUid, ct).ConfigureAwait(false);
        return reference is null ? null : await new PetInstance(reference, context)
            .AddExpCoreAsync(amount, ct).ConfigureAwait(false);
    }

    public async ValueTask<CreatureRuntimeChange?> GainRoomExperienceAsync(SelectedSessionIdentity session,
        DungeonRunId runId, MazeCell cell, int amount, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(GainRoomExperienceAsync));
        var tx = transactions.Current ?? throw new InvalidOperationException("Room experience requires a use case.");
        var pending = tx.Enlist(this, () => new Pending(this));
        var entry = pending.Read(session.CharacterId);
        if (entry?.Session != session || entry.AwardedRooms.Contains((runId, cell))) return null;
        var result = await GainExperienceAsync(session, amount, ct).ConfigureAwait(false);
        entry = pending.Read(session.CharacterId)!;
        pending.Write(session.CharacterId, entry with
        {
            AwardedRooms = entry.AwardedRooms.Where(x => x.Run == runId).Append((runId, cell)).ToHashSet()
        });
        return result;
    }

    public async ValueTask LeaveAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(LeaveAsync));
        lock (gate) if (!entries.TryGetValue(session.CharacterId, out var entry) || entry.Session != session) return;
        await WithinAsync(async tx =>
        {
            await AdvanceCoreAsync(tx, session, null, ct, leaving: true).ConfigureAwait(false);
            var pending = tx.Enlist(this, () => new Pending(this));
            if (pending.Read(session.CharacterId)?.Session == session) pending.Write(session.CharacterId, null);
            return true;
        }, ct).ConfigureAwait(false);
    }

    private async ValueTask<T> WithinAsync<T>(Func<IDomainTransaction, ValueTask<T>> action, CancellationToken ct)
    {
        if (transactions.Current is { } current) return await action(current).ConfigureAwait(false);
        if (transactions is DomainTransactionScope scope)
        {
            await using var lease = scope.Begin();
            var result = await action(scope.Current!).ConfigureAwait(false);
            await lease.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        GameplayWriteAssertion.Writing(nameof(CreatureRuntimeDomainService) + "." + nameof(WithinAsync));
        var transaction = new DomainTransaction();
        try
        {
            var result = await action(transaction).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch { await transaction.RollbackAsync().ConfigureAwait(false); throw; }
    }

    private sealed class InstanceRuntime(CreatureRuntimeDomainService owner, ISelectedSessionDomainService sessions,
        IDomainTransaction tx, SelectedSessionIdentity session, bool leaving) : ICreatureInstanceRuntime
    {
        private Pending Pending => tx.Enlist(owner, () => new Pending(owner));
        public void EnsureActive()
        {
            if (leaving ? sessions.StandingOf(session) is null : !sessions.IsInWorld(session))
                throw new InvalidOperationException("The creature's selected session has changed.");
        }
        public async ValueTask SynchronizeAsync(CancellationToken ct) =>
            _ = await owner.AdvanceCoreAsync(tx, session, null, ct, leaving).ConfigureAwait(false);
        public double Stomach(int uid, int baseline)
        {
            var pending = Pending;
            if (pending.Read(session.CharacterId) is not { } entry || entry.Session != session) return baseline;
            if (entry.Stomachs.TryGetValue(uid, out var balance) && balance.Baseline == baseline) return balance.Value;
            var balances = entry.Stomachs.ToDictionary(x => x.Key, x => x.Value);
            balances[uid] = new(baseline, baseline, owner.Now);
            pending.Write(session.CharacterId, entry with { Stomachs = balances, Due = owner.Now,
                TownRevivals = entry.TownRevivals.Where(value => value != uid).ToHashSet() });
            return baseline;
        }
        public void SetStomach(int uid, double value)
        {
            var pending = Pending;
            if (pending.Read(session.CharacterId) is not { } entry || entry.Session != session) return;
            var balances = entry.Stomachs.ToDictionary(x => x.Key, x => x.Value);
            if (!balances.TryGetValue(uid, out var previous) || previous.Value != value || previous.Baseline != (int)value)
                balances[uid] = new(value, (int)value, owner.Now);
            pending.Write(session.CharacterId, entry with { Stomachs = balances, Due = owner.Now });
        }
        public void Notify(CreatureRuntimeChange change) => Pending.Notify(session, [change]);
        public void ResetStomach(int uid)
        {
            var pending = Pending;
            if (pending.Read(session.CharacterId) is not { } entry || entry.Session != session) return;
            var balances = entry.Stomachs.ToDictionary(x => x.Key, x => x.Value);
            balances.Remove(uid);
            pending.Write(session.CharacterId, entry with { Stomachs = balances, Due = owner.Now });
        }
    }

    private sealed class Pending(CreatureRuntimeDomainService owner) : ITransactionParticipant
    {
        private readonly Dictionary<CharacterId, Entry?> expected = [];
        private readonly Dictionary<CharacterId, Entry?> next = [];
        private readonly List<CreaturePendingProjection> notifications = [];
        private readonly List<(SelectedSessionIdentity Session, RoomReward Reward)> acknowledgements = [];
        public void Acknowledge(SelectedSessionIdentity session, RoomReward reward) => acknowledgements.Add((session, reward));
        public bool IsAcknowledged(SelectedSessionIdentity session, RoomReward reward) =>
            acknowledgements.Any(x => x.Session == session && x.Reward.Run == reward.Run && x.Reward.Cell == reward.Cell);
        public void Notify(SelectedSessionIdentity session, IReadOnlyList<CreatureRuntimeChange> changes)
        {
            if (changes.Count > 0) notifications.Add(new(session, changes));
        }
        public bool CommitCanFail => false;
        public Entry? Read(CharacterId id)
        {
            if (next.TryGetValue(id, out var staged)) return staged;
            lock (owner.gate)
            {
                var value = owner.entries.GetValueOrDefault(id);
                expected.TryAdd(id, value);
                return value;
            }
        }
        public void Write(CharacterId id, Entry? value) { Read(id); next[id] = value; }
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            lock (owner.gate)
                if (expected.Any(x => owner.entries.GetValueOrDefault(x.Key) != x.Value))
                    throw new InvalidOperationException("Creature session changed before commit.");
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken ct)
        {
            lock (owner.gate)
            {
                // Enqueue never edits Entry. Clear only the committed receipt, preserving any
                // different room queued during the database commit window.
                foreach (var (session, reward) in acknowledgements)
                    if (owner.roomRewards.TryGetValue(session, out var rewards))
                        rewards[(reward.Run, reward.Cell)] = null;
                foreach (var (id, value) in next)
                    if (owner.entries.GetValueOrDefault(id) == expected.GetValueOrDefault(id))
                    {
                        if (value is null)
                        {
                            owner.entries.Remove(id);
                            if (expected[id] is { } previous) owner.roomRewards.Remove(previous.Session);
                        }
                        else owner.entries[id] = value;
                        foreach (var notification in notifications.Where(x => x.Session.CharacterId == id))
                            owner.projections.Enqueue(notification);
                    }
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
