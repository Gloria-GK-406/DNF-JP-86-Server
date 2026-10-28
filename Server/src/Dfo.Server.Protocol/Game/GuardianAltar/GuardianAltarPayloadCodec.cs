using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Protocol.Game.GuardianAltar;

/// <summary>A21 packed layouts. Source pointers and differences from aligned 70 structs live in protocol pages.</summary>
internal static class GuardianAltarPayloadCodec
{
    private static byte[] Write(Action<BinaryWriter> action)
    { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); action(writer); return stream.ToArray(); }
    public static byte[] Open(GuardianAltarProgress? progress, GuardianAltarCatalog catalog) => Write(w =>
    { w.Write((byte)1); w.Write(progress is null ? 1 : 0); if (progress is null) return; w.Write(1u); Profile(w, progress, catalog); });
    public static byte[] ProfileUpdate(GuardianAltarProgress progress, GuardianAltarCatalog catalog) => Write(w =>
    { w.Write((byte)0); Profile(w, progress, catalog); });
    private static void Profile(BinaryWriter w, GuardianAltarProgress p, GuardianAltarCatalog c)
    {
        w.Write(1); w.Write(p.FreeTickets); w.Write(p.ExtraTickets); w.Write(p.GameStars); w.Write(p.PurchasedStars); w.Write(p.AvailableStars);
        w.Write(checked((short)p.BestSurvival)); w.Write(checked((short)p.StarResetCount));
        w.Write(0); w.Write((byte)0);
        w.Write((uint)p.Stages.Count);
        foreach (var (stage, difficulty) in p.Stages.OrderBy(x => x.Key)) { w.Write(stage); w.Write(difficulty); }
        foreach (var kind in Enum.GetValues<GuardianAltarShopKind>())
        {
            var items = p.Purchases.Where(x => x.Kind == kind).OrderBy(x => x.Id).ToArray(); w.Write((uint)items.Length);
            foreach (var item in items) Purchase(w, item, c);
        }
        Slots(w, p, GuardianAltarShopKind.Unit); Slots(w, p, GuardianAltarShopKind.Skill);
        var achievements = Eligible(p, c); w.Write((uint)achievements.Length);
        foreach (var reward in achievements) { w.Write(reward.Id); w.Write(p.ClaimedAchievements.Contains(reward.Id) ? (byte)1 : (byte)0); }
    }
    private static void Purchase(BinaryWriter w, GuardianAltarPurchase item, GuardianAltarCatalog c)
    {
        var definition = c.Shop.Single(x => x.RidableId == 1 && x.Kind == item.Kind && x.Id == item.Id);
        w.Write((int)item.Kind); w.Write(item.Id); w.Write(checked((short)definition.Levels[item.Tier].Level));
        w.Write(checked((short)definition.Levels.Skip(1).Take(item.Tier).Sum(x => x.Price)));
    }
    private static void Slots(BinaryWriter w, GuardianAltarProgress p, GuardianAltarShopKind kind)
    {
        w.Write(6u);
        for (byte index = 0; index < 6; index++)
        {
            w.Write(kind == GuardianAltarShopKind.Skill && index < 2 ? (byte)1 : (byte)0);
            w.Write(kind == GuardianAltarShopKind.Unit ? 1 : 2); w.Write(index);
            w.Write(p.Slots.SingleOrDefault(x => x.Index == index + (kind == GuardianAltarShopKind.Skill ? 6 : 0))?.Id ?? 0);
        }
    }
    public static byte[] StageInfo(GuardianAltarAdmission admission) => Write(w => { w.Write(admission.Stage.DungeonId); w.Write((int)admission.Difficulty); });
    public static byte[] Map(DungeonRunState run, GuardianAltarAdmission a) => Write(w =>
    {
        // A21 initializes the altar scene on wire round 1, including ordinary stages.
        w.Write(a.Stage.Survival ? 1 : 0); w.Write(a.Stage.Survival ? a.SurvivalRound : 1); w.Write(a.Map.MapId);
        w.Write(a.Map.Phases.Sum(x => x.DurationMilliseconds)); w.Write(a.BasisLevel);
        w.Write((uint)a.Map.Phases.Length); var offset = 0;
        for (var index = 0; index < a.Map.Phases.Length; index++)
        { var phase = a.Map.Phases[index]; w.Write(index); w.Write(offset); w.Write(phase.Kind == "normal" ? 0 : phase.Kind == "named" ? 1 : 2); offset += phase.DurationMilliseconds; }
        var towers = run.CurrentCellState.Actors.Where(x => (!a.Stage.Survival || a.SurvivalRound == 1)
            && !x.Actor.IsHostile).ToArray();
        w.Write((uint)towers.Length);
        foreach (var tower in towers)
        {
            w.Write(tower.Actor.Declaration?.RecordIndex ?? 0); w.Write(tower.Sequence);
            w.Write(checked((ushort)tower.Actor.Code)); w.Write(tower.Actor.Level);
            w.Write(tower.Actor.ActorType); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
        }
    });
    public static byte[] Spawn(ushort sequence, GuardianAltarSpawn spawn, GuardianAltarCatalog c) => Write(w =>
    {
        var definition = c.Summons.Values.First(x => x.MonsterCode == spawn.MonsterCode && x.MinimumLevel == spawn.Level
            && x.X == spawn.X && x.Y == spawn.Y && x.Z == spawn.Z);
        w.Write(sequence); w.Write(definition.Id); w.Write((byte)1); w.Write((byte)0); w.Write((short)0);
        w.Write(definition.Rank == "normal" ? 0 : definition.Rank == "named" ? 1 : 2); w.Write(definition.MonsterCode);
        w.Write((short)spawn.Level); w.Write((short)0); w.Write(definition.MaximumLevel);
        w.Write((int)spawn.X); w.Write((int)spawn.Y); w.Write((int)spawn.Z);
    });
    public static byte[] Response(GuardianAltarRequest r, int result, GuardianAltarProgress? p, GuardianAltarRunState? run) => Write(w =>
    {
        w.Write((byte)1); w.Write(result);
        switch (r.Operation)
        {
            case GuardianAltarOperation.Buy:
                w.Write(r.RidableId); w.Write(r.Kind); w.Write(r.ItemId); w.Write((short)r.FirstLevel); w.Write((short)r.LastLevel); w.Write(p?.AvailableStars ?? 0); break;
            case GuardianAltarOperation.SetSlot:
                w.Write(r.RidableId); w.Write(r.Attach ? (byte)1 : (byte)0); w.Write(r.Kind); w.Write(r.Slot); w.Write(r.ItemId); break;
            case GuardianAltarOperation.UpgradeEnergy:
                w.Write(1); w.Write(run?.EnergyLevel ?? r.EnergyLevel); w.Write(run?.Energy ?? 0); break;
            case GuardianAltarOperation.Summon:
                w.Write(r.Slot); w.Write(r.ItemId); w.Write(run?.Energy ?? 0); break;
            case GuardianAltarOperation.Exchange:
                w.Write(r.RidableId); w.Write(r.Kind); w.Write(r.Slot); w.Write(r.ItemId); w.Write(r.SecondKind); w.Write(r.SecondSlot); w.Write(r.SecondItemId); break;
            case GuardianAltarOperation.Pause: w.Write(r.Attach ? (byte)1 : (byte)0); break;
            case GuardianAltarOperation.Achievement: w.Write(r.ItemId); break;
            case GuardianAltarOperation.Reset: w.Write(0); break;
        }
    });
    private static GuardianAltarAchievement[] Eligible(GuardianAltarProgress p, GuardianAltarCatalog c) => c.Achievements.Where(x =>
        p.Stages.Count(stage => stage.Key is >= 3511 and <= 3522 && stage.Value >= (int)x.Difficulty) >= x.ClearCount).ToArray();
    public static byte[] Result(GuardianAltarActionResult action, GuardianAltarProgress p, GuardianAltarCatalog c) => Write(w =>
    {
        var run = action.State;
        var a = run.Admission; var won = run.Status is GuardianAltarStatus.Won or GuardianAltarStatus.BetweenRounds;
        w.Write(a.Stage.Survival ? 1 : 0); w.Write(a.Stage.Survival ? a.SurvivalRound : 0); w.Write(p.BestSurvival);
        w.Write(action.NewSurvivalRecord ? (byte)1 : (byte)0); w.Write(a.Stage.DungeonId); w.Write((int)a.Difficulty); w.Write(action.StarDelta);
        var rewards = !won ? [] : a.Stage.Survival ? a.Stage.SurvivalRewards.Single(x => x.FirstRound <= a.SurvivalRound && x.LastRound >= a.SurvivalRound).Rewards
            : a.Stage.Maps.Single(x => x.Difficulty == a.Difficulty).Rewards;
        w.Write((uint)rewards.Length); foreach (var reward in rewards) { w.Write(0); w.Write(reward.ItemId); w.Write(reward.Count); }
        w.Write((uint)action.UnlockedPurchases.Length); foreach (var item in action.UnlockedPurchases) Purchase(w, item, c);
        var achievements = Eligible(p, c); w.Write((uint)achievements.Length); foreach (var achievement in achievements) { w.Write(achievement.Id); w.Write(p.ClaimedAchievements.Contains(achievement.Id) ? (byte)1 : (byte)0); }
    });
    public static byte[] Eplp(GuardianAltarRunState run, bool hasTickets) => Write(w =>
    { w.Write(run.Status == GuardianAltarStatus.Won ? 3 : 0); w.Write(hasTickets ? (byte)1 : (byte)0); });
}
