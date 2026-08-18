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
                grants.Add(new InitialSkillGrant((uint)pendingSkillId.Value, (byte)token.Value));
            pendingSkillId = null;
        }

        return grants;
    }

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
