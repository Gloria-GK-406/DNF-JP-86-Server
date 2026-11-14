using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads each job's <c>[support weapon sub type]</c> lists out of its character file.</summary>
/// <remarks>
/// Sections are read flat, in file order: a list before the first <c>[growtype N]</c> belongs to
/// every advancement, and one after it to that block (grow type N-1, the same numbering the
/// character stat source uses). <c>[awakening N]</c> headings sit inside a grow type block and do
/// not start a new one.
/// </remarks>
internal sealed class PvfCharacterSupportWeaponDefinitionSource(PvfArchiveProvider provider)
    : ICharacterSupportWeaponDefinitionSource, IContentWarmup
{
    public string Name => "character-support-weapons";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult((_allowances ??= ReadAll(provider.Archive)).Count);
    }

    private static readonly PvfFieldFamily GrowTypeFields = new("[growtype ");
    private static readonly PvfField<IReadOnlyList<int>> SubTypeField =
        PvfField.Int32List("[support weapon sub type]", optional: true);

    private const string CharacterList = "character/character.lst";
    private static readonly IReadOnlySet<int> None = FrozenSet<int>.Empty;

    private readonly Lock _gate = new();
    private FrozenDictionary<(byte Job, int GrowType), IReadOnlySet<int>>? _allowances;

    public ValueTask<IReadOnlySet<int>> SubTypesAsync(CharacterCareer career, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _allowances ??= ReadAll(provider.Archive);
            return ValueTask.FromResult(_allowances.GetValueOrDefault((career.Job, career.GrowType), None));
        }
    }

    private static FrozenDictionary<(byte Job, int GrowType), IReadOnlySet<int>> ReadAll(PvfArchive archive)
    {
        var allowances = new Dictionary<(byte Job, int GrowType), IReadOnlySet<int>>();
        foreach (var (id, relative) in PvfListFile.Read(archive, CharacterList))
        {
            var job = checked((byte)id);
            var path = "character/" + relative;
            var shared = new HashSet<int>();
            var byGrowType = new Dictionary<int, HashSet<int>>();
            HashSet<int>? current = null;
            foreach (var section in PvfDocument.Open(archive, path).Sections)
            {
                if (GrowTypeFields.TryGetIndex(section.Name, out var grow))
                    current = byGrowType[grow - 1] = [];
                else if (SubTypeField.Matches(section.Name))
                    (current ?? shared).UnionWith(SubTypeField.Decode(archive, section.Values, path));
            }
            if (shared.Count > 0) allowances[(job, 0)] = shared.ToFrozenSet();
            foreach (var (growType, own) in byGrowType)
            {
                own.UnionWith(shared);
                if (own.Count > 0) allowances[(job, growType)] = own.ToFrozenSet();
            }
        }
        return allowances.ToFrozenDictionary();
    }
}
