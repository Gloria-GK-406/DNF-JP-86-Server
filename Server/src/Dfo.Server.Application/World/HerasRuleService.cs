using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.World;

internal sealed class HerasRuleService(IHerasContentSource source) : IHerasRuleService
{
    internal const int DungeonId = 3900;
    internal const int ActivityId = 2367;
    public bool Supports(int dungeonId) => dungeonId == DungeonId;
    public int EventId => ActivityId;
    public int DailyEntries => 3;
    public EventConfiguration InitialActivity => new(0, "permanent", true, "赫拉斯研究所", "赫拉斯研究所",
        "50级以上单人挑战，每游戏日基础3次，不消耗疲劳。", "", "",
        DateTimeOffset.UnixEpoch, new DateTimeOffset(2037, 12, 31, 23, 59, 59, TimeSpan.FromHours(8)));
    public ValueTask<HerasDefinition> DefinitionAsync(CancellationToken ct) => source.ReadAsync(ct);
}
