using System.Collections.Immutable;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfAvatarCompoundDefinitionSource(PvfArchiveProvider provider,
    ICeraShopDefinitionSource items) : IAvatarCompoundDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static readonly (string Pool, string Rate, string Equipment)[] Parts =
    [ ("hat", "hat", "hat avatar"), ("hair", "hair", "hair avatar"), ("face", "face", "face avatar"),
      ("neck", "neck", "breast avatar"), ("coat", "coat", "coat avatar"), ("pants", "pants", "pants avatar"),
      ("belt", "belt", "waist avatar"), ("shoes", "shoes", "shoes avatar") ];
    private readonly Lock gate = new();
    private readonly Dictionary<byte, AvatarCompoundDefinition?> cache = [];
    private IReadOnlyDictionary<int, string>? jobPaths;
    private readonly Dictionary<int, AvatarCompoundItemDefinition?> itemCache = [];

    public string Name => "avatar-compound";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int[] keys;
        // The job list is the archive's own (key = job + 1); each job's definition is bounded by it.
        lock (gate)
        {
            JobPathsIndex();
            keys = [.. jobPaths.Keys.Where(key => key is >= 1 and <= byte.MaxValue + 1)];
        }
        foreach (var key in keys) _ = ForJobAsync(checked((byte)(key - 1)), cancellationToken);
        return ValueTask.FromResult(keys.Length);
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(jobPaths))]
    private void JobPathsIndex()
    {
        jobPaths ??= PvfListFile.Paths(provider.Archive, "etc/compoundavatar.etc", prefix: "etc/");
    }

    public ValueTask<AvatarCompoundDefinition?> ForJobAsync(byte job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.TryGetValue(job, out var found)) return ValueTask.FromResult(found);
            JobPathsIndex();
            found = jobPaths.TryGetValue(job + 1, out var path) ? Parse(path) : null;
            cache[job] = found;
            return ValueTask.FromResult(found);
        }
    }

    private AvatarCompoundDefinition? Parse(string path)
    {
        var archive = provider.Archive;
        var sections = (PvfDocument.TryOpen(archive, path, PvfPreamble.Ignore)?.Sections ?? []);
        var normal = Ints("[grade]"); var advanced = Ints("[upper grade]");
        if (normal.Length != 1 || advanced.Length != 1 || normal[0] < 1 || advanced[0] <= normal[0]) return null;
        var materials = ImmutableArray.CreateBuilder<AvatarCompoundMaterial>();
        foreach (var (category, tag) in new[] { ((byte)0, "[material]"), ((byte)1, "[material_upper]") })
        {
            var values = Ints(tag);
            if (values.Length != 3 || values[0] != 1 || values[1] <= 0 || values[2] <= 0) return null;
            materials.Add(new(category, values[1], values[2]));
        }
        var bonuses = (PvfDocument.TryOpen(archive, "etc/serverparameter.etc", PvfPreamble.Ignore)?.Sections ?? [])
            .Where(s => s.Name == "[upper bind cube rate bonus]").ToArray();
        if (bonuses.Length != 1 || bonuses[0].Values.Count != 1 || bonuses[0].Values[0].Kind != PvfTokenKind.Int32
            || bonuses[0].Values[0].Value is < 0 or > 10000) return null;
        var parts = ImmutableArray.CreateBuilder<AvatarCompoundPart>();
        foreach (var (pool, rate, equipment) in Parts)
        {
            var values = Ints($"[{pool} avatar]");
            if (values.Length < 3 || values[0] < 0 || values[0] > (values.Length - 1) / 2
                || (values.Length - 1) % 2 != 0) return null;
            var rare = ImmutableArray.CreateBuilder<AvatarCompoundWeightedItem>();
            var upper = ImmutableArray.CreateBuilder<AvatarCompoundWeightedItem>();
            for (var i = 1; i < values.Length; i += 2)
            {
                if (values[i] <= 1 || values[i + 1] <= 0) return null;
                (i <= values[0] * 2 ? rare : upper).Add(new(values[i], values[i + 1]));
            }
            var r = Rate("[rare rate]", rate); var u = Rate("[upper rare rate]", rate);
            if (r is < 0 or > 10000 || u is < 0 or > 10000 || upper.Count == 0) return null;
            parts.Add(new(equipment, r, u, rare.ToImmutable(), upper.ToImmutable()));
        }
        return new(normal[0], advanced[0], bonuses[0].Values[0].Value, materials.ToImmutable(), parts.ToImmutable());

        int[] Ints(string tag)
        {
            var rows = sections.Where(s => s.Name == tag).ToArray();
            return rows.Length == 1 && rows[0].Values.All(t => t.Kind == PvfTokenKind.Int32)
                ? rows[0].Values.Select(t => t.Value).ToArray() : [];
        }
        int Rate(string tag, string name)
        {
            var rows = sections.Where(s => s.Name == tag).ToArray();
            if (rows.Length != 1 || rows[0].Values.Count % 2 != 0) return -1;
            var values = rows[0].Values; var found = -1;
            for (var i = 0; i < values.Count; i += 2)
            {
                if (!values[i].IsString || values[i + 1].Kind != PvfTokenKind.Int32) return -1;
                if (archive.ResolveString(values[i].Value) != name) continue;
                if (found != -1) return -1;
                found = values[i + 1].Value;
            }
            return found;
        }
    }

    public async ValueTask<AvatarCompoundItemDefinition?> ItemAsync(int itemId, CancellationToken ct)
    {
        lock (gate) if (itemCache.TryGetValue(itemId, out var cached)) return cached;
        var item = await items.GetItemAsync(itemId, ct).ConfigureAwait(false);
        AvatarCompoundItemDefinition? result = null;
        if (item is { Kind: CeraShopItemKind.Avatar, Avatar: { } avatar })
        {
            var document = PvfDocument.Open(provider.Archive, item.Source).AtRoot();
            var grade = document.Read(PvfField.Int32("[grade]", optional: true));
            var sections = (PvfDocument.TryOpen(provider.Archive, item.Source, PvfPreamble.Ignore)?.Sections ?? []);
            var permanent = sections.All(s => s.Name is not ("[usable period]" or "[expiration date]")
                || s.Values.Count == 1 && s.Values[0].Kind == PvfTokenKind.Int32 && s.Values[0].Value == 0);
            var jobs = document.TryRead(PvfField.Strings("[usable job]", optional: true), out var names)
                ? PvfItemUseRuleDefinitionSource.ProjectJobs(names) : ItemJobRestriction.Any;
            var level = document.Read(PvfField.Int32("[minimum level]", optional: true));
            if (grade > 0 && jobs.IsValid && level >= 0) result = new(itemId, avatar.EquipmentType.Trim('[', ']'), grade,
                avatar.SelectableAbilities, permanent, item.Sealed) { Jobs = jobs, MinimumLevel = level };
        }
        lock (gate) { itemCache.TryAdd(itemId, result); return itemCache[itemId]; }
    }
}
