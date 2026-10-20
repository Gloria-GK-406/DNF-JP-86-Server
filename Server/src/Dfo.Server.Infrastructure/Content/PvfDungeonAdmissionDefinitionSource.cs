using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

// The ordinary source may be replaced by an embedding host or a synthetic fixture.
// Honor that content boundary rather than unexpectedly opening the default archive.
internal sealed class PvfDungeonAdmissionDefinitionSource(IDungeonDefinitionSource ordinary,
    PvfDungeonDefinitionSource pvf) : IDungeonAdmissionDefinitionSource
{
    public async ValueTask<DungeonAdmissionDefinition?> GetAdmissionAsync(int id, CancellationToken ct) => ReferenceEquals(ordinary, pvf)
        ? await pvf.GetAdmissionAsync(id, ct).ConfigureAwait(false)
        : await ordinary.GetAsync(id, ct).ConfigureAwait(false) is { } d ? DungeonAdmissionDefinition.From(d) : null;
}
