using System.Reflection;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>A membership with nobody in it: every lookup finds nothing, and anything else is unexpected.</summary>
/// <remarks>For the dungeon runs, towers and participation a character-side test needs present but empty.</remarks>
internal class NobodyThere : DispatchProxy
{
    public static T Create<T>() where T : class => DispatchProxy.Create<T, NobodyThere>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "Find" => null,
        "IsInTower" => false,
        _ => throw new NotSupportedException(targetMethod?.Name),
    };
}
