using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Achievements;

internal sealed class AchievementDomainService(IAchievementRuleService rules, IAchievementPersistence persistence,
    IEventService events, IAchievementCharacterFactsDomainService characterFacts,
    ITitleBookPersistence titlePersistence, ITitleBookRuleService titleRules) : IAchievementDomainService, IDomainEventSubscriber<MonsterDefeated>,
    IDomainEventSubscriber<DungeonCompleted>, IDomainEventSubscriber<QuestCompleted>, IDomainEventSubscriber<ItemUsed>,
    IDomainEventSubscriber<MaterialConsumed>, IDomainEventSubscriber<CharacterLevelChanged>
{
    public async ValueTask PrepareAsync(CharacterId character, int level, CancellationToken ct)
    {
        var facts = await characterFacts.ReadAsync(character, ct);
        var known = (await persistence.ReadAsync(character, ct)).Select(x => x.AchievementId).ToHashSet();
        known.UnionWith(titleRules.ActivatedAchievements((await titlePersistence.ReadUnlockedSlotsAsync(character, ct)).ToHashSet()));
        foreach (var definition in rules.Catalog())
            if (level >= definition.MinimumLevel && !known.Contains(definition.Id))
                await persistence.EnsureAsync(character, definition.Id, rules.InitialRemaining(definition), ct);
        await ReconcileAuthoritativeAsync(character, facts with { Level = level }, ct);
    }
    public async ValueTask<IReadOnlyList<CharacterAchievementProgressState>> GetAsync(CharacterId character, CancellationToken ct) =>
        (await persistence.ReadAsync(character, ct)).Select(Project).ToArray();
    public async ValueTask<IReadOnlyList<int>> UnlockSatisfiedAchievementsAsync(CharacterId character, CancellationToken ct)
    {
        var progress = (await persistence.ReadAsync(character, ct)).ToDictionary(x => x.AchievementId);
        var completed = progress.Values.Where(x => x.Completed).Select(x => x.AchievementId).ToHashSet();
        var unlocked = titleRules.ActivatedAchievements((await titlePersistence.ReadUnlockedSlotsAsync(character, ct)).ToHashSet());
        completed.UnionWith(unlocked);
        var pending = rules.Catalog().Where(x => !completed.Contains(x.Id) && !unlocked.Contains(x.Id)
            && rules.CanCheckSnapshot(x) && (!progress.TryGetValue(x.Id, out var p) || p.P2 == 0 && p.P3 == 0)).ToArray();
        if (pending.Length == 0) return [];
        var facts = await characterFacts.ReadForUnlockAsync(character, pending, ct);
        var allIds = rules.Catalog().Select(x => x.Id).ToHashSet();
        var newlyCompleted = new List<int>();
        bool Cleared(int id) => completed.Contains(id) || !allIds.Contains(id) && facts.ClearedQuests.Contains(id);
        for (var pass = 0; pass < pending.Length; pass++)
        {
            var changed = false;
            foreach (var d in pending)
            {
                if (completed.Contains(d.Id) || facts.Level < d.MinimumLevel || facts.Level > d.MaximumLevel) continue;
                if (d.RequiresHardcore is { } mode && facts.Hardcore != mode) continue;
                var groups = d.PrerequisiteGroups.Count > 0 ? d.PrerequisiteGroups
                    : d.Prerequisites.Count > 0 ? new[] { d.Prerequisites } : [];
                if (groups.Count > 0 && !groups.Any(g => g.Where(x => x > 0).All(Cleared))) continue;
                var satisfied = d.Condition.Type is "clear quest" or "clearquest"
                    ? d.Condition.Values.All(Cleared) : await rules.IsSatisfiedAsync(d, facts, ct) == true;
                if (!satisfied) continue;
                if (!progress.ContainsKey(d.Id)) await persistence.EnsureAsync(character, d.Id, rules.InitialRemaining(d), ct);
                if (await CompleteAsync(character, d.Id, ushort.MaxValue, ct))
                {
                    completed.Add(d.Id); newlyCompleted.Add(d.Id); changed = true;
                }
            }
            if (!changed) break;
        }
        return newlyCompleted;
    }
    public async ValueTask<CharacterAchievementProgressState?> ReconcileAsync(CharacterId character, int achievementId, CancellationToken ct)
    {
        if (!rules.Catalog().Any(x => x.Id == achievementId)) return null;
        await PrepareAsync(character, (await characterFacts.ReadAsync(character, ct)).Level, ct);
        return (await GetAsync(character, ct)).FirstOrDefault(x => x.AchievementId == achievementId);
    }
    private static CharacterAchievementProgressState Project(AchievementProgress p) => new(p.AchievementId,
        (ushort)Math.Clamp(p.Remaining, 0, ushort.MaxValue), (ushort)Math.Clamp(p.P2, 0, ushort.MaxValue),
        (ushort)Math.Clamp(p.P3, 0, ushort.MaxValue), (ushort)Math.Clamp(p.P4, 0, ushort.MaxValue));
    private async ValueTask<bool> EligibleAsync(CharacterId character, AchievementDefinition d, AchievementCharacterFacts facts, CancellationToken ct)
    {
        if (facts.Level < d.MinimumLevel || facts.Level > d.MaximumLevel) return false;
        if (d.RequiresHardcore is { } mode && facts.Hardcore != mode) return false;
        var groups = d.PrerequisiteGroups.Count > 0 ? d.PrerequisiteGroups
            : d.Prerequisites.Count > 0 ? new[] { d.Prerequisites } : [];
        if (groups.Count == 0) return true;
        foreach (var group in groups)
        {
            var fulfilled = true;
            foreach (var prerequisite in group.Where(x => x > 0))
                if (!await persistence.IsCompletedAsync(character, prerequisite, ct) && !facts.ClearedQuests.Contains(prerequisite)) { fulfilled = false; break; }
            if (fulfilled) return true;
        }
        return false;
    }
    private async ValueTask<bool> CompleteAsync(CharacterId character, int id, int count, CancellationToken ct)
    {
        if (await persistence.IsCompletedAsync(character, id, ct)) return true;
        var before = (await persistence.ReadAsync(character, ct)).FirstOrDefault(x => x.AchievementId == id);
        if (before is null || before.Remaining <= 0 || before.P2 != 0 || before.P3 != 0) return false;
        var complete = await persistence.AdvanceAsync(character, id, count, ct);
        await events.EnqueueAsync(new AchievementProgressChanged(character, id), ct);
        if (complete) await events.EnqueueAsync(new AchievementCompleted(character, id), ct);
        return complete;
    }
    private async ValueTask ReconcileAuthoritativeAsync(CharacterId character, AchievementCharacterFacts facts, CancellationToken ct)
    {
        var progress = await persistence.ReadAsync(character, ct);
        var completed = progress.Where(x => x.Completed).Select(x => x.AchievementId).ToHashSet();
        completed.UnionWith(titleRules.ActivatedAchievements((await titlePersistence.ReadUnlockedSlotsAsync(character, ct)).ToHashSet()));
        var known = rules.Catalog().Select(x => x.Id).ToHashSet();
        bool Cleared(int id)
        {
            if (completed.Contains(id)) return true;
            if (known.Contains(id)) return false;
            return facts.ClearedQuests.Contains(id);
        }
        // Follow completed dependencies to a fixed point. Unsupported rules never complete from client deltas.
        for (var pass = 0; pass < rules.Catalog().Count; pass++)
        {
            var changed = false;
            foreach (var d in rules.Catalog().Where(x => rules.IsSupported(x) && x.Condition.Type is "clearquest" or "clear quest"))
            {
                if (completed.Contains(d.Id) || !await EligibleAsync(character, d, facts, ct)) continue;
                var fulfilled = true;
                foreach (var required in d.Condition.Values)
                    if (!Cleared(required)) { fulfilled = false; break; }
                if (!fulfilled) continue;
                if (await CompleteAsync(character, d.Id, ushort.MaxValue, ct)) { completed.Add(d.Id); changed = true; }
            }
            if (!changed) break;
        }
    }
    private async ValueTask ApplyAsync(Guid eventId, CharacterGameplayFact fact, CancellationToken ct)
    {
        if (!await persistence.TryConsumeFactAsync(fact.CharacterId, eventId, ct)) return;
        var facts = await characterFacts.ReadAsync(fact.CharacterId, ct);
        await PrepareAsync(fact.CharacterId, facts.Level, ct);
        foreach (var d in rules.Catalog())
        {
            var count = rules.Contribution(d, fact);
            if (count > 0 && await EligibleAsync(fact.CharacterId, d, facts, ct)) await CompleteAsync(fact.CharacterId, d.Id, count, ct);
        }
        await ReconcileAuthoritativeAsync(fact.CharacterId, facts, ct);
        if (fact.Kind == "level.changed") await UnlockSatisfiedAchievementsAsync(fact.CharacterId, ct);
    }
    public ValueTask HandleAsync(DomainEventEnvelope<MonsterDefeated> e, CancellationToken ct) => ApplyAsync(e.EventId,
        new(e.Payload.CharacterId, "monster.defeated", e.Payload.MonsterId, 1, e.Payload.DungeonId, e.Payload.Difficulty), ct);
    public ValueTask HandleAsync(DomainEventEnvelope<DungeonCompleted> e, CancellationToken ct) => ApplyAsync(e.EventId,
        new(e.Payload.CharacterId, "dungeon.cleared", e.Payload.DungeonId, 1, e.Payload.DungeonId, e.Payload.Difficulty), ct);
    public ValueTask HandleAsync(DomainEventEnvelope<QuestCompleted> e, CancellationToken ct) => ApplyAsync(e.EventId, new(e.Payload.CharacterId, "quest.completed", e.Payload.QuestId), ct);
    public ValueTask HandleAsync(DomainEventEnvelope<ItemUsed> e, CancellationToken ct) => ApplyAsync(e.EventId, new(e.Payload.CharacterId, "item.used", e.Payload.ItemId, e.Payload.Count), ct);
    public ValueTask HandleAsync(DomainEventEnvelope<MaterialConsumed> e, CancellationToken ct) => ApplyAsync(e.EventId, new(e.Payload.CharacterId, "material.consumed", e.Payload.ItemId, e.Payload.Count), ct);
    public ValueTask HandleAsync(DomainEventEnvelope<CharacterLevelChanged> e, CancellationToken ct) => ApplyAsync(e.EventId, new(e.Payload.CharacterId, "level.changed", e.Payload.After), ct);
}

internal sealed class CharacterGameplayFactDomainService(IEventService events) : ICharacterGameplayFactDomainService
{
    public async ValueTask RecordMonsterDefeatedAsync(MonsterDefeated fact, CancellationToken ct) => _ = await events.EnqueueAsync(fact, ct);
    public async ValueTask RecordDungeonCompletedAsync(DungeonCompleted fact, CancellationToken ct) => _ = await events.EnqueueAsync(fact, ct);
}
