using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The skills a job's characters are created holding.
/// </summary>
/// <remarks>
/// Job 0 is the swordman, the job the tutorial was walked with on 2026-08-18 and the one whose
/// empty skill bar prompted this. Its declared set is fixed here in full because the set itself is
/// the answer: nothing inside the archive distinguishes a subset of it as "what level one gets".
/// Every entry requires level one, and there are three actives rather than the two that occupy the
/// attack keys, so neither the required level nor the active mark is a filter. See
/// protocol/patterns/data/probes/pvf-initial-skills-20260818.md.
/// </remarks>
public sealed class PvfInitialSkillDefinitionSourceTests
{
    [SkippableFact]
    public async Task The_swordman_is_created_holding_its_eight_declared_skills()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfInitialSkillDefinitionSource(provider);

        var grants = await source.GetAsync(0, CancellationToken.None);

        Assert.Equal(
            [
                new InitialSkillGrant(181, 2), // 精工
                new InitialSkillGrant(182, 2), // 纺织
                new InitialSkillGrant(184, 1), // 炼金
                new InitialSkillGrant(179, 7), // 物品分解
                new InitialSkillGrant(5, 1),   // 鬼斩
                new InitialSkillGrant(46, 1),  // 上挑
                new InitialSkillGrant(169, 1), // 后跳
                new InitialSkillGrant(174, 1), // 基础精通
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
        for (byte job = 0; job <= 12; job++)
            byJob[job] = await source.GetAsync(job, CancellationToken.None);

        Assert.All(byJob, pair => Assert.NotEmpty(pair.Value));
        Assert.All(
            byJob,
            pair => Assert.All(pair.Value, grant => Assert.True(grant.Level > 0, $"job {pair.Key}")));
        Assert.True(
            byJob.Values.Select(grants => string.Join(",", grants)).Distinct().Count() > 1,
            "every job answered with the same list, so the job is not being used");
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
