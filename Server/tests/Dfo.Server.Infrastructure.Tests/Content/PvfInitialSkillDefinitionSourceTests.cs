using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The skills a job's characters are created holding.
/// </summary>
/// <remarks>
/// Job 0 is the swordman, the job the tutorial was walked with on 2026-08-18 and the one whose
/// empty skill bar prompted this. Its declared set is fixed here in full because the set itself is
/// the answer: nothing inside the archive distinguishes a subset of it as "what level one gets",
/// and every entry requires level one. The active mark decides which slot band a grant lands in
/// rather than whether it is held at all. See
/// protocol/patterns/data/probes/pvf-initial-skills-20260818.md.
///
/// The values are the A21 client's. The older client declares eight grants for this job where A21
/// declares six, agreeing on 179, 174, 5, 46 and 169 and differing on the rest, so a test that
/// passes against one archive fails against the other -- which is the point of pinning them.
/// </remarks>
public sealed class PvfInitialSkillDefinitionSourceTests
{
    /// <summary>The highest job the A21 archive declares: 13, the demonic lancer.</summary>
    private const byte LastDeclaredJob = 13;

    [SkippableFact]
    public async Task The_swordman_is_created_holding_its_six_declared_skills()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfInitialSkillDefinitionSource(provider);

        var grants = await source.GetAsync(0, CancellationToken.None);

        Assert.Equal(
            [
                new InitialSkillGrant(179, 7, IsActive: false), // 物品分解
                new InitialSkillGrant(174, 1, IsActive: false), // 基础精通
                new InitialSkillGrant(169, 1, IsActive: true),  // 后跳
                new InitialSkillGrant(5, 1, IsActive: true),    // 鬼斩
                new InitialSkillGrant(46, 1, IsActive: true),   // 上挑
                new InitialSkillGrant(511, 1, IsActive: false), // 基础防具精通
            ],
            grants);
    }

    /// <remarks>
    /// The <c>[skill]</c> block is read from <c>[initial value]</c> and not from wherever the
    /// first one happens to be. The same file declares further blocks under its growth-stage
    /// sections, and taking the first match would silently pick one of those for any job whose
    /// initial section is not first.
    /// </remarks>
    [SkippableFact]
    public async Task Every_declared_job_answers_with_grants_of_its_own()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfInitialSkillDefinitionSource(provider);

        var byJob = new Dictionary<byte, IReadOnlyList<InitialSkillGrant>>();
        for (byte job = 0; job <= LastDeclaredJob; job++)
            byJob[job] = await source.GetAsync(job, CancellationToken.None);

        Assert.All(byJob, pair => Assert.NotEmpty(pair.Value));
        Assert.All(
            byJob,
            pair => Assert.All(pair.Value, grant => Assert.True(grant.Level > 0, $"job {pair.Key}")));
        Assert.True(
            byJob.Values.Select(grants => string.Join(",", grants)).Distinct().Count() > 1,
            "every job answered with the same list, so the job is not being used");
    }

    /// <summary>
    /// No job is created holding the same active skill twice.
    /// </summary>
    /// <remarks>
    /// Written down because a constraint now depends on it. The PvP skills table has always
    /// declared <c>UNIQUE (character_id, page_index, skill_id)</c> and the PvE one declared
    /// nothing; merging the two into one table carries that uniqueness onto page zero, which is
    /// where these grants land in the order they are read. If a job ever declared one of its
    /// actives twice, creating a character of that job would fail at the second row -- and it would
    /// fail in the game rather than here, because nothing else looks.
    ///
    /// The check is on the actives alone, because only the actives are written; a passive declared
    /// twice is held nowhere and cannot collide with anything.
    /// </remarks>
    [SkippableFact]
    public async Task No_job_declares_the_same_active_skill_twice()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfInitialSkillDefinitionSource(provider);

        for (byte job = 0; job <= LastDeclaredJob; job++)
        {
            var actives = (await source.GetAsync(job, CancellationToken.None))
                .Where(grant => grant.IsActive)
                .Select(grant => grant.SkillId)
                .ToArray();

            Assert.Equal(actives.Length, actives.Distinct().Count());
        }
    }

    [SkippableFact]
    public async Task A_job_the_archive_does_not_declare_has_no_grants()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfInitialSkillDefinitionSource(provider);

        Assert.Empty(await source.GetAsync(200, CancellationToken.None));
    }
}
