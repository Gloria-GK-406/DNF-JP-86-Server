using Dfo.Pvf.Schema;
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
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> EscaladeTutorialRewardField = PvfField.Int32List("[escalade tutorial reward]", optional: true);
        internal static readonly PvfField<bool> CloseEscaladeTutorialRewardField = PvfField.Marker("[/escalade tutorial reward]", optional: true);
    }

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

        var document = PvfDocument.Open(archive, ParameterPath);
        if (!document.TryRead(Fields.EscaladeTutorialRewardField, out var values)) return table
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<TutorialRewardItem>)pair.Value);
        if (values.Count % 3 != 0)
            throw new InvalidDataException($"PVF '{ParameterPath}', field '{Fields.EscaladeTutorialRewardField.Name}': incomplete reward triple.");
        for (var i = 0; i < values.Count; i += 3)
        {
            var (flag, itemId, count) = (values[i], values[i + 1], values[i + 2]);
            if (flag < 0 || count <= 0) continue;
            if (!table.TryGetValue((uint)flag, out var items)) table[(uint)flag] = items = [];
            items.Add(new TutorialRewardItem(itemId, count));
        }

        return table.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<TutorialRewardItem>)pair.Value);
    }
}
