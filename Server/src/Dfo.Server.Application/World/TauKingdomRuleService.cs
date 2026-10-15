using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.World;

internal sealed class TauKingdomRuleService(ITauKingdomContentSource source) : ITauKingdomRuleService
{
    public bool Supports(int dungeonId) => dungeonId == 3700;
    public int EventId => 2215;
    public int TicketItemId => 690001556;
    // ClientStatic: the generic event parser requires tag/int/int. Admission checks the event ID only.
    public string ActivityLink => "taukingdom/0/0";
    public EventConfiguration InitialActivity => new(0, "permanent", true, "牛头怪乐园", "牛头怪乐园",
        "单人牛头怪乐园，每次入场消耗一张通行证。", "", "",
        DateTimeOffset.UnixEpoch, new DateTimeOffset(2037, 12, 31, 23, 59, 59, TimeSpan.FromHours(8)));
    public ValueTask<TauKingdomDefinition> DefinitionAsync(CancellationToken ct) => source.ReadAsync(ct);
}
