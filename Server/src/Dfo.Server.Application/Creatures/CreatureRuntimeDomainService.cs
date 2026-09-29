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
internal sealed class CreatureRuntimeDomainService(ICreatureDomainStore store, ICreatureRuleSource rules,
    IDomainTransactionAccessor transactions, ISelectedSessionDomainService sessions,
    TimeProvider clock, ICreatureDomainService? inventory = null,
    ICreatureTransformationDomainService? transformations = null) : ICreatureRuntimeDomainService
{
    private readonly object gate = new();
    private readonly Dictionary<CharacterId, Entry> entries = [];
    // Null means committed. Keep the key until session exit so repeated clear events are inert.
    private readonly Dictionary<SelectedSessionIdentity, Dictionary<(DungeonRunId Run, MazeCell Cell), RoomReward?>> roomRewards = [];
    private readonly Queue<CreaturePendingProjection> projections = new();
    public IReadOnlyList<CreaturePendingProjection> DrainProjections()
    {
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
        DateTimeOffset Due, bool Combat, int? Worn, IReadOnlyDictionary<int, double> Stomachs)
    {
        public IReadOnlySet<(DungeonRunId Run, MazeCell Cell)> AwardedRooms { get; init; } = new HashSet<(DungeonRunId, MazeCell)>();
        public IReadOnlySet<int> TownRevivals { get; init; } = new HashSet<int>();
    }
    private sealed record RoomReward(DungeonRunId Run, MazeCell Cell, int Amount, int? CreatureKey, bool ResolveInitialPet);

    public void QueueRoomExperience(SelectedSessionIdentity session, DungeonRunId runId, MazeCell cell, int amount)
    {
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
        lock (gate)
        {
            foreach (var old in roomRewards.Keys.Where(x => x.CharacterId == session.CharacterId).ToArray())
                roomRewards.Remove(old);
            entries[session.CharacterId] = new(session, clock.GetUtcNow(), clock.GetUtcNow(), false, null,
                new Dictionary<int, double>());
        }
    }

    public void ChangeCombat(CharacterId characterId, bool combat)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(characterId, out var entry) || entry.Combat == combat) return;
            var now = clock.GetUtcNow();
            var balances = entry.Stomachs.ToDictionary(x => x.Key,
                x => CreatureRules.AdvanceStomach(x.Value, now - entry.At,
                    entry.Combat && entry.Worn == x.Key));
            var revivals = entry.TownRevivals.ToHashSet();
            if (!combat && entry.Worn is { } worn && balances.GetValueOrDefault(worn) <= 0)
            {
                balances[worn] = 1;
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
        CancellationToken cancellationToken, bool? combat = null) =>
        await WithinAsync(tx => AdvanceCoreAsync(tx, session, combat, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask<IReadOnlyList<CreatureRuntimeChange>> AdvanceCoreAsync(IDomainTransaction tx,
        SelectedSessionIdentity session, bool? combat, CancellationToken ct)
    {
        if (sessions.FindByCharacter(session.CharacterId) != session) return [];
        var pending = tx.Enlist(this, () => new Pending(this));
        var previous = pending.Read(session.CharacterId);
        if (previous is null || previous.Session != session) return [];
        var now = clock.GetUtcNow();
        var pets = store.SessionIn(tx);
        var all = await pets.ReadAllAsync(session.CharacterId, ct).ConfigureAwait(false);
        var current = await pets.ReadCurrentAsync(session.CharacterId, ct).ConfigureAwait(false);
        var fighting = combat ?? previous.Combat;
        var next = new Dictionary<int, double>();
        var changed = new List<CreatureRuntimeChange>();
        var nextDelay = TimeSpan.FromMinutes(6);
        foreach (var state in all)
        {
            var definition = await rules.GetAsync(state.ItemTemplateId, ct).ConfigureAwait(false);
            var oldValue = previous.Stomachs.GetValueOrDefault(state.ItemUid, state.Stomach);
            var elapsed = previous.Stomachs.ContainsKey(state.ItemUid) ? now - previous.At : TimeSpan.Zero;
            var value = CreatureRules.AdvanceStomach(oldValue, elapsed,
                previous.Combat && previous.Worn == state.ItemUid, definition?.HungerEnabled ?? true);
            var equipped = current?.ItemUid == state.ItemUid;
            if (equipped && !fighting && value <= 0) value = 1;
            var updated = state with { Stomach = (int)value, ExactStomach = value };
            next[state.ItemUid] = value;
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
                pets.Write(session.CharacterId, updated);
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
                await GainCoreAsync(tx, session, pet, reward.Amount, current?.ItemUid == uid, ct).ConfigureAwait(false);
            pending.Acknowledge(session, reward);
        }
        return changed;
    }

    public async ValueTask<CreatureRuntimeChange?> FeedAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (sessions.FindByCharacter(session.CharacterId) != session) return null;
        var tx = transactions.Current ?? throw new InvalidOperationException("Feeding requires a use case.");
        await AdvanceCoreAsync(tx, session, null, ct).ConfigureAwait(false);
        var pending = tx.Enlist(this, () => new Pending(this));
        var entry = pending.Read(session.CharacterId);
        if (entry?.Session != session) return null;
        var pets = store.SessionIn(tx);
        var pet = await pets.ReadCurrentAsync(session.CharacterId, ct).ConfigureAwait(false);
        if (pet is null) return null;
        pet = pet with { ExactStomach = entry.Stomachs.GetValueOrDefault(pet.ItemUid, pet.Stomach) };
        var value = CreatureRules.Feed(entry.Stomachs.GetValueOrDefault(pet.ItemUid, pet.Stomach));
        var after = pet with { Stomach = (int)value, ExactStomach = value };
        pets.Write(session.CharacterId, after);
        var balances = entry.Stomachs.ToDictionary(x => x.Key, x => x.Value);
        balances[pet.ItemUid] = value;
        pending.Write(session.CharacterId, entry with { Stomachs = balances });
        var change = new CreatureRuntimeChange(pet, after, true);
        tx.Enlist(this, () => new Pending(this)).Notify(session, [change]);
        return change;
    }

    public async ValueTask<CreatureRuntimeChange?> GainExperienceAsync(SelectedSessionIdentity session,
        int amount, CancellationToken ct)
    {
        if (sessions.FindByCharacter(session.CharacterId) != session) return null;
        var tx = transactions.Current ?? throw new InvalidOperationException("Creature experience requires a use case.");
        await AdvanceCoreAsync(tx, session, null, ct).ConfigureAwait(false);
        var pets = store.SessionIn(tx);
        var pet = await pets.ReadCurrentAsync(session.CharacterId, ct).ConfigureAwait(false);
        return pet is null ? null : await GainCoreAsync(tx, session, pet, amount, true, ct).ConfigureAwait(false);
    }

    private async ValueTask<CreatureRuntimeChange?> GainCoreAsync(IDomainTransaction tx,
        SelectedSessionIdentity session, CreatureState pet, int amount, bool equipped, CancellationToken ct)
    {
        var pets = store.SessionIn(tx);
        if (pet is null || pet.Stomach <= 0 || pet.ModeFlag != 0 || amount <= 0) return null;
        var rule = await rules.GetAsync(pet.ItemTemplateId, ct).ConfigureAwait(false);
        if (rule is null || rule.IsEgg || rule.HungerEnabled == false) return null;
        if (inventory is not null)
        {
            var equipment = await inventory.GetStore(session.CharacterId)
                .GetContainer(BusinessInventorySpace.WornCreatureEquipment).ReadAllAsync(ct).ConfigureAwait(false);
            var bonuses = new List<float>();
            foreach (var item in equipment)
                if (await rules.GetArtifactExperienceAsync(item.ItemId, ct).ConfigureAwait(false) is { } artifact
                    && pet.Level >= artifact.RequiredCreatureLevel)
                    bonuses.Add(artifact.BonusPercent);
            amount = CreatureRules.CalculateExperienceGain(amount, bonuses.Take(3).ToArray());
        }
        var growth = CreatureRules.GainExperience(rule, pet.Experience, amount);
        var after = pet with { Experience = growth.Experience, Level = growth.Level };
        if (pet.Experience == after.Experience && pet.Level == after.Level) return null;
        pets.Write(session.CharacterId, after);
        var transformation = equipped && growth.LevelChanged && transformations is not null
            ? await transformations.EvolveAsync(session, ct).ConfigureAwait(false) : null;
        var change = new CreatureRuntimeChange(pet, transformation?.After ?? after, equipped)
        {
            Transformation = transformation
        };
        tx.Enlist(this, () => new Pending(this)).Notify(session, [change]);
        return change;
    }

    public async ValueTask<CreatureRuntimeChange?> GainRoomExperienceAsync(SelectedSessionIdentity session,
        DungeonRunId runId, MazeCell cell, int amount, CancellationToken ct)
    {
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
        await WithinAsync(async tx =>
        {
            await AdvanceCoreAsync(tx, session, null, ct).ConfigureAwait(false);
            var pending = tx.Enlist(this, () => new Pending(this));
            if (pending.Read(session.CharacterId)?.Session == session) pending.Write(session.CharacterId, null);
            return true;
        }, ct).ConfigureAwait(false);
    }

    private async ValueTask<T> WithinAsync<T>(Func<IDomainTransaction, ValueTask<T>> action, CancellationToken ct)
    {
        if (transactions.Current is { } current) return await action(current).ConfigureAwait(false);
        var transaction = new DomainTransaction();
        try
        {
            var result = await action(transaction).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch { await transaction.RollbackAsync().ConfigureAwait(false); throw; }
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
