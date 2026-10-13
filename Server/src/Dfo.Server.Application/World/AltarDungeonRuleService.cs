using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>Pure altar planning; it never reads a character, session or mutable run.</summary>
internal sealed class AltarDungeonRuleService(AltarDungeonPolicy policy, IAltarDungeonContentSource content, IExperienceRuleService experience,
    IClearRewardRuleService clearRewards) : IAltarDungeonRuleService
{
    public ValueTask<AltarDungeonDefinition> GetDefinitionAsync(int dungeonId, CancellationToken ct) => content.GetAsync(dungeonId, ct);
    public AltarDungeonPolicy Policy => policy;
    public bool IsOpen(int dungeonId, DateTimeOffset now)
    {
        var windows = policy.OpeningWindows.Where(x => x.DungeonId == dungeonId).ToArray();
        var time = TimeOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(8)).DateTime);
        return windows.Length == 0 || windows.Any(x => x.Opens == x.Closes
            || (x.Opens < x.Closes ? time >= x.Opens && time < x.Closes : time >= x.Opens || time < x.Closes));
    }
    public int AdmissionGold(AltarDungeonDefinition definition, int level, int previousEntries)
    {
        if (definition.Kind != AltarDungeonKind.Endless) return 0;
        var pricing = definition.AdmissionPricing ?? throw new InvalidDataException("Endless altar admission prices are unavailable.");
        var band = pricing.Prices.SingleOrDefault(x => level >= x.MinimumLevel && level <= x.MaximumLevel)
            ?? throw new InvalidDataException("Endless altar admission level is not priced.");
        var increase = pricing.EntryIncreasePercent.OrderBy(x => x.Key).FirstOrDefault(x => x.Key > previousEntries);
        if (increase.Key == 0) throw new InvalidDataException("Endless altar admission count is not priced.");
        return checked((int)((long)band.Gold * (100 + increase.Value) / 100 + pricing.LotteryGold));
    }
    public IReadOnlyList<AltarScheduledWave> PlanRound(AltarMapDefinition map, int localRound, byte difficulty,
        int completedRounds, byte level, int firstActorSequence)
    {
        var round = map.Rounds.SingleOrDefault(x => x.Number == localRound)
            ?? throw new InvalidDataException("Altar round is absent from the current map.");
        var phases = round.Phases.Where(x => x.Difficulty == 0 || x.Difficulty == difficulty).ToArray();
        var drafts = new List<(int Delay, int Phase, int Batch, AltarPhaseDefinition Rule, AltarMonsterDefinition Monster, int Count)>();
        long elapsed = 0;
        for (var index = 0; index < phases.Length;)
        {
            var concurrent = Math.Min(phases.Length - index, Math.Max(1, phases[index].ConcurrentPhaseCount));
            long groupEnd = elapsed;
            AltarMonsterDefinition? tail = null;
            for (var offset = 0; offset < concurrent; offset++)
            {
                var phase = phases[index + offset];
                var monster = map.Monsters[phase.MonsterTemplateIndex];
                var batches = Math.Max(1, monster.BatchCount);
                for (var batch = 0; batch < batches; batch++)
                {
                    var count = checked(monster.BaseSpawnCount + monster.SpawnCountIncrement * batch);
                    if (count <= 0) continue;
                    if (count > ushort.MaxValue) throw new InvalidDataException("Altar spawn count exceeds the actor namespace.");
                    var delay = checked((int)(elapsed + phase.DelayMilliseconds + (long)monster.SpawnIntervalMilliseconds * batch));
                    drafts.Add((delay, index + offset, batch, phase, monster, count));
                }
                var end = elapsed + phase.DelayMilliseconds + (long)monster.SpawnIntervalMilliseconds * (batches - 1);
                if (tail is null || end >= groupEnd) { groupEnd = end; tail = monster; }
            }
            if (tail is not null)
                elapsed = groupEnd + Math.Max(0, tail.DurationMilliseconds - (long)tail.SpawnIntervalMilliseconds * Math.Max(1, tail.BatchCount));
            index += concurrent;
        }
        if (drafts.Count == 0) throw new InvalidDataException("Altar round has no scheduled actors.");
        var sequence = firstActorSequence;
        var result = new List<AltarScheduledWave>();
        foreach (var draft in drafts.OrderBy(x => x.Delay).ThenBy(x => x.Phase).ThenBy(x => x.Batch))
        {
            if ((long)sequence + draft.Count - 1 > ushort.MaxValue || sequence < 1)
                throw new InvalidDataException("Altar actor sequence namespace exhausted.");
            var actors = Enumerable.Range(0, draft.Count).Select(_ => new AltarSpawn(checked((ushort)sequence++),
                draft.Monster.MonsterCode, 0, level, checked((ushort)Math.Clamp(Math.Round(draft.Rule.Scale * 100), 1, ushort.MaxValue)),
                checked((ushort)draft.Monster.X), checked((ushort)draft.Monster.Y), checked((ushort)draft.Monster.Z))).ToArray();
            result.Add(new(draft.Delay, actors) { SpawnTail = 1 });
        }
        // Boss rank is confined to the final batch, and never drives ordinary boss suppression.
        if (localRound == map.Rounds.Count - 1)
            result[^1] = result[^1] with { SpawnTail = 0,
                Actors = result[^1].Actors.Select(x => x with { ActorType = (byte)((difficulty == 0 ? completedRounds == 14 : completedRounds == 6) ? 3 : 0) }).ToArray() };
        return result;
    }
    public IReadOnlyList<AltarParticipantReward> Rewards(AltarDungeonState state, int seed)
    {
        var definition = state.Definition.Rewards;
        var random = new Random(seed);
        var result = new List<AltarParticipantReward>();
        foreach (var player in state.Participants)
        {
            var roundWeight = definition.RewardExperienceWeights[Math.Min(state.CompletedRounds, definition.RewardExperienceWeights.Count - 1)];
            var entryWeight = state.Definition.Kind == AltarDungeonKind.Ultimate ? 10.0
                : definition.EntryCountExperienceWeights[Math.Min(player.EntryCount, definition.EntryCountExperienceWeights.Count - 1)];
            var exp = (uint)Math.Clamp(Math.Floor(state.CompletedRounds * (double)experience.MobReward(player.Level) * roundWeight * entryWeight), 0, uint.MaxValue);
            var cardCount = state.CompletedRounds == 0 ? 0 : Math.Clamp(10 * state.CompletedRounds / state.Definition.MaxRounds, 1, 10);
            var progress = Math.Clamp(state.CompletedRounds, 0, 10);
            var cards = new List<AltarReward>();
            for (var index = 0; index < cardCount; index++)
            {
                var roll = random.Next(checked(definition.RewardWeightPerProgress * 10));
                var occasion = new ClearRewardOccasion(ClearRewardCardKind.Free, player.Level, state.Definition.BasisLevel,
                    DungeonDifficulty.Normal, state.Definition.Kind == AltarDungeonKind.Endless ? 3 : 1, state.Participants.Count);
                var face = clearRewards.FaceFor(occasion, unchecked((ulong)random.NextInt64()));
                if (roll < progress * definition.GoldCandidateWeight)
                    cards.Add(new(0, (int)Math.Clamp(Math.Floor(face.Gold * definition.GoldAmountWeight), 0, int.MaxValue)));
                else if (roll < progress * (definition.GoldCandidateWeight + definition.ItemCandidateWeight)
                    && face.Slots.FirstOrDefault()?.Item is { } item)
                    cards.Add(new(item.ItemId, item.Quantity, item.Durability));
                else cards.Add(new(-1, 0));
            }
            if (state.Definition.Kind == AltarDungeonKind.Ultimate)
            {
                var probability = definition.UltimateRewards.SingleOrDefault(x => x.Point == state.DifficultyPoints);
                var roll = random.Next(100);
                if (probability is not null && roll < probability.Item1252Probability) cards.Add(new(1252, 1));
                else if (probability is not null && roll < probability.Item1252Probability + probability.Item1251Probability) cards.Add(new(1251, 1));
            }
            var gold = (int)Math.Min(int.MaxValue, cards.Where(x => x.ItemId == 0).Sum(x => (long)x.Count));
            result.Add(new(player.Participant, exp, gold, cards.Where(x => x.ItemId > 0 && x.Count > 0).ToArray()) { Cards = cards });
        }
        return result;
    }
}
