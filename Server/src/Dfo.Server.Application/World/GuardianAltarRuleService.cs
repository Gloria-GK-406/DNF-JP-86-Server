using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class GuardianAltarRuleService(IGuardianAltarContentSource content) : IGuardianAltarRuleService
{
    public GuardianAltarCatalog Catalog => content.Catalog;
    internal const int ActivityId = 119;
    public int EventId => ActivityId;
    public Dfo.Server.Data.Events.EventConfiguration InitialActivity => new(0, "permanent", true, "守护者祭坛", "守护者祭坛",
        "50级以上单人挑战守护者祭坛，每日补充3次免费挑战，不消耗疲劳。", "", "", DateTimeOffset.UnixEpoch,
        new DateTimeOffset(2037, 12, 31, 23, 59, 59, TimeSpan.FromHours(8)));
    public byte MinimumLevel => 50;
    public bool Supports(int dungeonId) => dungeonId is >= 3511 and <= 3523;
    public GuardianAltarProgress Initial(DateOnly period)
    {
        var purchases = Catalog.Shop.Where(x => x.RidableId == 1 && x.UnlockStageId == 0)
            .Select(x => new GuardianAltarPurchase(x.Kind, x.Id, 0)).ToImmutableArray();
        return new(0, period, Catalog.Configuration.DailyRefillTickets, 0, 0, 0, 0, 0, 0,
            ImmutableDictionary<int, int>.Empty.Add(3511, -1), purchases, FillEmptySlots([], purchases), []);
    }
    private ImmutableArray<GuardianAltarSlot> FillEmptySlots(ImmutableArray<GuardianAltarSlot> slots, ImmutableArray<GuardianAltarPurchase> purchases)
    {
        foreach (var owned in purchases.Where(x => x.Kind != GuardianAltarShopKind.Tower))
        {
            var item = Catalog.Shop.Single(x => x.RidableId == 1 && x.Kind == owned.Kind && x.Id == owned.Id);
            if (!item.Settable || item.Levels[owned.Tier].Level <= 0 || slots.Any(x => x.Kind == owned.Kind && x.Id == owned.Id)) continue;
            var empty = Enumerable.Range(owned.Kind == GuardianAltarShopKind.Unit ? 0 : 8,
                owned.Kind == GuardianAltarShopKind.Unit ? 6 : 4).FirstOrDefault(index => !slots.Any(x => x.Index == index), -1);
            if (empty >= 0) slots = slots.Add(new(checked((byte)empty), owned.Kind, owned.Id));
        }
        return slots.OrderBy(x => x.Index).ToImmutableArray();
    }
    public GuardianAltarProgress RefreshTickets(GuardianAltarProgress progress, DateOnly period) =>
        period <= progress.TicketPeriod ? progress : progress with
        { TicketPeriod = period, FreeTickets = Catalog.Configuration.DailyRefillTickets };

    public GuardianAltarAdmission? Admission(GuardianAltarProgress progress, int dungeonId, GuardianAltarDifficulty difficulty, int survivalRound = 1)
    {
        if (!Supports(dungeonId) || !Enum.IsDefined(difficulty)) return null;
        var stage = Catalog.Stages.Single(x => x.DungeonId == dungeonId);
        if (stage.Survival)
        {
            if (!progress.Stages.TryGetValue(3522, out var cleared) || cleared < 0
                || survivalRound < 1 || survivalRound > stage.SurvivalMaps.Length) return null;
            return WithTowers(new(stage, Catalog.Maps[stage.SurvivalMaps[survivalRound - 1]], difficulty, survivalRound), progress);
        }
        if (!progress.Stages.TryGetValue(dungeonId, out var highest) || (int)difficulty > highest + 1) return null;
        var map = stage.Maps.Single(x => x.Difficulty == difficulty);
        return WithTowers(new(stage, Catalog.Maps[map.MapId], difficulty, 0), progress);
    }
    private GuardianAltarAdmission WithTowers(GuardianAltarAdmission admission, GuardianAltarProgress progress) => admission with
    {
        Towers = progress.Purchases.Where(x => x.Kind == GuardianAltarShopKind.Tower)
            .Select(x => (x.Id, Level: Catalog.Shop.Single(y => y.RidableId == 1 && y.Kind == x.Kind && y.Id == x.Id).Levels[x.Tier]))
            .Where(x => x.Level.Level > 0).ToImmutableDictionary(x => x.Id, x => x.Level)
    };
    public GuardianAltarProgress? BuyLevels(GuardianAltarProgress progress, GuardianAltarShopKind kind, int id, int firstLevel, int lastLevel)
    {
        var definition = Catalog.Shop.SingleOrDefault(x => x.RidableId == 1 && x.Kind == kind && x.Id == id);
        var owned = progress.Purchases.SingleOrDefault(x => x.Kind == kind && x.Id == id);
        if (definition is null || owned is null || definition.Levels[owned.Tier].Level + 1 != firstLevel || lastLevel < firstLevel) return null;
        var target = Array.FindIndex(definition.Levels.ToArray(), x => x.Level == lastLevel);
        return target < 0 ? null : Buy(progress, kind, id, owned.Tier, target);
    }
    private static bool Eligible(GuardianAltarProgress progress, GuardianAltarAchievement achievement) =>
        progress.Stages.Count(x => x.Key is >= 3511 and <= 3522 && x.Value >= (int)achievement.Difficulty) >= achievement.ClearCount;
    public GuardianAltarProgressView DescribeProgress(GuardianAltarProgress progress) => new(progress,
        progress.Purchases.Select(purchase =>
        {
            var definition = Catalog.Shop.Single(x => x.RidableId == 1 && x.Kind == purchase.Kind && x.Id == purchase.Id);
            return new GuardianAltarPurchaseView(purchase.Kind, purchase.Id, definition.Levels[purchase.Tier].Level,
                definition.Levels.Skip(1).Take(purchase.Tier).Sum(x => x.Price));
        }).ToImmutableArray(),
        Catalog.Achievements.Where(x => Eligible(progress, x))
            .Select(x => new GuardianAltarAchievementView(x.Id, progress.ClaimedAchievements.Contains(x.Id))).ToImmutableArray());
    public GuardianAltarSpawnView DescribeSpawn(GuardianAltarSpawn spawn)
    {
        var definition = Catalog.Summons.Values.First(x => x.MonsterCode == spawn.MonsterCode && x.MinimumLevel == spawn.Level
            && x.X == spawn.X && x.Y == spawn.Y && x.Z == spawn.Z);
        return new(spawn, definition.Id, definition.Rank, definition.MaximumLevel);
    }
    public GuardianAltarProgress RecordClear(GuardianAltarProgress progress, GuardianAltarAdmission admission)
    {
        if (admission.Stage.Survival) return progress with { BestSurvival = Math.Max(progress.BestSurvival, admission.SurvivalRound) };
        var old = progress.Stages.GetValueOrDefault(admission.Stage.DungeonId, -1);
        // 70Reference clearStage's difficulty table is {3,2,1}; replaying a cleared difficulty awards no stars.
        var stars = (int)admission.Difficulty > old ? 3 - (int)admission.Difficulty : 0;
        var stages = progress.Stages.SetItem(admission.Stage.DungeonId, Math.Max(old, (int)admission.Difficulty));
        if (admission.Stage.DungeonId < 3522 && !stages.ContainsKey(admission.Stage.DungeonId + 1))
            stages = stages.Add(admission.Stage.DungeonId + 1, -1);
        var purchases = progress.Purchases;
        foreach (var item in Catalog.Shop.Where(x => x.RidableId == 1 && x.UnlockStageId == admission.Stage.DungeonId))
            if (!purchases.Any(x => x.Kind == item.Kind && x.Id == item.Id)) purchases = purchases.Add(new(item.Kind, item.Id, 0));
        return progress with { Stages = stages, GameStars = checked(progress.GameStars + stars),
            AvailableStars = checked(progress.AvailableStars + stars), Purchases = purchases, Slots = FillEmptySlots(progress.Slots, purchases) };
    }
    public GuardianAltarProgress? Buy(GuardianAltarProgress progress, GuardianAltarShopKind kind, int id, int expectedTier, int targetTier)
    {
        var purchase = progress.Purchases.SingleOrDefault(x => x.Kind == kind && x.Id == id);
        var definition = Catalog.Shop.SingleOrDefault(x => x.RidableId == 1 && x.Kind == kind && x.Id == id);
        if (purchase is null || definition is null || purchase.Tier != expectedTier || targetTier <= expectedTier
            || targetTier >= definition.Levels.Length || targetTier < 0) return null;
        var cost = checked(definition.Levels.Skip(expectedTier + 1).Take(targetTier - expectedTier).Sum(x => x.Price));
        if (progress.AvailableStars < cost) return null;
        return progress with { AvailableStars = progress.AvailableStars - cost,
            Purchases = progress.Purchases.Replace(purchase, purchase with { Tier = targetTier }) };
    }
    public GuardianAltarProgress? SetSlot(GuardianAltarProgress progress, byte slot, GuardianAltarShopKind kind, int id)
    {
        if (kind == GuardianAltarShopKind.Tower || slot >= 12 || (kind == GuardianAltarShopKind.Unit ? slot >= 6 : slot < 6)
            || kind == GuardianAltarShopKind.Skill && slot < 8) return null;
        if (id == 0) return progress with { Slots = progress.Slots.Where(x => x.Index != slot).ToImmutableArray() };
        if (!progress.Purchases.Any(x => x.Kind == kind && x.Id == id)
            || !Catalog.Shop.Single(x => x.RidableId == 1 && x.Kind == kind && x.Id == id).Settable) return null;
        var slots = progress.Slots.Where(x => x.Index != slot && !(x.Kind == kind && x.Id == id)).ToImmutableArray();
        return progress with { Slots = slots.Add(new(slot, kind, id)).OrderBy(x => x.Index).ToImmutableArray() };
    }
    public GuardianAltarProgress? ExchangeSlots(GuardianAltarProgress progress, byte first, byte second)
    {
        if (first >= 12 || second >= 12 || (first < 6) != (second < 6) || first is 6 or 7 || second is 6 or 7) return null;
        return progress with { Slots = progress.Slots.Select(x => x.Index == first ? x with { Index = second }
            : x.Index == second ? x with { Index = first } : x).OrderBy(x => x.Index).ToImmutableArray() };
    }
    public GuardianAltarProgress ResetStars(GuardianAltarProgress progress)
    {
        var purchases = progress.Purchases.Select(x => x with { Tier = 0 }).ToImmutableArray();
        return progress with { Purchases = purchases, Slots = FillEmptySlots([], purchases),
            AvailableStars = checked(progress.GameStars + progress.PurchasedStars), StarResetCount = checked(progress.StarResetCount + 1) };
    }
    public int ResetGold(GuardianAltarProgress progress) => Catalog.StarResetGold[Math.Min(progress.StarResetCount, Catalog.StarResetGold.Keys.Max())];
    public GuardianAltarAchievement? ClaimableAchievement(GuardianAltarProgress progress, int achievementId)
    {
        var reward = Catalog.Achievements.SingleOrDefault(x => x.Id == achievementId);
        return reward is not null && !progress.ClaimedAchievements.Contains(achievementId)
            && Eligible(progress, reward) ? reward : null;
    }
    public GuardianAltarShopLevel? SlotUnit(GuardianAltarProgress progress, byte slot)
    {
        var equipped = progress.Slots.SingleOrDefault(x => x.Index == slot && x.Kind == GuardianAltarShopKind.Unit);
        if (equipped is null) return null;
        var owned = progress.Purchases.Single(x => x.Kind == equipped.Kind && x.Id == equipped.Id);
        return Catalog.Shop.Single(x => x.RidableId == 1 && x.Kind == owned.Kind && x.Id == owned.Id).Levels[owned.Tier];
    }
    public ImmutableArray<GuardianAltarReward> Rewards(GuardianAltarAdmission admission) => admission.Stage.Survival
        ? admission.Stage.SurvivalRewards.Single(x => x.FirstRound <= admission.SurvivalRound && x.LastRound >= admission.SurvivalRound).Rewards
        : admission.Stage.Maps.Single(x => x.Difficulty == admission.Difficulty).Rewards;
    public ImmutableArray<GuardianAltarSpawn> PlanPhase(GuardianAltarAdmission admission, int phase, int alive)
    {
        var definition = admission.Map.Phases[phase];
        var assign = definition.Assignments.LastOrDefault(x => x.MinimumAliveMonsters >= 0 && x.MinimumAliveMonsters <= alive && x.MaximumAliveMonsters >= alive)
            ?? definition.Assignments.FirstOrDefault(x => x.MinimumAliveMonsters == -1 && x.MaximumAliveMonsters == -1);
        if (assign is null) return [];
        var action = Catalog.Actions.Single(x => x.Id == assign.ActionId);
        var result = new List<GuardianAltarSpawn>();
        var limitedCounts = new Dictionary<int, int>();
        foreach (var spawn in action.Spawns)
        {
            var unit = Catalog.Summons[spawn.SummonId];
            var count = spawn.CountLimit == 0 ? 0 : limitedCounts.GetValueOrDefault(unit.MonsterCode);
            for (long offset = spawn.DelayMilliseconds; offset < definition.DurationMilliseconds
                && (spawn.CountLimit == 0 || count < spawn.CountLimit); offset += spawn.IntervalMilliseconds, count++)
            {
                if (result.Count >= 10000) throw new InvalidDataException("Guardian altar phase exceeds the server actor safety budget.");
                result.Add(new((int)offset, unit.MonsterCode, checked((byte)unit.MinimumLevel),
                    unit.Rank == "boss" ? (byte)3 : unit.Rank == "named" ? (byte)1 : (byte)0,
                    checked((ushort)unit.X), checked((ushort)unit.Y), checked((ushort)unit.Z)));
                if (spawn.CountLimit != 0) limitedCounts[unit.MonsterCode] = count + 1;
            }
        }
        return result.OrderBy(x => x.OffsetMilliseconds).ToImmutableArray();
    }
    public int MaximumEnergy(int level) => Catalog.Configuration.GageLevels.Single(x => x.Level == level).Capacity * 1000;
    public int MaximumEnergyLevel => Catalog.Configuration.GageLevels.Max(x => x.Level);
    public int EnergyPerSecond(int level) => Catalog.Configuration.GageLevels.Single(x => x.Level == level).Rate;
    public int UpgradeEnergyCost(int level) => checked((int)(Catalog.Configuration.GageLevels.Single(x => x.Level == level).Capacity
        * Catalog.Configuration.UpgradeCostPercent) * 1000);
}
