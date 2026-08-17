using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>The stats a job's archive entry gives a level-one character.</summary>
public sealed class PvfCharacterStatDefinitionSourceTests
{
    [SkippableFact]
    public async Task Real_archive_projects_the_swordmans_initial_stats()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCharacterStatDefinitionSource(provider);

        var stats = await source.GetInitialAsync(0, CancellationToken.None);

        // character/Swordman/Swordman.chr, [initial value], each number times ten.
        Assert.Equal(1800, stats.HpMax);
        Assert.Equal(1400, stats.MpMax);
        Assert.Equal(75, stats.PhysicalAttack);
        Assert.Equal(75, stats.PhysicalDefense);
        Assert.Equal(45, stats.MagicalAttack);
        Assert.Equal(45, stats.MagicalDefense);
        Assert.Equal(480000, stats.InventoryLimit);
        Assert.Equal(500, stats.MpRegenSpeed);
        Assert.Equal(8500, stats.MoveSpeed);
        Assert.Equal(8500, stats.AttackSpeed);
        Assert.Equal(7000, stats.CastSpeed);
        Assert.Equal(6000, stats.HitRecovery);
        Assert.Equal(4300, stats.JumpPower);
        Assert.Equal(500000, stats.Weight);
    }

    [SkippableFact]
    public async Task Real_archive_keeps_a_negative_resistance_and_leaves_an_undeclared_one_at_zero()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCharacterStatDefinitionSource(provider);

        var swordman = await source.GetInitialAsync(0, CancellationToken.None);
        var knight = await source.GetInitialAsync(12, CancellationToken.None);

        // The swordman declares two resistances, one of them negative; the knight declares all
        // four. Zero here is the archive's answer, not a parse that gave up.
        Assert.Equal(200, swordman.DarkResistance);
        Assert.Equal(-200, swordman.LightResistance);
        Assert.Equal(0, swordman.FireResistance);
        Assert.Equal(0, swordman.WaterResistance);
        Assert.Equal(50, knight.FireResistance);
        Assert.Equal(50, knight.WaterResistance);
        Assert.Equal(50, knight.DarkResistance);
        Assert.Equal(50, knight.LightResistance);
    }

    [SkippableFact]
    public async Task Real_archive_reads_the_initial_block_rather_than_the_growth_that_follows_it()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCharacterStatDefinitionSource(provider);

        var stats = await source.GetInitialAsync(0, CancellationToken.None);

        // [growtype 1] repeats every stat tag with the per-level increment. A scan that ran past
        // the initial block would report 450 -- the swordman's HP per level -- as his starting HP.
        Assert.NotEqual(450, stats.HpMax);
        Assert.NotEqual(250, stats.MpMax);
    }

    [SkippableFact]
    public async Task Real_archive_gives_every_declared_job_a_usable_starting_body()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCharacterStatDefinitionSource(provider);

        // Character creation accepts job ids up to twelve, so every one of them has to answer.
        for (byte job = 0; job <= 12; job++)
        {
            var stats = await source.GetInitialAsync(job, CancellationToken.None);
            Assert.True(stats.HpMax > 0, $"job {job} starts with no HP");
            Assert.True(stats.MpMax > 0, $"job {job} starts with no MP");
            Assert.True(stats.MoveSpeed > 0, $"job {job} cannot move");
        }
    }

    [SkippableFact]
    public async Task Real_archive_rejects_a_job_it_does_not_declare()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCharacterStatDefinitionSource(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await source.GetInitialAsync(200, CancellationToken.None));
    }
}
