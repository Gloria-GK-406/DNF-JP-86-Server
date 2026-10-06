using System.Reflection;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Support;

internal class TowerPresenceProxy : DispatchProxy
{
    public HashSet<CharacterId> Members { get; } = [];

    public static IIllusionTowerDomainService Create(out TowerPresenceProxy control)
    {
        var service = DispatchProxy.Create<IIllusionTowerDomainService, TowerPresenceProxy>();
        control = (TowerPresenceProxy)(object)service;
        return service;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        nameof(IIllusionTowerDomainService.IsInTower) => Members.Contains((CharacterId)args![0]!),
        nameof(IIllusionTowerDomainService.Find) => null,
        _ => throw new NotSupportedException(targetMethod?.Name),
    };
}
