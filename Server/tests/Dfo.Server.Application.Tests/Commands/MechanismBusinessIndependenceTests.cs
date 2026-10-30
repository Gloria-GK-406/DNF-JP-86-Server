using System.Reflection;
using System.Reflection.Emit;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.Tests.Commands;

/// <summary>
/// #247: the command mechanism (bus, registry, request context, work units, failure audit) learns what a
/// command needs only from its registration and unit declarations. No mechanism type may reference an HTTP
/// or GM operation, in its signatures or in any method body, compiler-generated state machines included.
/// </summary>
public sealed class MechanismBusinessIndependenceTests
{
    private static readonly string[] MechanismNamespaces = ["Dfo.Server.Application.Commands", "Dfo.Server.Application.Transactions"];
    private static readonly string[] BusinessNamespaces =
    [
        "Dfo.Server.Application.Administration", "Dfo.Server.Application.HttpOperations",
        "Dfo.Server.Abstractions.Administration", "Dfo.Server.Data.Administration",
    ];

    [Fact]
    public void No_mechanism_type_references_an_http_or_gm_operation()
    {
        var mechanism = typeof(CommandWorkUnits).Assembly.GetTypes()
            .Where(type => MechanismNamespaces.Contains(Outermost(type).Namespace)).ToArray();
        Assert.Contains(typeof(CommandBus), mechanism);
        Assert.Contains(typeof(CommandWorkUnits), mechanism);
        Assert.Empty(mechanism.SelectMany(Violations));
    }

    /// <summary>The counter-example: the same rule refuses a type that reaches a GM domain service in a body or a signature.</summary>
    [Fact]
    public void A_type_that_reaches_a_gm_operation_is_refused()
    {
        Assert.NotEmpty(Violations(typeof(BodyReference)));
        Assert.NotEmpty(Violations(typeof(SignatureReference)));
        Assert.Empty(Violations(typeof(Neutral)));
    }

    private sealed class BodyReference
    {
        internal static string Name() => typeof(IGmCeraDomainService).Name;
    }
    private sealed class SignatureReference(IGmOfficialMailDomainService mail)
    {
        internal IGmOfficialMailDomainService Mail { get; } = mail;
    }
    private sealed class Neutral
    {
        internal static string Name() => nameof(Neutral);
    }

    private static Type Outermost(Type type) => type.DeclaringType is { } outer ? Outermost(outer) : type;

    private static bool IsBusiness(Type? type)
    {
        if (type is null) return false;
        if (type.HasElementType) return IsBusiness(type.GetElementType());
        if (type.IsGenericType && !type.IsGenericTypeDefinition && type.GetGenericArguments().Any(IsBusiness)) return true;
        return BusinessNamespaces.Contains(Outermost(type).Namespace);
    }

    internal static IEnumerable<string> Violations(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (IsBusiness(type.BaseType)) yield return $"{type.FullName} derives {type.BaseType}";
        foreach (var contract in type.GetInterfaces().Where(IsBusiness)) yield return $"{type.FullName} implements {contract}";
        foreach (var field in type.GetFields(all).Where(field => IsBusiness(field.FieldType))) yield return $"{type.FullName}.{field.Name}: {field.FieldType}";
        foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
        {
            if (method is MethodInfo info && IsBusiness(info.ReturnType)) yield return $"{type.FullName}.{method.Name} returns {info.ReturnType}";
            foreach (var parameter in method.GetParameters().Where(parameter => IsBusiness(parameter.ParameterType)))
                yield return $"{type.FullName}.{method.Name}({parameter.ParameterType})";
            foreach (var reference in BodyReferences(method).Where(IsBusiness))
                yield return $"{type.FullName}.{method.Name} uses {reference}";
        }
    }

    private static IEnumerable<Type> BodyReferences(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) yield break;
        var typeArguments = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (var offset = 0; offset < il.Length;)
        {
            var code = il[offset] == 0xFE ? (short)(0xFE00 | il[offset + 1]) : il[offset];
            var opCode = OpCodesByValue[code];
            offset += opCode.Size;
            var operand = offset;
            offset += OperandSize(opCode.OperandType, il, offset);
            if (opCode.OperandType is not (OperandType.InlineType or OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok)) continue;
            MemberInfo? member;
            try { member = method.Module.ResolveMember(BitConverter.ToInt32(il, operand), typeArguments, methodArguments); }
            catch (ArgumentException) { continue; }
            switch (member)
            {
                case Type referenced: yield return referenced; break;
                case FieldInfo field: yield return field.DeclaringType!; yield return field.FieldType; break;
                case MethodBase called:
                    if (called.DeclaringType is { } declaring) yield return declaring;
                    if (called is MethodInfo { IsGenericMethod: true } generic) foreach (var argument in generic.GetGenericArguments()) yield return argument;
                    break;
            }
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    private static int OperandSize(OperandType type, byte[] il, int offset) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, offset)),
        _ => 4,
    };
}
