using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads the equipment each job's characters are created wearing out of its character definition.
/// </summary>
/// <remarks>
/// <c>character/character.lst</c> maps a job to its <c>.chr</c> file, and that file's
/// <c>[create equipment list]</c> section names a slot and then the item that fills it:
/// <c>[weapon] 27600 [coat] 10400 [pants] 12400</c> for the swordman. The section is read for the
/// slot names it uses rather than positionally, because a job that starts with fewer pieces simply
/// omits their names and reading by position would then dress it wrong.
/// <para>
/// Durability comes from the item's own definition, not from a default. An equipment core created
/// at zero durability reaches the client as a broken piece, which is drawn but cannot act -- the
/// same visible symptom as having no equipment at all, arrived at a different way.
/// </para>
/// </remarks>
internal sealed class PvfInitialEquipmentDefinitionSource : IInitialEquipmentDefinitionSource
{
    private const string ListPath = "character/character.lst";
    private const string CharacterDirectory = "character";
    private const string EquipmentListPath = "equipment/equipment.lst";
    private const string EquipmentDirectory = "equipment";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<byte, IReadOnlyList<InitialEquipmentGrant>> _byJob = [];
    private IReadOnlyDictionary<int, string>? _characterPaths;
    private IReadOnlyDictionary<int, string>? _equipmentPaths;

    public PvfInitialEquipmentDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public ValueTask<IReadOnlyList<InitialEquipmentGrant>> GetAsync(
        byte job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_byJob.TryGetValue(job, out var cached))
                return ValueTask.FromResult(cached);

            var grants = Read(_provider.Archive, job);
            _byJob[job] = grants;
            return ValueTask.FromResult(grants);
        }
    }

    private IReadOnlyDictionary<int, string> CharacterPaths()
    {
        if (_characterPaths is not null)
            return _characterPaths;

        var paths = new Dictionary<int, string>();
        foreach (var (job, relativePath) in PvfListFile.Read(_provider.Archive, ListPath))
            paths.TryAdd(job, relativePath);
        if (paths.Count == 0)
            throw new InvalidOperationException($"{ListPath} resolved no character files.");
        return _characterPaths = paths;
    }

    private IReadOnlyDictionary<int, string> EquipmentPaths()
    {
        if (_equipmentPaths is not null)
            return _equipmentPaths;

        var paths = new Dictionary<int, string>();
        foreach (var (itemId, relativePath) in PvfListFile.Read(_provider.Archive, EquipmentListPath))
            paths.TryAdd(itemId, relativePath);
        return _equipmentPaths = paths;
    }

    private IReadOnlyList<InitialEquipmentGrant> Read(PvfArchive archive, byte job)
    {
        if (!CharacterPaths().TryGetValue(job, out var relativePath))
            return [];

        var fileIndex = archive.FindFile($"{CharacterDirectory}/{relativePath}");
        if (fileIndex < 0)
            return [];

        var open = archive.GetTag("[create equipment list]").Offset;
        var close = archive.GetTag("[/create equipment list]").Offset;
        var grants = new List<InitialEquipmentGrant>();
        var inside = false;
        byte? pendingSlot = null;

        // The slot names inside the section are quoted strings, not tags. The section's own open
        // and close are tags, so both kinds have to be read: a reader that matches only tags sees
        // an empty section and dresses every character in nothing, which is what the printed form
        // of the file gives no hint of.
        var reader = archive.OpenScript(fileIndex);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (token.Value == open)
                {
                    inside = true;
                    pendingSlot = null;
                    continue;
                }

                if (inside && token.Value == close)
                    break;

                if (inside)
                    pendingSlot = null;
                continue;
            }

            if (!inside)
                continue;

            if (token.IsString)
            {
                // An unrecognised slot name still clears the pending one. Carrying it forward
                // would attach the next name's integer to the last slot we understood.
                pendingSlot = PvfEquipmentSlots.ByName.TryGetValue(archive.ResolveString(token.Value), out var slot)
                    ? slot
                    : null;
                continue;
            }

            if (pendingSlot is null || token.Kind != PvfTokenKind.Int32)
                continue;

            if (token.Value > 0)
                grants.Add(new InitialEquipmentGrant(
                    pendingSlot.Value, token.Value, Durability(archive, token.Value)));
            pendingSlot = null;
        }

        return grants;
    }

    /// <summary>What the item's definition declares, or zero when it declares nothing.</summary>
    private ushort Durability(PvfArchive archive, int itemId) =>
        PvfEquipmentDurability.Read(archive, EquipmentPaths(), itemId);

}
