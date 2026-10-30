using System.Reflection;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// Rule inputs enter through rule services, and the contract layer carries no rule logic. Both are
/// judged from the production types themselves: content sources are found by what their PVF or
/// channel-script implementations read, not by an interface-name suffix.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ContentBoundaryArchitectureTests
{
    private static readonly Assembly Abstractions = typeof(Dfo.Server.Abstractions.IServerModule).Assembly;
    private static readonly Assembly Infrastructure = typeof(Dfo.Server.Infrastructure.Content.ChannelScript).Assembly;
    private static readonly Assembly[] Consumers =
    [
        typeof(Dfo.Server.Application.ApplicationModule).Assembly,
        typeof(Dfo.Server.Protocol.ProtocolModule).Assembly,
        typeof(Dfo.Server.GmWeb.GmWebModule).Assembly,
        typeof(Dfo.Server.HttpApi.HttpApiModule).Assembly,
        typeof(Dfo.Server.GameServer.GameServerHostingExtensions).Assembly,
        Assembly.Load("Dfo.Server.Host"),
    ];

    [Fact]
    public void Abstractions_holds_contracts_not_rules()
    {
        Assert.Empty(RuleLogicIn(Abstractions.GetTypes()));
    }

    [Fact]
    public void Content_sources_are_injected_only_into_rule_services()
    {
        var sources = ContentSources(Infrastructure.GetTypes());
        Assert.NotEmpty(sources);
        Assert.Empty(ContentReaders(Consumers.SelectMany(LoadableTypes), sources).Select(type => type.FullName));
    }

    [Fact]
    public void The_judgements_reject_their_counterexamples()
    {
        Assert.Equal(["PretendRules", "PretendRuleService"],
            RuleLogicIn([typeof(PretendRules), typeof(PretendRuleService), typeof(PretendDataRules), typeof(IPretendContract)]));
        var sources = ContentSources([typeof(PretendPvfSource), typeof(PretendRandomSource)]);
        Assert.Equal([typeof(IPretendContentSource)], sources);
        Assert.Equal([typeof(PretendDomainService)],
            ContentReaders([typeof(PretendDomainService), typeof(PretendRuleService), typeof(PretendRandomUser)], sources));
    }

    /// <summary>Rule logic by shape: a non-record class named as pure rules or as a rule service.</summary>
    private static IEnumerable<string> RuleLogicIn(IEnumerable<Type> types) => types
        .Where(type => type.IsPublic || type.IsNestedPublic)
        .Where(type => type.IsClass && type.GetMethod("<Clone>$") is null
            && (type.Name.EndsWith("Rules", StringComparison.Ordinal) || type.Name.EndsWith("RuleService", StringComparison.Ordinal)))
        .Select(type => type.Name);

    /// <summary>Interfaces implemented by a type whose constructor takes a PVF archive or the channel script.</summary>
    private static IReadOnlyList<Type> ContentSources(IEnumerable<Type> implementations) => implementations
        .Where(type => type.IsClass && !type.IsAbstract)
        .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Any(constructor => constructor.GetParameters().Any(parameter => ReadsContent(parameter.ParameterType))))
        .SelectMany(type => type.GetInterfaces())
        .Where(contract => contract.Assembly == Abstractions || contract.DeclaringType?.Name == nameof(ContentBoundaryArchitectureTests))
        .Distinct()
        .ToArray();

    private static bool ReadsContent(Type parameter) =>
        parameter.Name is "PvfArchiveProvider" or "FileChannelScriptSource" or nameof(PretendArchive);

    private static IEnumerable<Type> ContentReaders(IEnumerable<Type> types, IReadOnlyList<Type> sources) => types
        .Where(type => type.IsClass && !type.IsAbstract)
        .Where(type => !IsRuleOwner(type) && !(type.DeclaringType is { } owner && IsRuleOwner(owner))
            && !typeof(Dfo.Server.Abstractions.IServerModule).IsAssignableFrom(type))
        .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Any(constructor => constructor.GetParameters().Any(parameter => sources.Contains(parameter.ParameterType))));

    private static bool IsRuleOwner(Type type) =>
        type.Name.EndsWith("RuleService", StringComparison.Ordinal) || type.Name.EndsWith("Policy", StringComparison.Ordinal);

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error) { return error.Types.OfType<Type>(); }
    }

    public sealed class PretendArchive;
    public interface IPretendContentSource;
    public interface IPretendRandomSource;
    public sealed class PretendPvfSource(PretendArchive archive) : IPretendContentSource { public PretendArchive Archive => archive; }
    public sealed class PretendRandomSource : IPretendRandomSource;
    public sealed class PretendDomainService(IPretendContentSource content) { public IPretendContentSource Content => content; }
    public sealed class PretendRuleService(IPretendContentSource content) { public IPretendContentSource Content => content; }
    public sealed class PretendRandomUser(IPretendRandomSource random) { public IPretendRandomSource Random => random; }
    public static class PretendRules;
    public sealed record PretendDataRules(int Limit);
    public interface IPretendContract { int Value(); }
}
