using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Raids;

internal sealed class AntonRaidPhaseRuntime
{
    private readonly AntonRaidRules rules;
    private readonly AntonRaidPhaseRule phase;
    private readonly Dictionary<int, Map> maps;
    private readonly Dictionary<uint, uint> symbols = [];
    private readonly Dictionary<(AntonRaidCombatDeadlineKind Kind, uint Key, byte Type), Scheduled> timers = [];
    private readonly Queue<TriggerEvent> pending = [];
    private readonly Dictionary<byte, AntonRaidBuffTarget> buffs = [];
    private long version;

    internal Guid Instance { get; }
    internal uint Number { get; }
    internal byte Phase { get; }
    internal long Generation { get; }
    internal DateTimeOffset StartedAt { get; }
    internal uint InfectedMap { get; private set; }
    internal int DeathCount { get; set; }
    internal bool? Succeeded { get; private set; }
    internal Dictionary<byte, Sortie> Sorties { get; } = [];
    internal HashSet<CharacterId> Participants { get; } = [];
    internal HashSet<CharacterId> ClearParticipants { get; } = [];
    internal List<DungeonRunId> Ends { get; } = [];
    internal List<KeyValuePair<int, AntonRaidMapStatus>> MapChanges { get; } = [];
    internal List<KeyValuePair<uint, uint>> SymbolUpdates { get; } = [];
    internal List<AntonRaidCombatDeadline> TimerChanges { get; } = [];

    internal AntonRaidPhaseRuntime(Guid instance, uint number, byte index, long generation, AntonRaidRules rules,
        DateTimeOffset startedAt, DateTimeOffset attackDeadline, int seed)
    {
        Instance = instance;
        Number = number;
        Phase = index;
        Generation = generation;
        this.rules = rules;
        phase = rules.Phases[index];
        StartedAt = startedAt;
        maps = phase.Maps.ToDictionary(map => map.DungeonId, map => new Map(map.InitialStatus));
        version = generation;
        SetDeadline(StartedAt, AntonRaidCombatDeadlineKind.Attack, 0, 0, (int)(attackDeadline - StartedAt).TotalSeconds);
        Evaluate(new(null, 0, 0), startedAt, seed);
    }

    private AntonRaidPhaseRuntime(AntonRaidPhaseRuntime old)
    {
        rules = old.rules;
        phase = old.phase;
        Instance = old.Instance;
        Number = old.Number;
        Phase = old.Phase;
        Generation = old.Generation;
        StartedAt = old.StartedAt;
        InfectedMap = old.InfectedMap;
        DeathCount = old.DeathCount;
        Succeeded = old.Succeeded;
        version = old.version;
        maps = new(old.maps);
        symbols = new(old.symbols);
        timers = new(old.timers);
        buffs = new(old.buffs);
        Sorties = new(old.Sorties);
        Participants = [.. old.Participants];
        ClearParticipants = [.. old.ClearParticipants];
    }

    internal AntonRaidPhaseRuntime Copy() => new(this);

    internal IEnumerable<AntonRaidCombatDeadline> Deadlines => timers.Values.Select(timer => timer.Deadline);

    internal bool AttackOpenAt(DateTimeOffset now) => Succeeded is null
        && timers.TryGetValue((AntonRaidCombatDeadlineKind.Attack, 0, 0), out var attack) && now < attack.Deadline.At;

    internal AntonRaidCombatSnapshot Snapshot() => new AntonRaidCombatSnapshot(Instance, Number, Phase, StartedAt, InfectedMap,
        maps.ToDictionary(item => item.Key, item => item.Value.Status), new Dictionary<uint, uint>(symbols),
        Deadlines.Where(timer => timer.Kind is AntonRaidCombatDeadlineKind.Attack or AntonRaidCombatDeadlineKind.MapTimer)
            .OrderBy(timer => timer.At).ToArray(),
        Sorties.Values.Select(sortie => new AntonRaidSortieSnapshot(sortie.Run.Value, sortie.DungeonId, sortie.Squad,
            sortie.Participants.Keys.ToArray(), sortie.Cleared, sortie.UsedCoins, sortie.HitPoints)).ToArray())
    {
        Buffs = rules.Buffs.Select(buff => new AntonRaidBuffGroup(buff.Type,
            buffs.TryGetValue(buff.Type, out var target) ? [target] : [])).ToArray(),
    };

    internal bool HasCapacity(int dungeon, byte squad, DungeonRunId? replacing = null) => Succeeded is null
        && maps.TryGetValue(dungeon, out var map) && map.Status == AntonRaidMapStatus.Open
        && (replacing is { } run ? Sorties.TryGetValue(squad, out var sortie) && sortie.Run == run : !Sorties.ContainsKey(squad))
        && Sorties.Values.Count(other => other.DungeonId == dungeon)
            < phase.Maps.Single(rule => rule.DungeonId == dungeon).PartyLimit;

    internal void StartSortie(DungeonRunId run, int dungeon, byte squad, bool isTransfer)
    {
        var previous = isTransfer ? Sorties[squad] : null;
        Sorties[squad] = new(run, dungeon, squad, new Dictionary<CharacterId, SelectedSessionIdentity>(),
            UsedCoins: previous?.UsedCoins ?? 0)
        {
            BonusCoins = previous?.BonusCoins ?? 0,
        };
    }

    internal bool Clear(Sortie sortie, IReadOnlyCollection<CharacterId> atClear, DateTimeOffset now, int seed)
    {
        if (!AttackOpenAt(now) || sortie.Cleared || !maps.TryGetValue(sortie.DungeonId, out var map)
            || map.Status != AntonRaidMapStatus.Open || atClear.Count == 0)
            return false;
        Sorties[sortie.Squad] = sortie with { Cleared = true };
        ClearParticipants.UnionWith(atClear);
        var mapRule = phase.Maps.Single(rule => rule.DungeonId == sortie.DungeonId);
        var hits = checked(map.Clears + 1);
        maps[sortie.DungeonId] = map with { Clears = hits };
        SetSymbol(mapRule.HitSymbol, (uint)hits);
        if ((uint)hits >= mapRule.RequiredClears)
            SetMap((uint)sortie.DungeonId, AntonRaidMapStatus.Cleared);
        Evaluate(new(null, 0, 0), now, seed);
        return true;
    }

    internal bool Advance(AntonRaidCombatDeadline expected, DateTimeOffset now, int seed)
    {
        var key = (expected.Kind, expected.Key, expected.TimerType);
        if (!timers.TryGetValue(key, out var current) || current.Deadline != expected || expected.At > now)
            return false;
        timers.Remove(key);
        if (expected.Kind == AntonRaidCombatDeadlineKind.Attack)
        {
            Finish(false);
            return true;
        }
        if (expected.Kind == AntonRaidCombatDeadlineKind.ShieldRecovery)
        {
            var rate = InfectedMap != 0 && maps[(int)InfectedMap].Status == AntonRaidMapStatus.Open
                ? rules.InfectedShieldChargeRate : rules.ShieldChargeRate;
            SetShield(Math.Min(10_000u, Shield + (uint)rate), now, seed);
            if (Shield < 10_000)
                SetDeadline(now, AntonRaidCombatDeadlineKind.ShieldRecovery, 219, 0, 1);
            return true;
        }
        if (expected.Kind == AntonRaidCombatDeadlineKind.ReservedMap)
            SetMap(expected.Key, current.MapStatus);
        Evaluate(new(AntonRaidConditionKind.TimerEnded, expected.Key, expected.TimerType), now, seed);
        return true;
    }

    internal bool ApplyShield(uint operand, byte operation, DateTimeOffset now, int seed)
    {
        if (Phase != 1 || Succeeded is not null || operation > 2 || operand > 10_000
            || !maps.TryGetValue(219, out var volcano) || volcano.Status != AntonRaidMapStatus.Open)
            return false;
        var previous = Shield;
        var value = operation switch
        {
            0 => operand,
            1 => Math.Min(10_000u, previous + operand),
            _ => operand >= previous ? 0 : previous - operand,
        };
        if (value == previous) return false;
        SetShield(value, now, seed);
        if (value < 10_000 && !timers.ContainsKey((AntonRaidCombatDeadlineKind.ShieldRecovery, 219, 0)))
            SetDeadline(now, AntonRaidCombatDeadlineKind.ShieldRecovery, 219, 0, 1);
        return true;
    }

    private uint Shield => symbols.GetValueOrDefault(rules.Symbols["[BLACK VOLCANO BARRIER]"]);

    private void SetShield(uint value, DateTimeOffset now, int seed)
    {
        SetSymbol(rules.Symbols["[BLACK VOLCANO BARRIER]"], value);
        Evaluate(new(null, 0, 0), now, seed);
    }

    internal bool ActivateBuff(AntonRaidBuffRule rule, ushort squad, ushort user, DateTimeOffset now)
    {
        if (Succeeded is not null || buffs.TryGetValue(rule.Type, out var active) && active.CooldownUntil > now)
            return false;
        if (rule.Type == 1)
        {
            if (!timers.TryGetValue((AntonRaidCombatDeadlineKind.Attack, 0, 0), out var attack)
                || attack.Deadline.At <= now)
                return false;
            var remaining = (int)Math.Ceiling((attack.Deadline.At - now).TotalSeconds);
            SetDeadline(now, AntonRaidCombatDeadlineKind.Attack, 0, 0, checked(remaining + rule.Effect));
        }
        if (rule.Type == 0)
        {
            if (squad > byte.MaxValue || !Sorties.TryGetValue((byte)squad, out var sortie) || sortie.Participants.Count == 0)
                return false;
            Sorties[(byte)squad] = sortie with { BonusCoins = checked(sortie.BonusCoins + rule.Effect) };
        }
        buffs[rule.Type] = new(squad, user, now.AddSeconds(rule.Duration), now.AddSeconds(rule.Cooldown));
        return true;
    }

    private void Evaluate(TriggerEvent cause, DateTimeOffset now, int seed)
    {
        pending.Enqueue(cause);
        var processed = 0;
        while (pending.TryDequeue(out var trigger) && Succeeded is null)
        {
            if (++processed > 256)
                throw new InvalidDataException("Anton raid trigger chain exceeds its event limit.");
            foreach (var rule in phase.Triggers)
            {
                if (!rule.Conditions.All(condition => Matches(condition, trigger)))
                    continue;
                foreach (var action in rule.Actions)
                    Apply(action, now, seed);
                if (Succeeded is not null) break;
            }
        }
        pending.Clear();
    }

    private bool Matches(AntonRaidCondition condition, TriggerEvent cause) => condition.Kind switch
    {
        AntonRaidConditionKind.Symbol => symbols.GetValueOrDefault(condition.Key) == condition.Value,
        AntonRaidConditionKind.MapStatus => maps.TryGetValue((int)condition.Key, out var map) && (uint)map.Status == condition.Value,
        AntonRaidConditionKind.MapChanged or AntonRaidConditionKind.SymbolChanged =>
            cause.Kind == condition.Kind && condition.Key == cause.Key,
        AntonRaidConditionKind.TimerEnded =>
            cause.Kind == condition.Kind && condition.Key == cause.Key && condition.Value == cause.Value,
        _ => false,
    };

    private void Apply(AntonRaidAction action, DateTimeOffset now, int seed)
    {
        switch (action.Kind)
        {
            case AntonRaidActionKind.SetMap:
                SetMap(action.Key, (AntonRaidMapStatus)action.Value);
                break;
            case AntonRaidActionKind.ReserveMap:
                SetDeadline(now, AntonRaidCombatDeadlineKind.ReservedMap, action.Key, 0, action.Seconds,
                    (AntonRaidMapStatus)action.Value);
                break;
            case AntonRaidActionKind.SetSymbol:
                SetSymbol(action.Key, action.Value);
                break;
            case AntonRaidActionKind.SetTimer:
                SetDeadline(now, AntonRaidCombatDeadlineKind.MapTimer, action.Key, checked((byte)action.Value), action.Seconds);
                break;
            case AntonRaidActionKind.ResetTimer:
                if (timers.Remove((AntonRaidCombatDeadlineKind.MapTimer, action.Key, checked((byte)action.Value)), out var timer))
                    TimerChanges.Add(timer.Deadline with { At = now, Duration = 0 });
                break;
            case AntonRaidActionKind.ClearPhase:
                Finish(true);
                break;
            case AntonRaidActionKind.OpenHatcheries:
                var random = new Random(seed);
                var hatcheries = rules.Hatcheries.ToArray();
                random.Shuffle(hatcheries.AsSpan());
                var selected = hatcheries.Take(rules.OpenHatcheries).ToArray();
                InfectedMap = (uint)selected[0];
                SetSymbol(rules.Symbols["[IS EXIST INFECTION DUNGEON]"], 1);
                SetSymbol(rules.Symbols["[INFECTION DUNGEON INDEX]"], InfectedMap);
                foreach (var map in selected)
                    SetMap((uint)map, AntonRaidMapStatus.Open);
                break;
        }
    }

    private void SetMap(uint key, AntonRaidMapStatus status)
    {
        var id = (int)key;
        var previous = maps[id];
        if (previous.Status == status) return;
        var rule = phase.Maps.Single(candidate => candidate.DungeonId == id);
        var cleared = status == AntonRaidMapStatus.Open ? 0 : previous.Clears;
        maps[id] = new(status, cleared);
        if (status == AntonRaidMapStatus.Open && cleared != previous.Clears)
            SetSymbol(rule.HitSymbol, 0);
        if (status == AntonRaidMapStatus.Hidden)
        {
            timers.Remove((AntonRaidCombatDeadlineKind.ReservedMap, key, 0));
            if (id == 219)
                timers.Remove((AntonRaidCombatDeadlineKind.ShieldRecovery, key, 0));
            if (InfectedMap == key)
            {
                InfectedMap = 0;
                SetSymbol(rules.Symbols["[IS EXIST INFECTION DUNGEON]"], 0);
                SetSymbol(rules.Symbols["[INFECTION DUNGEON INDEX]"], 0);
            }
        }
        MapChanges.Add(new(id, status));
        pending.Enqueue(new(AntonRaidConditionKind.MapChanged, key, 0));
    }

    private void SetSymbol(uint key, uint value)
    {
        var previous = symbols.GetValueOrDefault(key);
        symbols[key] = value;
        SymbolUpdates.Add(new(key, value));
        if (previous != value)
            pending.Enqueue(new(AntonRaidConditionKind.SymbolChanged, key, 0));
        foreach (var rule in phase.Maps)
        {
            if (rule.HitSymbol == key && maps[rule.DungeonId].Clears != value)
                maps[rule.DungeonId] = maps[rule.DungeonId] with { Clears = checked((int)value) };
        }
        if (value != 0 && rules.Symbols.TryGetValue("[KILL ALL CHARACTER]", out var kill) && kill == key)
            EndSorties();
    }

    private void SetDeadline(DateTimeOffset now, AntonRaidCombatDeadlineKind kind, uint key, byte type, int seconds,
        AntonRaidMapStatus status = default)
    {
        var deadline = new AntonRaidCombatDeadline(Instance, Number, Phase, checked(++version),
            kind, key, type, now.AddSeconds(seconds), seconds);
        timers[(kind, key, type)] = new(deadline, status);
        if (kind is AntonRaidCombatDeadlineKind.Attack or AntonRaidCombatDeadlineKind.MapTimer)
            TimerChanges.Add(deadline);
    }

    private void Finish(bool success)
    {
        Succeeded = success;
        timers.Clear();
        EndSorties();
    }

    private void EndSorties()
    {
        Ends.AddRange(Sorties.Values.Select(sortie => sortie.Run));
        Sorties.Clear();
    }

    internal sealed record Sortie(DungeonRunId Run, int DungeonId, byte Squad,
        IReadOnlyDictionary<CharacterId, SelectedSessionIdentity> Participants, bool Cleared = false, int UsedCoins = 0)
    {
        public IReadOnlyList<uint> HitPoints { get; init; } = [];
        public int BonusCoins { get; init; }
    }

    private readonly record struct Map(AntonRaidMapStatus Status, int Clears = 0);
    private readonly record struct Scheduled(AntonRaidCombatDeadline Deadline, AntonRaidMapStatus MapStatus);
    private readonly record struct TriggerEvent(AntonRaidConditionKind? Kind, uint Key, uint Value);
}
