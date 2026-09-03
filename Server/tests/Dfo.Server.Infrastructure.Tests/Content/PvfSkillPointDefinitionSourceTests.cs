using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfSkillPointDefinitionSourceTests
{
    [SkippableFact]
    public async Task Real_archive_projects_totals_free_grants_and_learned_skill_costs()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfSkillPointDefinitionSource(provider);

        var result = await source.GetAsync(0, 1, 86, [1], CancellationToken.None);

        Assert.Equal(11010, result.TotalSp);
        Assert.Equal(37, result.TotalTp);
        Assert.InRange(result.FreeGrants.Count, 14, 14);
        Assert.Equal(new SkillPointGrantDefinition(179, 7, IsInitial: true), result.FreeGrants[0]);
        Assert.True(result.Skills.TryGetValue(1, out var skill));
        Assert.Equal([20], skill!.Costs);
        Assert.Equal(5, skill.OrdinaryMaximumLevel);
    }

    [SkippableFact]
    public async Task Real_archive_preserves_initial_skill_source_order_for_normal_and_dark_knight_jobs()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfSkillPointDefinitionSource(provider);
        var normal = await source.GetAsync(1, 1, 86, [], CancellationToken.None);
        var darkKnight = await source.GetAsync(9, 1, 86, [], CancellationToken.None);

        Assert.Equal([179, 174, 169, 46, 5, 511],
            normal.FreeGrants.Where(value => value.IsInitial).Take(6).Select(value => value.SkillId));
        Assert.Equal([118, 119, 120, 121, 122, 123],
            darkKnight.FreeGrants.Where(value => value.IsInitial).Take(6).Select(value => value.SkillId));
    }

    [SkippableFact]
    public void Real_archive_projects_expert_contract_premium_types()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var result = new PvfExpertSkillContractDefinitionSource(provider)
            .GetOverSkillPremiumTypes();

        Assert.Equal([27], result.Order());
        Assert.DoesNotContain(0, result);
    }

    /// <summary>
    /// The same question is parsed once, however many times it is asked.
    /// </summary>
    /// <remarks>
    /// <b>Identity is the assertion, and it is what makes this a proof rather than a hope.</b>
    /// Answering this question means walking five kinds of file in the archive and building a new
    /// set out of what they say -- so a second walk would hand back a second object. Equal, but
    /// not the same one. That the same instance comes back is therefore only possible if the
    /// archive was not read again.
    /// <para>
    /// It matters because of who asks now. Every kill, clear and quest hand-in sends a frame that
    /// carries what the character has left to spend, and that figure is worked out from this. A
    /// full parse of the archive on the kill path is not a slow answer, it is a different kind of
    /// operation to the one that used to be there.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Real_archive_is_walked_once_per_distinct_question()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfSkillPointDefinitionSource(provider);

        var first = await source.GetAsync(0, 1, 86, [1, 2], CancellationToken.None);

        Assert.Same(first, await source.GetAsync(0, 1, 86, [1, 2], CancellationToken.None));

        // The learned skills arrive as a collection, which promises no order and may repeat -- and
        // the answer depends on neither, because the ids are deduplicated before anything is read.
        // Two callers naming the same skills differently must not each pay for a walk.
        Assert.Same(first, await source.GetAsync(0, 1, 86, [2, 1, 2], CancellationToken.None));

        // And it is a cache, not a single remembered answer: a different question is a different
        // walk. Level is the one that moves in play, so it is the one asserted.
        Assert.NotSame(first, await source.GetAsync(0, 1, 85, [1, 2], CancellationToken.None));
        Assert.NotSame(first, await source.GetAsync(1, 1, 86, [1, 2], CancellationToken.None));
        Assert.NotSame(first, await source.GetAsync(0, 2, 86, [1, 2], CancellationToken.None));
        Assert.NotSame(first, await source.GetAsync(0, 1, 86, [1], CancellationToken.None));
    }

    /// <summary>
    /// Callers racing on one question all end up with one answer.
    /// </summary>
    /// <remarks>
    /// The source is a singleton and the connections asking it are not serialised, so the store
    /// behind it has to be safe for concurrent readers and writers. Asserted on identity again:
    /// whichever caller's walk lands first is the answer every one of them is handed, so a store
    /// that let two answers stand would be caught here rather than in a session six months from
    /// now holding a set nobody else has.
    /// </remarks>
    [SkippableFact]
    public async Task Racing_callers_are_handed_one_answer()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfSkillPointDefinitionSource(provider);

        var answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            Task.Run(async () =>
                await source.GetAsync(0, 1, 40, [1, 2, 3], CancellationToken.None))));

        Assert.All(answers, answer => Assert.Same(answers[0], answer));
    }

    [SkippableFact]
    public async Task Real_archive_distinguishes_inapplicable_skills_from_applicable_zero_level_caps()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfSkillPointDefinitionSource(provider);

        var result = await source.GetAsync(
            job: 0, packedGrowType: 1, characterLevel: 1,
            Enumerable.Range(1, 1000).ToArray(), CancellationToken.None);

        Assert.Contains(result.Skills.Values, value => value.OrdinaryMaximumLevel is null);
        Assert.Contains(result.Skills.Values, value => value.OrdinaryMaximumLevel == 0);
        Assert.Equal(2, result.Skills.Values
            .Where(value => value.OrdinaryMaximumLevel is null).Min(value => value.SkillId));
        Assert.Equal(1, result.Skills.Values
            .Where(value => value.OrdinaryMaximumLevel == 0).Min(value => value.SkillId));
    }
}
