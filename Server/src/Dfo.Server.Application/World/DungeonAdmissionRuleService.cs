using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.World;

internal sealed class DungeonAdmissionRuleService(IDungeonAdmissionDefinitionSource source) : IDungeonAdmissionRuleService
{
    public ValueTask<DungeonAdmissionDefinition?> GetAsync(int dungeonId, CancellationToken ct) => source.GetAdmissionAsync(dungeonId, ct);
}
