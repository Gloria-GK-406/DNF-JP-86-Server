using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads each job's starting skills out of its character definition.
/// </summary>
/// <remarks>
/// <c>character/character.lst</c> maps a job to its <c>.chr</c> file, and that file's
/// <c>[initial value]</c> section carries a <c>[skill]</c> block of bare integers read in pairs:
/// skill identifier then level. The same file declares further grants under <c>[growtype N]</c>
/// and awakening sections, which belong to progression rather than to creation and are not read
/// here.
/// <para>
/// The <c>[skill]</c> block is scoped to <c>[initial value]</c> deliberately. Taking the first
/// <c>[skill]</c> in the file would work for the jobs whose initial section comes first and
/// silently pick a growth-stage list for any job where it does not.
/// </para>
/// </remarks>
internal sealed class PvfInitialSkillDefinitionSource : IInitialSkillDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<string> TypeField = PvfField.String("[type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> InitialValueField = PvfField.Tokens("[initial value]", optional: true);
        internal static readonly PvfField<bool> CloseInitialValueField = PvfField.Marker("[/initial value]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> SkillField = PvfField.Int32List("[skill]", optional: true);
        internal static readonly PvfField<bool> CloseSkillField = PvfField.Marker("[/skill]", optional: true);
    }

    private const string ListPath = "character/character.lst";
    private const string CharacterDirectory = "character";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<byte, IReadOnlyList<InitialSkillGrant>> _byJob = [];
    private IReadOnlyDictionary<int, string>? _paths;

    public PvfInitialSkillDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

    public string Name => "initial-skills";

    /// <remarks>Every job the archive's character list declares.</remarks>
    public async ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        int[] jobs;
        lock (_gate) jobs = [.. Paths().Keys];
        foreach (var job in jobs) await GetAsync(checked((byte)job), cancellationToken).ConfigureAwait(false);
        return jobs.Length;
    }

    public ValueTask<IReadOnlyList<InitialSkillGrant>> GetAsync(
        byte job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_byJob.TryGetValue(job, out var cached))
                return ValueTask.FromResult(cached);

            var grants = Read(_provider.Archive, Paths(), job);
            _byJob[job] = grants;
            return ValueTask.FromResult(grants);
        }
    }

    private IReadOnlyDictionary<int, string> Paths()
    {
        if (_paths is not null)
            return _paths;

        var paths = new Dictionary<int, string>();
        foreach (var (job, relativePath) in PvfListFile.Read(_provider.Archive, ListPath))
            paths.TryAdd(job, relativePath);
        if (paths.Count == 0)
            throw new InvalidOperationException($"{ListPath} resolved no character files.");
        return _paths = paths;
    }

    private static IReadOnlyList<InitialSkillGrant> Read(
        PvfArchive archive,
        IReadOnlyDictionary<int, string> paths,
        byte job)
    {
        if (!paths.TryGetValue(job, out var relativePath))
            return [];

        var fileIndex = archive.FindFile($"{CharacterDirectory}/{relativePath}");
        if (fileIndex < 0)
            return [];

        var skillPaths = SkillPaths(archive, job);
        var grants = new List<InitialSkillGrant>();
        var inInitialValue = false;
        var path = $"{CharacterDirectory}/{relativePath}";
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            if (Fields.InitialValueField.Matches(section.Name)) inInitialValue = true;
            else if (Fields.CloseInitialValueField.Matches(section.Name)) break;
            else if (inInitialValue && Fields.SkillField.Matches(section.Name))
            {
                var values = Fields.SkillField.Decode(archive, section.Values, path);
                if (values.Count % 2 != 0) throw new InvalidDataException($"PVF '{path}', field '{Fields.SkillField.Name}': incomplete skill/level pair.");
                for (var i = 0; i < values.Count; i += 2)
                {
                    if (values[i] < 0 || values[i + 1] is < 0 or > byte.MaxValue)
                        throw new InvalidDataException($"PVF '{path}', field '{Fields.SkillField.Name}': invalid skill/level range.");
                    grants.Add(new((uint)values[i], (byte)values[i + 1], IsActiveSkill(archive, skillPaths, values[i])));
                }
                break;
            }
        }
        return grants;
    }

    /// <summary>
    /// Whether the skill's own definition marks it <c>[active]</c>.
    /// </summary>
    /// <remarks>
    /// Read from the skill rather than guessed from the grant. Nothing in the character file
    /// distinguishes the skills a player can put on a key from the passives and sub-profession
    /// skills that are held invisibly -- every entry requires level one, and the identifiers carry
    /// no pattern -- so the answer has to come from the skill itself.
    /// </remarks>
    private static bool IsActiveSkill(
        PvfArchive archive,
        IReadOnlyDictionary<int, string> skillPaths,
        int skillId)
    {
        if (!skillPaths.TryGetValue(skillId, out var relativePath))
            return false;
        var fileIndex = archive.FindFile($"skill/{relativePath}");
        if (fileIndex < 0)
            return false;

        var document = PvfDocument.Open(archive, $"skill/{relativePath}");
        return document.TryRead(Fields.TypeField, out var type)
            && type.Contains("active", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The job's skill index, mapping identifiers to their definition files.</summary>
    /// <remarks>
    /// Named after the job rather than shared: skill identifiers are per job, so 5 is one skill
    /// for the swordman and another for the mage.
    /// </remarks>
    private static IReadOnlyDictionary<int, string> SkillPaths(PvfArchive archive, byte job)
    {
        var listPath = $"skill/{JobSkillListName(archive, job)}";
        if (archive.FindFile(listPath) < 0)
            return new Dictionary<int, string>();
        var paths = new Dictionary<int, string>();
        foreach (var (skillId, relative) in PvfListFile.Read(archive, listPath))
            paths.TryAdd(skillId, relative);
        return paths;
    }

    /// <summary>
    /// The skill list belonging to a job, found by the directory its character file lives in.
    /// </summary>
    /// <remarks>
    /// The lists are named after that directory with "skill" appended -- Swordman/Swordman.chr
    /// alongside swordmanskill.lst -- with the knight as the archive's own exception, whose list
    /// carries no suffix.
    /// </remarks>
    private static string JobSkillListName(PvfArchive archive, byte job) =>
        job == KnightJob ? "knight.lst" : $"{JobDirectory(archive, job)}skill.lst";

    private static string JobDirectory(PvfArchive archive, byte job)
    {
        foreach (var (candidate, relative) in PvfListFile.Read(archive, ListPath))
        {
            if (candidate != job)
                continue;
            var name = relative.Split('/')[^1];
            return name.EndsWith(".chr", StringComparison.OrdinalIgnoreCase)
                ? name[..^4].ToLowerInvariant()
                : name.ToLowerInvariant();
        }

        return string.Empty;
    }

    private const byte KnightJob = 12;

    private readonly struct CharacterTags
    {
        public CharacterTags(PvfArchive archive)
        {
            InitialValue = archive.GetTag(Fields.InitialValueField).Offset;
            InitialValueClose = archive.GetTag(Fields.CloseInitialValueField).Offset;
            Skill = archive.GetTag(Fields.SkillField).Offset;
            SkillClose = archive.GetTag(Fields.CloseSkillField).Offset;
        }

        public int InitialValue { get; }

        public int InitialValueClose { get; }

        public int Skill { get; }

        public int SkillClose { get; }
    }
}
