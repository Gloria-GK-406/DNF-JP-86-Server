using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>
/// Pins the one random input of an ordinary dungeon entry: the layout seed that
/// DungeonVisitDomainService draws from Random.Shared and hands to the structure rule.
/// </summary>
/// <remarks>
/// A determinism seam, not a fault: every call still reaches the real rule service with the real
/// archive, and only the seed argument is replaced. The rule takes the seed from its caller for
/// exactly this reason (IDungeonStructureRuleService.ResolveAsync). Without it the start cell,
/// the room map, its actors and the room seed in START_MAP change from run to run, and the
/// actor count -- hence the frame length -- could too, which no byte mask can cover.
/// </remarks>
internal sealed class DeterministicLayouts(IDungeonStructureRuleService inner) : IDungeonStructureRuleService
{
    public static void Register(IServiceCollection services)
    {
        var descriptor = services.Last(service => service.ServiceType == typeof(IDungeonStructureRuleService));
        services.Remove(descriptor);
        services.AddSingleton<IDungeonStructureRuleService>(provider => new DeterministicLayouts(
            descriptor.ImplementationInstance as IDungeonStructureRuleService
                ?? (descriptor.ImplementationFactory is not null
                    ? (IDungeonStructureRuleService)descriptor.ImplementationFactory(provider)
                    : (IDungeonStructureRuleService)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!))));
    }

    public ValueTask<DungeonDefinition?> GetDefinitionAsync(int dungeonId, CancellationToken cancellationToken) =>
        inner.GetDefinitionAsync(dungeonId, cancellationToken);

    public ValueTask<DungeonLayout?> ResolveTopologyAsync(
        int dungeonId, DungeonEntryFacts facts, int seed, CancellationToken cancellationToken) =>
        inner.ResolveTopologyAsync(dungeonId, facts, CharacterizationTranscript.FixedLayoutSeed, cancellationToken);

    public ValueTask<DungeonLayout> PrepareLayoutAsync(DungeonLayout layout, CancellationToken cancellationToken) =>
        inner.PrepareLayoutAsync(layout, cancellationToken);

    public ValueTask<DungeonMapSelection> SelectMapAsync(
        DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken cancellationToken) =>
        inner.SelectMapAsync(layout, from, target, cancellationToken);

    public ValueTask<DungeonLayout?> ResolveAsync(
        int dungeonId, DungeonEntryFacts facts, int seed, CancellationToken cancellationToken) =>
        inner.ResolveAsync(dungeonId, facts, CharacterizationTranscript.FixedLayoutSeed, cancellationToken);
}
