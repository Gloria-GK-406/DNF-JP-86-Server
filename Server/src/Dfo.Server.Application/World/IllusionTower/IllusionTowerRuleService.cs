using Dfo.Server.Abstractions.World.IllusionTower;

namespace Dfo.Server.Application.World.IllusionTower;

internal sealed class IllusionTowerRuleService(IIllusionTowerContentSource content) : IIllusionTowerRuleService
{
    public bool Supports(int dungeonId) => content.Supports(dungeonId);

    public ValueTask<IllusionTowerDefinition> GetDefinitionAsync(CancellationToken cancellationToken, int dungeonId) =>
        content.GetAsync(cancellationToken, dungeonId);

    public async ValueTask<TowerRoomSeed> CreateRoomAsync(
        int level, uint seed, CancellationToken cancellationToken, int dungeonId)
    {
        var definition = await content.GetAsync(cancellationToken, dungeonId).ConfigureAwait(false);
        var room = definition.Rooms.SingleOrDefault(value => value.Level == level)
            ?? throw new ArgumentOutOfRangeException(nameof(level));
        var actors = new List<TowerActorSeed>();
        var drops = new Dfo.Server.Abstractions.World.DropRandom(unchecked((ulong)seed) ^ 0x544f574552UL);
        IReadOnlyList<int> Draw(IReadOnlyList<TowerItemChance> items) => definition.Parameters.ItemDrop == 0 ? []
            : items.Where(item => item.Rate > 0 && drops.Next(10000) <= item.Rate).Select(item => item.ItemId).ToArray();
        var monsterIndex = 0;
        var apcIndex = 0;
        foreach (var actor in room.Actors)
        {
            var actorLevel = actor.RelativeLevel ? checked(definition.BasisLevel + actor.LevelValue) : actor.LevelValue;
            if (actorLevel is < 1 or > byte.MaxValue)
                throw new InvalidDataException($"Tower map {room.MapId}: invalid actor level {actorLevel}.");
            actors.Add(new(actor.ActorType >= 5 ? apcIndex++ : monsterIndex++, actor.TemplateId,
                (byte)actorLevel, actor.ActorType, actor.BlocksClear, actor.Position) { Items = Draw(actor.Items) });
        }

        // UpstreamImplementation: DeathTowerMapLoader uses appearance-point budgets, no
        // duplicate candidates, and APC indexes starting at 64. A21 random equivalence is unverified.
        if (room.RandomApcPoints > 0)
        {
            if (room.RandomApcPositions.Count == 0 || apcIndex > 64)
                throw new InvalidDataException($"Tower map {room.MapId}: invalid random APC slots.");
            var budget = room.RandomApcPoints;
            var available = definition.RandomApcCandidates.Where(candidate => candidate.AppearancePoint > 0)
                .OrderBy(candidate => candidate.TemplateId).ToList();
            var random = new Random(unchecked((int)seed));
            var selectedCount = 0;
            foreach (var position in room.RandomApcPositions)
            {
                var eligible = available.Where(candidate => candidate.AppearancePoint <= budget).ToArray();
                if (eligible.Length == 0) break;
                var selected = eligible[random.Next(eligible.Length)];
                available.Remove(selected);
                budget -= selected.AppearancePoint;
                actors.Add(new(64 + selectedCount++, selected.TemplateId, selected.Level, 5, true, position) { Items = Draw(selected.Items) });
            }
            if (selectedCount == 0)
                throw new InvalidDataException($"Tower map {room.MapId}: no APC fits its declared random budget.");
        }
        if (!actors.Any(actor => actor.BlocksClear) || actors.Count > byte.MaxValue)
            throw new InvalidDataException($"Tower map {room.MapId}: invalid combat actor count {actors.Count}.");
        return new(level, room.MapId, seed, actors.AsReadOnly(), room.Objects);
    }
}
