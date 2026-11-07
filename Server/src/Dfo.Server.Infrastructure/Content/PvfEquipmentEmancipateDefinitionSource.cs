using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads the <c>[emancipate]</c> blocks of equipment templates, in declaration order.</summary>
/// <remarks>
/// A block's fields are flat sections between <c>[emancipate]</c> and <c>[/emancipate]</c>;
/// <c>[input]</c> and <c>[output]</c> close with their own tags. Presentation fields such as
/// <c>[emancipate explain]</c> are skipped. Blocks are used by several unrelated A21 systems and not
/// all of them are well-formed (accessories with a bare <c>1</c> as output), so a list that is not
/// item/count pairs reads as empty rather than failing the template; the growth position, which
/// only dragon-leap weapons declare, is read strictly.
/// </remarks>
internal sealed class PvfEquipmentEmancipateDefinitionSource(PvfArchiveProvider provider)
    : IEquipmentEmancipateDefinitionSource, IContentWarmup
{
    private const string Open = "[emancipate]";
    private const string Close = "[/emancipate]";
    private readonly Lock gate = new();
    private IReadOnlyDictionary<int, string>? paths;
    private readonly Dictionary<int, IReadOnlyList<EquipmentEmancipateDefinition>> cache = [];

    public string Name => "equipment-emancipate-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(Paths().Count);
    }

    public ValueTask<IReadOnlyList<EquipmentEmancipateDefinition>> GetEquipmentAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!Paths().TryGetValue(itemId, out var path))
                return ValueTask.FromResult<IReadOnlyList<EquipmentEmancipateDefinition>>([]);
            if (!cache.TryGetValue(itemId, out var blocks))
            {
                blocks = Read(provider.Archive, PvfDocument.Open(provider.Archive, path).Sections, path);
                cache.Add(itemId, blocks);
            }
            return ValueTask.FromResult(blocks);
        }
    }

    private IReadOnlyDictionary<int, string> Paths() =>
        paths ??= PvfListFile.Paths(provider.Archive, "equipment/equipment.lst", prefix: "equipment/");

    internal static IReadOnlyList<EquipmentEmancipateDefinition> Read(PvfArchive archive, IReadOnlyList<PvfSection> sections, string path)
    {
        var blocks = new List<EquipmentEmancipateDefinition>();
        for (var i = 0; i < sections.Count; i++)
        {
            if (sections[i].Name != Open) continue;
            int? type = null;
            EquipmentGrowthPosition? growth = null;
            IReadOnlyList<EmancipateItemCount>? inputs = null, outputs = null;
            var closed = false;
            for (i++; i < sections.Count && !closed; i++)
            {
                var section = sections[i];
                switch (section.Name)
                {
                    case Close: closed = true; i--; break;
                    case "[emancipate type]":
                        if (section.Values is [{ Kind: PvfTokenKind.Int32 } value]) type = value.Value;
                        break;
                    case "[equipment grow]":
                        PvfField.Require(growth is null && section.Values is [{ Kind: PvfTokenKind.Int32 }, { Kind: PvfTokenKind.Int32 }]
                            && section.Values[0].Value > 0 && section.Values[1].Value > 0,
                            path, section.Name, "expected one positive group and star");
                        growth = new(section.Values[0].Value, section.Values[1].Value);
                        break;
                    case "[input]": inputs ??= Pairs(section); break;
                    case "[output]": outputs ??= Pairs(section); break;
                }
            }
            PvfField.Require(closed, path, Open, "block is not closed");
            blocks.Add(new(type, growth, inputs ?? [], outputs ?? []));
        }
        PvfField.Require(blocks.Count(x => x.Growth is not null) <= 1, path, "[equipment grow]", "declared by more than one block");
        return blocks.AsReadOnly();
    }

    private static IReadOnlyList<EmancipateItemCount> Pairs(PvfSection section)
    {
        var values = section.Values;
        if (values.Count % 2 != 0 || values.Any(x => x.Kind != PvfTokenKind.Int32 || x.Value <= 0)) return [];
        var pairs = new EmancipateItemCount[values.Count / 2];
        for (var i = 0; i < pairs.Length; i++) pairs[i] = new(values[2 * i].Value, values[2 * i + 1].Value);
        return pairs;
    }
}
