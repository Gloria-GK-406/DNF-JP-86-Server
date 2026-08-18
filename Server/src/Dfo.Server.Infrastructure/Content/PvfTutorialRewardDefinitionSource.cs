using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads the tutorial's reward table out of the server parameter file.
/// </summary>
/// <remarks>
/// <c>etc/serverparameter.etc</c> -- under <c>etc/</c>, not at the archive root -- declares
/// <c>[escalade tutorial reward]</c> as a flat run of integers read in threes: tutorial flag, item
/// identifier, count. A flag may appear more than once to give several items; in the archive
/// shipped here the whole table is one triple, <c>38 8474 1</c>.
/// </remarks>
internal sealed class PvfTutorialRewardDefinitionSource : ITutorialRewardDefinitionSource
{
    private const string ParameterPath = "etc/serverparameter.etc";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private IReadOnlyDictionary<uint, IReadOnlyList<TutorialRewardItem>>? _table;

    public PvfTutorialRewardDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<IReadOnlyList<TutorialRewardItem>> GetAsync(
        uint tutorialFlag,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _table ??= Read(_provider.Archive);
            return ValueTask.FromResult(
                _table.TryGetValue(tutorialFlag, out var items) ? items : []);
        }
    }

    private static IReadOnlyDictionary<uint, IReadOnlyList<TutorialRewardItem>> Read(PvfArchive archive)
    {
        var table = new Dictionary<uint, List<TutorialRewardItem>>();
        var fileIndex = archive.FindFile(ParameterPath);
        if (fileIndex < 0)
            return table.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<TutorialRewardItem>)pair.Value);

        var open = archive.GetTag("[escalade tutorial reward]").Offset;
        var close = archive.GetTag("[/escalade tutorial reward]").Offset;
        var inside = false;
        var operands = new List<int>(3);

        var reader = archive.OpenScript(fileIndex);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                var name = archive.ResolveString(token.Value);
                if (!name.StartsWith('['))
                    continue;
                if (token.Value == open)
                {
                    inside = true;
                    operands.Clear();
                }
                else if (inside)
                {
                    // Closed by its own tag, and by any other tag as well: an unterminated block
                    // would otherwise read the next section's integers as reward triples.
                    _ = close;
                    break;
                }

                continue;
            }

            if (!inside || token.Kind != PvfTokenKind.Int32)
                continue;

            operands.Add(token.Value);
            if (operands.Count < 3)
                continue;

            var (flag, itemId, count) = (operands[0], operands[1], operands[2]);
            operands.Clear();
            if (flag < 0 || count <= 0)
                continue;
            if (!table.TryGetValue((uint)flag, out var items))
                table[(uint)flag] = items = [];
            items.Add(new TutorialRewardItem(itemId, count));
        }

        return table.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<TutorialRewardItem>)pair.Value);
    }
}
