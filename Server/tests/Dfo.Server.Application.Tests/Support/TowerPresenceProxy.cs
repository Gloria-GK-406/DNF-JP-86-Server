using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Support;

internal class TowerPresenceProxy : DispatchProxy
{
    public HashSet<CharacterId> Members { get; } = [];

    /// <summary>Which tower the registry names for the members; a Boss tower seat is invisible to this illusion tower.</summary>
    public DungeonParticipationMode Mode { get; set; } = DungeonParticipationMode.IllusionTower;

    public static IIllusionTowerDomainService Create(out TowerPresenceProxy control)
    {
        var service = DispatchProxy.Create<IIllusionTowerDomainService, TowerPresenceProxy>();
        control = (TowerPresenceProxy)(object)service;
        return service;
    }

    /// <summary>
    /// Registers this tower and makes the occupancy registry agree with it, the way a real tower
    /// entry claims participation: its members are in a dungeon for every "in a dungeon" check.
    /// </summary>
    public void Register(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddSingleton((IIllusionTowerDomainService)(object)this);
        services.AddSingleton<IDungeonParticipationDomainService>(provider => new Participation(
            ActivatorUtilities.CreateInstance<Dfo.Server.Application.World.DungeonParticipationDomainService>(provider), this));
    }

    private readonly Guid runId = Guid.NewGuid();

    private sealed class Participation(IDungeonParticipationDomainService inner, TowerPresenceProxy tower) : IDungeonParticipationDomainService
    {
        public DungeonParticipationOwner? Find(CharacterId character) => tower.Members.Contains(character)
            ? new(tower.Mode, tower.runId) : inner.Find(character);
        public bool TryClaim(IReadOnlyList<CharacterId> characters, DungeonParticipationOwner owner) => inner.TryClaim(characters, owner);
        public void Release(CharacterId character, DungeonParticipationOwner expected) => inner.Release(character, expected);
        public void ReleaseCommitted(CharacterId character, DungeonParticipationOwner expected) => inner.ReleaseCommitted(character, expected);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        nameof(IIllusionTowerDomainService.IsInTower) => Mode == DungeonParticipationMode.IllusionTower && Members.Contains((CharacterId)args![0]!),
        nameof(IIllusionTowerDomainService.Find) => null,
        _ => throw new NotSupportedException(targetMethod?.Name),
    };
}
