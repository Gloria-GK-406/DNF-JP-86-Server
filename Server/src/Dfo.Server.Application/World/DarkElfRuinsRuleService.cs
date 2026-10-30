using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using System.Collections.Immutable;

namespace Dfo.Server.Application.World;

internal sealed class DarkElfRuinsRuleService(IDarkElfRuinsContentSource source,
    IDungeonDefinitionSource dungeons) : IDarkElfRuinsRuleService
{
    public DarkElfRuinsCatalog Catalog => source.Load();
    public int EntryQuestId => 2656;
    public int ReviveLimit => 4;
    public int MaximumParticipants => 4;
    // A21 monster.lst -> DarkelfTemple/groop/groop.mob, [category] [groop].
    public bool IsGuardian(int monsterCode) => monsterCode == 61379;
    public System.Collections.Immutable.ImmutableHashSet<int> RequiredCombatantCodes { get; } = [61379];
    public bool Supports(int dungeonId) => dungeonId is >= 5006 and <= 5029
        && Catalog.Dungeons.Any(x => x.DungeonId == dungeonId);

    public async ValueTask<DarkElfRuinsEntryRule> EntryAsync(int dungeonId, DayOfWeek day, CancellationToken ct)
    {
        if (!Enum.IsDefined(day)) throw new ArgumentOutOfRangeException(nameof(day));
        var directory = Catalog.Dungeons.SingleOrDefault(x => x.DungeonId == dungeonId)
            ?? throw new InvalidDataException("Unknown dark elf ruins dungeon.");
        var dungeon = await dungeons.GetAsync(dungeonId, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing dark elf ruins dungeon content.");
        if (dungeon.ConsumesFatigue || dungeon.MinimumRequiredLevel != 40 || dungeon.Mazes.Count == 0)
            throw new InvalidDataException("Incompatible dark elf ruins dungeon content.");
        return new(directory, dungeon, Catalog.Difficulties[directory.License],
            Catalog.OpenDungeons[(int)day].Contains(dungeonId),
            Catalog.Rewards.Single(x => x.License == directory.License), Catalog.DailyRewards[dungeonId]);
    }

    public int GuardianRate(int monthlyEntryOrdinal)
    {
        if (monthlyEntryOrdinal < 1) throw new ArgumentOutOfRangeException(nameof(monthlyEntryOrdinal));
        return Catalog.GuardianRates.LastOrDefault(x => x.Entries <= monthlyEntryOrdinal)?.PerTenThousand ?? 0;
    }

    public DarkElfRuinsMazeChoice ChooseMaze(DarkElfRuinsEntryRule rule, IReadOnlyList<DarkElfRuinsProgress> participants, int seed)
    {
        if (participants.Count == 0) throw new ArgumentException("Ruins entry requires participants.");
        var random = new Random(seed);
        // One party receives one maze; every entrant must have room under the monthly cap.
        var guardian = rule.Directory.GuardianMazes.Count > 0
            && participants.All(x => x.Guardians < Catalog.MonthlyGuardians)
            && random.Next(10000) < participants.Min(x => GuardianRate(checked(x.MonthlyEntries + 1)));
        var candidates = guardian ? rule.Directory.GuardianMazes : rule.Directory.OrdinaryMazes;
        if (candidates.Count == 0) throw new InvalidDataException("Ruins maze candidates are empty.");
        return new(candidates[random.Next(candidates.Count)], guardian);
    }

    public ItemDeliveryGrant? GuardianDrop(DarkElfRuinsTierReward rewards, int ticket)
    {
        // User-approved server rule: archive weights use a 10,000 basis, including the empty tail.
        if (ticket is < 0 or >= 10000) throw new ArgumentOutOfRangeException(nameof(ticket));
        var end = 0;
        foreach (var drop in rewards.GuardianDrops)
        {
            end = checked(end + drop.Weight);
            if (end > 10000) throw new InvalidDataException("Guardian equipment weights exceed 10,000.");
            if (ticket < end) return new(drop.ItemId, 1);
        }
        return null;
    }

    public DungeonLayout PrepareLayout(DungeonLayout layout, byte characterLevel, DarkElfRuinsEntryPlan plan)
    {
        var features = Catalog.Dungeons.Single(x => x.DungeonId == layout.DungeonId).MazeFeatures.GetValueOrDefault(layout.MazeIndex);
        var objects = new List<DungeonObjectSpawn>();
        var random = new Random(layout.Seed);
        foreach (var group in features?.ObjectGroups ?? [])
        {
            var candidates = group.Objects.ToArray();
            for (var i = candidates.Length - 1; i > 0; i--)
            {
                var selected = random.Next(i + 1);
                (candidates[i], candidates[selected]) = (candidates[selected], candidates[i]);
            }
            objects.AddRange(candidates.Take(group.SelectCount));
        }
        var warps = features?.Warps.ToImmutableDictionary() ?? ImmutableDictionary<MazeCell, MazeCell>.Empty;
        if (objects.Any(x => !layout.Cells.ContainsKey(x.Cell))
            || warps.Any(x => x.Key == x.Value || !layout.Cells.ContainsKey(x.Key) || !layout.Cells.ContainsKey(x.Value)))
            throw new InvalidDataException("Ruins objects or warps refer to missing cells.");
        return layout with { Mode = new DungeonRunMode.DarkElfRuins(plan), ExperienceWeight = 0, FixedMonsterLevel = characterLevel,
            RequiredCombatantCodes = RequiredCombatantCodes, RandomObjects = objects.ToImmutableArray(), Warps = warps };
    }

    public DarkElfRuinsReward FreezeReward(DarkElfRuinsEntryRule rule, bool guardianDefeated, int elapsedMilliseconds, int seed)
    {
        var daily = rule.DailyReward with { Count = checked(rule.DailyReward.Count * (guardianDefeated ? 2 : 1)) };
        var equipment = guardianDefeated ? GuardianDrop(rule.Rewards, new Random(seed).Next(10000)) : null;
        return new(rule.Directory.DungeonId, guardianDefeated, Math.Max(0, elapsedMilliseconds), daily,
            rule.Rewards.Clear.ToArray(), equipment is null ? [] : [equipment]);
    }

    public DarkElfRuinsProgress Advance(DarkElfRuinsProgress progress, int clearedLicense, bool revived)
    {
        if (progress.License is < 1 or > 4 || progress.Clears < 0) throw new InvalidDataException("Invalid ruins progression.");
        if (clearedLicense != progress.License || progress.License == 4) return progress;
        // Authorized rule: shared regional tier, N clears followed by a separate no-coin clear.
        var required = progress.License switch { 1 => 1, 2 => 3, 3 => 7, _ => throw new InvalidOperationException() };
        return progress.Clears >= required && !revived
            ? progress with { License = progress.License + 1, Clears = 0 }
            : progress with { Clears = Math.Min(required, progress.Clears + 1) };
    }
}
