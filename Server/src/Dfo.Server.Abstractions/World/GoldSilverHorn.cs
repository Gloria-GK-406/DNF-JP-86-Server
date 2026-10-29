using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Abstractions.World;

public sealed record GoldSilverHornContent(int DungeonId, int DailyEntries);
public interface IGoldSilverHornContentSource
{
    GoldSilverHornContent Load();
}
public sealed record GoldSilverHornQuotaSnapshot(int DungeonId, DateOnly Period, int Used, int Maximum)
{
    public int Remaining => Math.Max(0, Maximum - Used);
}
public interface IGoldSilverHornRuleService
{
    int DungeonId { get; }
    bool Supports(int dungeonId);
    int EventId { get; }
    int MaximumEntries { get; }
    EventConfiguration InitialActivity { get; }
    ValueTask<DungeonDefinition> DefinitionAsync(CancellationToken ct);
}
public interface IGoldSilverHornDomainService
{
    bool Supports(int dungeonId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
    ValueTask<GoldSilverHornQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken ct);
}
