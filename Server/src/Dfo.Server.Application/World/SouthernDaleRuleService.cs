using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.World;

internal sealed class SouthernDaleRuleService(ISouthernDaleContentSource content) : ISouthernDaleRuleService
{
    internal const int DungeonId = 100;
    internal const int ActivityId = 2330;
    public bool Supports(int dungeonId) => dungeonId == DungeonId;
    public int EventId => ActivityId;
    public int TicketItemId => 2680738;
    // Server activity identity follows the DGN schedule; no native link syntax is asserted.
    public string ActivityLink => "";
    public EventConfiguration InitialActivity => new(0, "permanent", true, "南部溪谷", "南部溪谷",
        "每次入场消耗一张神秘邀请函，通关获得封印钥匙。", "", "", DateTimeOffset.UnixEpoch,
        new DateTimeOffset(2037, 12, 31, 23, 59, 59, TimeSpan.FromHours(8)));
    public ValueTask<SouthernDaleDefinition> DefinitionAsync(CancellationToken ct) => content.ReadAsync(ct);
}
