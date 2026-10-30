using System.Text.RegularExpressions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// A Game opcode is written down once, as a catalog member; registrations and encoders read it from there.
/// </summary>
/// <remarks>
/// Before this, a slice named its opcode in the decoder table, again in the message's catalog member and again in the
/// encoder, and nothing checked that the three agreed. Decoders now register under a catalog member and encoders take
/// the type from the message, so a mismatch can only come back by writing a number where a member belongs: these
/// tests refuse that.
/// </remarks>
[Trait("Category", "Unit")]
public sealed partial class OpcodeArchitectureTests
{
    /// <summary>Wire keys the catalog cannot spell, each with the reason.</summary>
    private static readonly Dictionary<(byte Selector, ushort Type), string> UnnamedKeys = new()
    {
        [(1, 0x0008)] = "GET_USER_INFO: one opcode, told apart as roster or other-character detail by its body.",
    };

    [Fact]
    public void Every_decoder_key_is_a_catalog_member_or_the_family_of_one()
    {
        using var composed = ProductionComposition.Build();
        var violations = UnnamedDecoderKeys(
            composed.Services.Select(x => x.ImplementationInstance).OfType<GameMessageDecoderRegistration>().ToArray(),
            composed.Services.Select(x => x.ImplementationInstance).OfType<ClientMessageProductionRegistration>().ToArray());
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_decoder_rule_refuses_a_key_whose_members_live_elsewhere()
    {
        GameMessageDecoderRegistration[] decoders =
        [
            new(1, 0x03E5, typeof(HeadDecoder)),
            new(1, 0x03E6, typeof(FamilyDecoder)),
            new(1, 0x0123, typeof(StrayDecoder)),
        ];
        ClientMessageProductionRegistration[] productions =
        [
            new(ClientMessageType.GameCollectionBox, typeof(HeadDecoder)),
            new(ClientMessageType.GameCollectionBox, typeof(FamilyDecoder)),
            new(ClientMessageType.GameMarriageHousing, typeof(StrayDecoder)),
        ];
        Assert.Equal(["StrayDecoder at 1:0123 produces GameMarriageHousing"], UnnamedDecoderKeys(decoders, productions));
    }

    [Fact]
    public void No_encoder_writes_an_opcode_as_a_number()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "86JP.Server.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var protocol = Path.Combine(root.FullName, "src", "Dfo.Server.Protocol");
        var violations = Directory.EnumerateFiles(protocol, "*.cs", SearchOption.AllDirectories)
            .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => NumericOpcodes(File.ReadAllText(file)).Select(x => $"{Path.GetRelativePath(protocol, file)}: {x}"))
            .ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_encoder_rule_refuses_a_literal_but_not_a_member_or_a_declared_type()
    {
        const string source = """
            OnePayload(0x03EB, body);
            OnePayload(m.Multi ? (ushort)0x013B : (ushort)0x005E, MailboxWire.Ack(0x01));
            OnePayload(checked((ushort)(0x03E5 + (int)m.Action)), body);
            OnePayload(message, new byte[] { 0x00, 0x01 });
            OnePayload(m.Type, Body(0x10));
            OnePayload(checked((ushort)(GameWireKey.TypeOf(m.MessageType) + (int)m.Action)), body);
            """;
        Assert.Equal(["OnePayload(0x03EB", "OnePayload(m.Multi ? (ushort)0x013B : (ushort)0x005E",
            "OnePayload(checked((ushort)(0x03E5 + (int)m.Action))"], NumericOpcodes(source));
    }

    private static IReadOnlyList<string> UnnamedDecoderKeys(
        IReadOnlyList<GameMessageDecoderRegistration> decoders, IReadOnlyList<ClientMessageProductionRegistration> productions)
    {
        static (int Selector, int Type) KeyOf(ClientMessageType member) => ((int)member >> 16, (int)member & 0xFFFF);
        var registered = decoders.Select(x => ((int)x.Selector, (int)x.Type)).ToHashSet();
        var violations = new List<string>();
        foreach (var decoder in decoders)
        {
            if (UnnamedKeys.ContainsKey((decoder.Selector, decoder.Type))) continue;
            var produced = productions.Where(x => x.DecoderType == decoder.DecoderType).Select(x => x.MessageType).Distinct().ToArray();
            // Named: a member the decoder produces is this very key.
            if (produced.Any(x => KeyOf(x) == (decoder.Selector, decoder.Type))) continue;
            // A family: opcodes that decode to a member whose own key is registered too, so the member is not a guess.
            if (produced.Length > 0 && produced.All(x => registered.Contains(KeyOf(x)))) continue;
            violations.Add($"{decoder.DecoderType.Name} at {decoder.Selector}:{decoder.Type:X4} produces {string.Join(", ", produced)}");
        }
        return violations;
    }

    /// <summary>The first argument of every <c>OnePayload</c> call that spells a number.</summary>
    private static IReadOnlyList<string> NumericOpcodes(string source)
    {
        var found = new List<string>();
        foreach (Match call in OnePayloadCall().Matches(source))
        {
            var start = call.Index + call.Length;
            var depth = 0;
            var end = start;
            for (; end < source.Length; end++)
            {
                var c = source[end];
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') { if (depth == 0) break; depth--; }
                else if (c == ',' && depth == 0) break;
            }
            var argument = source[start..end];
            if (NumberLiteral().IsMatch(argument)) found.Add(call.Value + argument);
        }
        return found;
    }

    [GeneratedRegex(@"\bOnePayload\(")]
    private static partial Regex OnePayloadCall();

    [GeneratedRegex(@"\b0[xX][0-9A-Fa-f]+\b|\b\d+\b")]
    private static partial Regex NumberLiteral();

    private sealed class HeadDecoder;
    private sealed class FamilyDecoder;
    private sealed class StrayDecoder;
}
