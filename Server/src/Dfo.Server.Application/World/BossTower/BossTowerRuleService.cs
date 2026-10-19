using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.World.BossTower;

internal sealed class BossTowerRuleService(IBossTowerContentSource content) : IBossTowerRuleService
{
    internal const int DungeonId = 3505;
    internal const int ActivityId = 2329;
    public bool Supports(int dungeonId) => dungeonId == DungeonId;
    public int EventId => ActivityId;
    public int TicketItemId => 2749110;
    public int PartyLimit => 4;
    public TimeSpan LoadingTimeout => TimeSpan.FromSeconds(120);
    public TimeSpan ResultTimeout => TimeSpan.FromSeconds(20);
    public bool CanTrade(int npcId, int shopId) => npcId == 1010 && shopId == 1118;
    public bool IsNextStageTarget(int currentStage, byte targetX, byte targetY) =>
        // The A21 tower sends (-1,-1) as a next-stage marker, not a maze cell.
        targetX == byte.MaxValue && targetY == byte.MaxValue
        || targetY == 0 && targetX == (currentStage + 1) % 2;
    public EventConfiguration InitialActivity => new(0, "permanent", true, "领主之塔", "领主之塔",
        "60级以上可挑战领主之塔；每次消耗一张入场券，不消耗疲劳。", "", "", DateTimeOffset.UnixEpoch,
        new DateTimeOffset(2037, 12, 31, 23, 59, 59, TimeSpan.FromHours(8)));
    public async ValueTask<BossTowerDefinition> DefinitionAsync(CancellationToken ct)
    {
        var d = await content.GetAsync(ct).ConfigureAwait(false);
        if (d.Admission.DungeonId != DungeonId || d.Stages.Count != 8
            || d.Stages.Where((x, i) => x.Stage != i + 1 || x.Maps.Count == 0
                || x.Maps.Any(m => m.Weight <= 0) || x.Maps.Sum(m => (long)m.Weight) != 10000).Any()
            || d.Rewards.ExperienceWeights.Count != 8 || d.Rewards.CardCounts.Count != 8
            || d.Rewards.ItemProbabilityOperands.Count != 3
            || d.Rewards.ExperienceWeights.Any(x => !double.IsFinite(x) || x < 0)
            || d.Rewards.ItemProbabilityOperands.Any(x => x < 0) || d.Rewards.CardCounts.Any(x => x < 0)
            || !double.IsFinite(d.Rewards.GoldWeight) || d.Rewards.GoldWeight < 0
            || !double.IsFinite(d.Rewards.MonsterExperienceWeight) || d.Rewards.MonsterExperienceWeight < 0
            || !double.IsFinite(d.Rewards.ApcExperienceWeight) || d.Rewards.ApcExperienceWeight < 0 || d.MaximumClearItemCount is < 0 or > 10)
            throw new InvalidDataException("Boss tower content is incomplete or incompatible.");
        return d;
    }
    public async ValueTask<BossTowerRoomSeed> CreateRoomAsync(int stage, uint seed, CancellationToken ct)
    {
        var d = await DefinitionAsync(ct).ConfigureAwait(false);
        if (stage < 1 || stage > d.Stages.Count) throw new ArgumentOutOfRangeException(nameof(stage));
        // Explicit seed makes a selected room stable within a run; equivalence with native PRNG is not claimed.
        var random = new Random(unchecked((int)seed));
        var roll = random.Next(10000);
        BossTowerMapCandidate? chosen = null;
        foreach (var map in d.Stages[stage - 1].Maps)
        {
            if (roll < map.Weight) { chosen = map; break; }
            roll -= map.Weight;
        }
        var selected = chosen ?? throw new InvalidDataException("Boss tower map weights do not cover the draw.");
        var actors = selected.Room.Actors.Select((actor, index) =>
        {
            var level = actor.RelativeLevel ? checked(d.BasisLevel + actor.LevelValue) : actor.LevelValue;
            if (level is < 1 or > 255) throw new InvalidDataException("Invalid boss tower monster level.");
            return new TowerActorSeed(index, actor.TemplateId, (byte)level, actor.ActorType, actor.BlocksClear, actor.Position);
        }).ToArray();
        if (actors.Length > 255 || !actors.Any(x => x.BlocksClear)) throw new InvalidDataException("Invalid boss tower combat actors.");
        return new(new(stage, selected.Room.MapId, seed, Array.AsReadOnly(actors), selected.Room.Objects), selected.NpcIds);
    }
}
