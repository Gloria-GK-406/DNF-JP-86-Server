using System.Globalization;
using System.Collections.Immutable;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfItemSocketDefinitionSource(PvfArchiveProvider provider) : IItemSocketDefinitionSource
{
    private static readonly PvfField<IReadOnlyList<PvfToken>> EquipmentType = PvfField.Tokens("[equipment type]", optional: true);
    private static readonly PvfField<IReadOnlyList<PvfToken>> StackType = PvfField.Tokens("[stackable type]", optional: true);
    private static readonly PvfField<IReadOnlyList<PvfToken>> Action = PvfField.Tokens("[action type]", optional: true);
    private static readonly PvfField<IReadOnlyList<PvfToken>> AvatarTypes = PvfField.Tokens("[avatar type select]", optional: true);
    private static readonly PvfField<IReadOnlyList<PvfToken>> DefaultTypes = PvfField.Tokens("[emblem socket default]", optional: true);
    private static readonly PvfField<int> DefaultCount = PvfField.Int32("[avatar emblem socket num]", optional: true, minimum: 0, maximum: 5);
    private static readonly PvfField<IReadOnlyList<PvfToken>> Expiration = PvfField.Tokens("[expiration date]", optional: true);
    private static readonly PvfField<IReadOnlyList<string>> EmblemTypes = PvfField.Strings("[avatar emblem target type]", optional: true);
    private readonly Lock gate = new();
    private Dictionary<int, string>? equipmentPaths, stackablePaths;
    private Dictionary<string, ImmutableArray<ushort>>? equipmentTypes;
    private readonly Dictionary<int, ItemSocketDefinition?> targets = [];
    private readonly Dictionary<int, SocketDrillDefinition?> drills = [];
    private readonly Dictionary<int, ushort> emblems = [];
    public ValueTask<ItemSocketDefinition?> TargetAsync(int id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (targets.TryGetValue(id, out var cached)) return ValueTask.FromResult(cached);
            var archive = provider.Archive;
            equipmentPaths ??= Paths(archive, "equipment");
            ItemSocketDefinition? found = null;
            if (equipmentPaths.TryGetValue(id, out var path))
            {
                var document = PvfDocument.Open(archive, path).AtRoot();
                var type = FirstText(archive, document, EquipmentType);
                if (type.EndsWith(" avatar]", StringComparison.Ordinal))
                {
                    var open = document.TryRead(AvatarTypes, out var values) ? ReadTypes(archive, values) : [];
                    var defaults = document.TryRead(DefaultTypes, out values) ? ReadTypes(archive, values) : [];
                    if (document.TryRead(DefaultCount, out var count)) defaults = defaults.Take(count).ToImmutableArray();
                    found = new(true, open, defaults);
                }
                else
                {
                    equipmentTypes ??= ReadEquipmentTypes(archive);
                    if (equipmentTypes.TryGetValue(type, out var types)) found = new(false, types, []);
                }
            }
            targets[id] = found;
            return ValueTask.FromResult(found);
        }
    }
    public ValueTask<SocketDrillDefinition?> DrillAsync(int id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (drills.TryGetValue(id, out var cached)) return ValueTask.FromResult(cached);
            var archive = provider.Archive;
            stackablePaths ??= Paths(archive, "stackable");
            SocketDrillDefinition? found = null;
            if (stackablePaths.TryGetValue(id, out var path))
            {
                var document = PvfDocument.Open(archive, path).AtRoot();
                if (FirstText(archive, document, Action) == "[add avatar socket]") found = new(true);
                // These A21 equipment drills have no action declaration, only [etc]. See protocol 037A.
                else if (id is 2749124 or 10005065 or 490002488 or 490003035
                    && FirstText(archive, document, StackType) == "[etc]") found = new(false);
            }
            if (found is not null && stackablePaths.TryGetValue(id, out var drillPath))
            {
                var document = PvfDocument.Open(archive, drillPath).AtRoot();
                if (document.TryRead(Expiration, out var dates))
                {
                    if (dates.Count != 1) found = null;
                    else if (dates[0].IsString && DateTime.TryParseExact(archive.ResolveString(dates[0].Value),
                        "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        found = found with { ExpiresAt = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date)).ToUnixTimeSeconds() };
                    else if (dates[0].Kind != PvfTokenKind.Int32 || dates[0].Value != 0) found = null;
                }
            }
            drills[id] = found;
            return ValueTask.FromResult(found);
        }
    }
    public ValueTask<ushort> EmblemTypesAsync(int id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (emblems.TryGetValue(id, out var cached)) return ValueTask.FromResult(cached);
            var archive = provider.Archive;
            stackablePaths ??= Paths(archive, "stackable");
            ushort result = 0;
            if (stackablePaths.TryGetValue(id, out var path))
            {
                var document = PvfDocument.Open(archive, path).AtRoot();
                if (FirstText(archive, document, StackType) == "[avatar emblem]"
                    && document.TryRead(EmblemTypes, out var values))
                {
                    var types = values.Select(SocketType).ToArray();
                    if (types.All(x => x != 0)) foreach (var value in types) result |= value;
                }
            }
            emblems[id] = result;
            return ValueTask.FromResult(result);
        }
    }
    private static Dictionary<int, string> Paths(PvfArchive archive, string folder) =>
        PvfListFile.Read(archive, folder + "/" + folder + ".lst").ToDictionary(x => x.Id, x => folder + "/" + x.RelativePath);
    private static string FirstText(PvfArchive archive, PvfDocument document, PvfField<IReadOnlyList<PvfToken>> field) =>
        document.TryRead(field, out var values) && values.Count > 0 && values[0].IsString ? archive.ResolveString(values[0].Value) : "";
    private static ImmutableArray<ushort> ReadTypes(PvfArchive archive, IReadOnlyList<PvfToken> values)
    {
        var types = values.Where(x => x.IsString).Select(x => SocketType(archive.ResolveString(x.Value))).ToImmutableArray();
        return types.Any(x => x == 0) ? [] : types;
    }
    private static Dictionary<string, ImmutableArray<ushort>> ReadEquipmentTypes(PvfArchive archive)
    {
        var result = new Dictionary<string, ImmutableArray<ushort>>(StringComparer.Ordinal);
        string? type = null;
        foreach (var section in PvfDocument.Open(archive, "etc/chn_equipmentsockettypelist.etc").Sections)
        {
            if (section.Name == "[equipment type]") type = section.Values.Count == 1 && section.Values[0].IsString
                ? archive.ResolveString(section.Values[0].Value) : throw new InvalidDataException("Invalid equipment socket type.");
            if (section.Name != "[socket info]") continue;
            var types = ReadTypes(archive, section.Values);
            if (type is null || types.Length is < 1 or > 2 || types.Length != section.Values.Count || !result.TryAdd(type, types))
                throw new InvalidDataException("Invalid equipment socket layout.");
        }
        return result;
    }
    private static ushort SocketType(string value) => value switch
    { "[A socket]" => 1, "[B socket]" => 2, "[C socket]" => 4, "[D socket]" => 8, "[S socket]" => 16, "[M socket]" => 0xFFEF, _ => 0 };
}
