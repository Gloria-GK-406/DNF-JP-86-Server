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
internal sealed class PvfInitialSkillDefinitionSource : IInitialSkillDefinitionSource
{
    private const string ListPath = "character/character.lst";
    private const string CharacterDirectory = "character";

    private readonly PvfArchiveProvider _provider;
    private readonly Lock _gate = new();
    private readonly Dictionary<byte, IReadOnlyList<InitialSkillGrant>> _byJob = [];
    private IReadOnlyDictionary<int, string>? _paths;

    public PvfInitialSkillDefinitionSource(PvfArchiveProvider provider) => _provider = provider;

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

        var tags = new CharacterTags(archive);
        var skillPaths = SkillPaths(archive, job);
        var grants = new List<InitialSkillGrant>();
        var inInitialValue = false;
        var inSkill = false;
        int? pendingSkillId = null;

        var reader = archive.OpenScript(fileIndex);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                var name = archive.ResolveString(token.Value);
                if (!name.StartsWith('['))
                    continue;

                if (token.Value == tags.InitialValue) inInitialValue = true;
                else if (token.Value == tags.InitialValueClose) return grants;
                else if (token.Value == tags.Skill) inSkill = inInitialValue;
                else if (token.Value == tags.SkillClose)
                {
                    if (inSkill)
                        return grants;
                }
                else if (inSkill)
                {
                    // Any other tag ends the block too: the observed files close [skill] properly,
                    // but a file that did not would otherwise swallow the next section's integers.
                    return grants;
                }

                pendingSkillId = null;
                continue;
            }

            if (!inSkill || token.Kind != PvfTokenKind.Int32)
                continue;

            if (pendingSkillId is null)
            {
                pendingSkillId = token.Value;
                continue;
            }

            if (pendingSkillId >= 0 && token.Value is >= 0 and <= byte.MaxValue)
            {
                grants.Add(new InitialSkillGrant(
                    (uint)pendingSkillId.Value,
                    (byte)token.Value,
                    IsActiveSkill(archive, skillPaths, pendingSkillId.Value)));
            }

            pendingSkillId = null;
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

        var typeTag = archive.GetTag("[type]").Offset;
        var reader = archive.OpenScript(fileIndex);
        var expecting = false;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                var name = archive.ResolveString(token.Value);
                if (name.StartsWith('['))
                {
                    expecting = token.Value == typeTag;
                    continue;
                }
            }

            if (expecting && token.IsString)
            {
                return archive.ResolveString(token.Value)
                    .Contains("active", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
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
            InitialValue = archive.GetTag("[initial value]").Offset;
            InitialValueClose = archive.GetTag("[/initial value]").Offset;
            Skill = archive.GetTag("[skill]").Offset;
            SkillClose = archive.GetTag("[/skill]").Offset;
        }

        public int InitialValue { get; }

        public int InitialValueClose { get; }

        public int Skill { get; }

        public int SkillClose { get; }
    }
}
