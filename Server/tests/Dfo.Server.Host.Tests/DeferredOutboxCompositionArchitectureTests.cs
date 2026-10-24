using System.Reflection;
using System.Reflection.Emit;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Abstractions.Execution;
using Xunit.Abstractions;

namespace Dfo.Server.Host.Tests;

/// <summary>F-132: deferred composition remains unused by production, rather than being declared repaired.</summary>
public sealed class DeferredOutboxCompositionArchitectureTests(ITestOutputHelper output)
{
    private static readonly MethodInfo Forbidden = typeof(GameplayOutbox).GetMethods()
        .Single(method => method.Name == nameof(GameplayOutbox.ComposeAsync) && method.IsGenericMethodDefinition);
    private static readonly IReadOnlyDictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);
    private const BindingFlags Members = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void No_production_method_references_the_deferred_outbox_composition_API()
    {
        var assemblies = GameplayJobHostClosure.ProductionAssemblies(Assembly.Load("Dfo.Server.Host"));
        var names = assemblies.Select(assembly => assembly.GetName().Name!).ToHashSet(StringComparer.Ordinal);
        output.WriteLine("scanned assemblies: " + string.Join(", ", names.Order(StringComparer.Ordinal)));
        Assert.All(new[] { "Dfo.Server.Application", "Dfo.Server.Protocol", "Dfo.Server.Host", "Dfo.Server.GameServer",
            "Dfo.Server.HttpApi", "Dfo.Server.GmWeb" }, name => Assert.Contains(name, names));
        // Utilities has no source or references today. GetTypes intentionally fails rather than skipping an unloadable type.
        var violations = FindReferences(assemblies.SelectMany(assembly => assembly.GetTypes()));
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void The_same_detector_rejects_calls_in_async_bodies_and_constructors()
    {
        var violations = FindReferences(typeof(BadCalls).GetNestedTypes(Members).Prepend(typeof(BadCalls)));
        Assert.Contains(violations, entry => entry.Contains("MoveNext", StringComparison.Ordinal));
        Assert.Contains(violations, entry => entry.Contains(".ctor", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unrelated_method_named_ComposeAsync_is_allowed()
    {
        Assert.Empty(FindReferences([typeof(UnrelatedCalls)]));
    }

    private static IReadOnlyList<string> FindReferences(IEnumerable<Type> types)
    {
        var violations = new List<string>();
        foreach (var type in types)
        {
            var methods = type.GetMethods(Members).Cast<MethodBase>().Concat(type.GetConstructors(Members));
            if (type.TypeInitializer is { } initializer) methods = methods.Append(initializer).Distinct();
            foreach (var method in methods)
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il is null) continue;
                var typeArguments = type.IsGenericType ? type.GetGenericArguments() : null;
                var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
                for (var offset = 0; offset < il.Length;)
                {
                    var code = il[offset] == 0xFE ? (short)(0xFE00 | il[offset + 1]) : il[offset];
                    var op = Codes[code];
                    offset += op.Size;
                    var operand = offset;
                    offset += op.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, operand),
                        _ => 4,
                    };
                    if (op.OperandType is not (OperandType.InlineMethod or OperandType.InlineTok)) continue;
                    var token = BitConverter.ToInt32(il, operand);
                    MemberInfo? target;
                    try { target = method.Module.ResolveMember(token, typeArguments, methodArguments); }
                    catch (Exception error) when (error is ArgumentException or BadImageFormatException)
                    {
                        throw new InvalidOperationException($"Cannot resolve {method.Module.Assembly.GetName().Name}:{type.FullName}.{method.Name} token {token:X8}", error);
                    }
                    if (target is not MethodInfo called) continue;
                    if (called.IsGenericMethod) called = called.GetGenericMethodDefinition();
                    if (called.Module == Forbidden.Module && called.MetadataToken == Forbidden.MetadataToken)
                        violations.Add($"{method.Module.Assembly.GetName().Name}:{type.FullName}.{method.Name} references {Forbidden.DeclaringType!.FullName}.{Forbidden.Name}");
                }
            }
        }
        return violations;
    }

    // These methods are inspected only; executing them would violate the policy under test.
    private sealed class BadCalls
    {
        public BadCalls(GameplayOutbox outbox) => _ = outbox.ComposeAsync<int>([], Empty, default);
        private static async ValueTask InAsyncBody(GameplayOutbox outbox) => await outbox.ComposeAsync<int>([], Empty, default);
        private static ValueTask<IReadOnlyList<OutboundEntry>> Empty(int recipient, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<OutboundEntry>>([]);
    }
    private static class UnrelatedCalls
    {
        private static ValueTask ComposeAsync() => ValueTask.CompletedTask;
        private static ValueTask Call() => ComposeAsync();
    }
}
