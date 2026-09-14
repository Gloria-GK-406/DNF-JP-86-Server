using System.Reflection;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Application.Parties;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Parties;

public sealed class PartyBoundaryArchitectureTests
{
    private static readonly Type[] ForbiddenTypes =
    [
        typeof(SelectedSessionIdentity),
        typeof(NetworkConnectionId),
    ];

    private static readonly string[] ForbiddenMemberNames =
    [
        "Session",
        "ConnectionId",
        "ConnectionGeneration",
        "ListenerPort",
    ];

    [Fact]
    public void PublicPartyContractsContainNoTransportOrSessionIdentity()
    {
        var contractTypes = typeof(PartySnapshot).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(PartySnapshot).Namespace
                && (type.IsPublic || type.IsNestedPublic))
            .Concat(typeof(IPartyService).Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(IPartyService).Namespace
                    && (type.IsPublic || type.IsNestedPublic)))
            .Distinct()
            .ToArray();

        var violations = contractTypes
            .SelectMany(VisibleSurface)
            .Where(surface => ContainsForbidden(surface.Type)
                || ForbiddenMemberNames.Contains(surface.Name, StringComparer.Ordinal))
            .Select(surface => $"{surface.Owner.FullName}.{surface.Name}: {surface.Type.FullName}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void PartyServiceAndItsPrivateStateContainNoTransportOrSessionIdentity()
    {
        var types = typeof(PartyService).GetNestedTypes(BindingFlags.NonPublic)
            .Prepend(typeof(PartyService))
            .ToArray();
        var violations = types
            .SelectMany(type => type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => (Owner: type, field.Name, Type: field.FieldType)))
            .Where(surface => ContainsForbidden(surface.Type)
                || ForbiddenMemberNames.Contains(surface.Name, StringComparer.Ordinal))
            .Select(surface => $"{surface.Owner.FullName}.{surface.Name}: {surface.Type.FullName}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static IEnumerable<(Type Owner, string Name, Type Type)> VisibleSurface(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            yield return (type, property.Name, property.PropertyType);
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            yield return (type, field.Name, field.FieldType);
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                     .Where(method => !method.IsSpecialName))
        {
            yield return (type, $"{method.Name}.return", method.ReturnType);
            foreach (var parameter in method.GetParameters())
                yield return (type, $"{method.Name}.{parameter.Name}", parameter.ParameterType);
        }
    }

    private static bool ContainsForbidden(Type type)
    {
        if (ForbiddenTypes.Contains(type))
            return true;
        if (type.HasElementType)
            return type.GetElementType() is { } element && ContainsForbidden(element);
        return type.IsGenericType && type.GetGenericArguments().Any(ContainsForbidden);
    }
}
