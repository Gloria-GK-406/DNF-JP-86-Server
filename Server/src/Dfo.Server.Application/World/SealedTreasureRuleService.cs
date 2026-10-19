using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.World;

internal sealed class SealedTreasureRuleService(IDungeonDefinitionSource? content = null) : ISealedTreasureRuleService
{
    internal const int DungeonId = 3557;
    internal const int ActivityId = 2331;
    private static readonly MazeCell Center = new(2, 2);
    // ContentStatic pairs; these are named requirements, not native PARTY SYMBOL bit encoding.
    private static readonly (int Monster, int Stone, MazeCell Road)[] Seals = [
        (110348, 54291, new(1, 2)), (110349, 54292, new(2, 3)),
        (110350, 54293, new(3, 2)), (110351, 54294, new(2, 1))];

    public bool Supports(int dungeonId) => dungeonId == DungeonId;
    public int EventId => ActivityId;
    public EventConfiguration InitialActivity => new(0, "permanent", true, "封印的藏宝地", "封印的藏宝地",
        "单人探索封印，击败对应元素精灵打开通路；入场优先消耗免费导航人偶。", "", "", DateTimeOffset.UnixEpoch,
        new DateTimeOffset(2037, 12, 31, 23, 59, 59, TimeSpan.FromHours(8)));

    public async ValueTask<DungeonDefinition> DefinitionAsync(CancellationToken ct)
    {
        var definition = content is null ? null : await content.GetAsync(DungeonId, ct).ConfigureAwait(false);
        if (definition is null || !definition.IsSpiciesDungeon || definition.ConsumesFatigue
            || definition.PartyLimit != 1 || !definition.ProhibitsPractice
            || definition.SupportedDifficulties is not { Count: 1 } tiers || tiers[0] != 2
            || definition.RequiredItems.Count == 0 || definition.EventRequiredItems.Count == 0)
            throw new InvalidDataException("Sealed Treasure content is incomplete or incompatible.");
        return definition;
    }

    public bool AllowsTraversal(int dungeonId, IEnumerable<int> killedMonsters, MazeCell from, MazeCell target)
    {
        if (!Supports(dungeonId)) return true;
        var road = target == Center ? from : from == Center ? target : (MazeCell?)null;
        var required = Seals.FirstOrDefault(x => x.Road == road).Monster;
        return required == 0 || killedMonsters.Contains(required);
    }

    public bool AllowsObjectDestruction(int dungeonId, IEnumerable<int> killedMonsters, int objectCode)
    {
        if (!Supports(dungeonId)) return true;
        var required = Seals.FirstOrDefault(x => x.Stone == objectCode).Monster;
        return required == 0 || killedMonsters.Contains(required);
    }

    public bool RequiresDestroyedObject(int dungeonId, int objectCode) => Supports(dungeonId) && objectCode == 15150;
}
