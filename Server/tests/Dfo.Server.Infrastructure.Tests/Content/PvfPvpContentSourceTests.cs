using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfPvpContentSourceTests
{
    [SkippableFact]
    public void Grade_table_reads_every_grade_with_its_bounds_and_outcome_experience()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var catalog = new PvfPvpGradeDefinitionSource(provider).Load();

        Assert.Equal(20, catalog.MaximumGrade);
        Assert.Equal(Enumerable.Range(0, 21).Select(x => (byte)x), catalog.Grades.Select(x => x.Grade));
        Assert.Equal(new PvpGradeStep(0, 0, 12000, 1000, 0), catalog.Grades[0]);
        Assert.Equal(new PvpGradeStep(1, 12000, 27000, 1000, -429), catalog.Grades[1]);
        Assert.Equal(new PvpGradeStep(10, 709000, 949000, 1000, -756), catalog.Grades[10]);
        Assert.Equal(new PvpGradeStep(19, 4849000, 5649000, 1000, -1044), catalog.Grades[19]);
        // The top grade's upper bound is the archive's sentinel row, not a grade anyone reaches.
        Assert.Equal(new PvpGradeStep(20, 5649000, 9999999, 1000, -1076), catalog.Grades[20]);
        Assert.All(catalog.Grades.Skip(1), x => Assert.Equal(catalog.Grades[x.Grade - 1].UpperBound, x.LowerBound));
        Assert.Equal(TimeSpan.FromSeconds(180), catalog.NormalBattleTime);
        Assert.Equal(TimeSpan.FromSeconds(120), catalog.RelayBattleTime);
    }

    [SkippableFact]
    public void Map_order_is_read_in_archive_order()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var catalog = new PvfPvpMapDefinitionSource(provider).Load();

        Assert.Equal([50000, 50042, 50041, 50036, 50025, 50035], catalog.Order);
    }

    [SkippableFact]
    public async Task Both_sources_warm_up_from_the_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        Assert.Equal(21, await new PvfPvpGradeDefinitionSource(provider).WarmAsync(default));
        Assert.Equal(6, await new PvfPvpMapDefinitionSource(provider).WarmAsync(default));
    }

    [Fact]
    public void A_synthetic_grade_table_reads_like_the_archive()
    {
        using var archive = new TemporaryArchive(GradeFile());
        var catalog = new PvfPvpGradeDefinitionSource(archive.Provider).Load();

        Assert.Equal(2, catalog.MaximumGrade);
        Assert.Equal(new PvpGradeStep(2, 300, 999, 50, -30), catalog.Grades[2]);
        Assert.Equal(TimeSpan.FromSeconds(180), catalog.NormalBattleTime);
    }

    [Theory]
    [InlineData("no-grade-point")]
    [InlineData("no-battle-time")]
    [InlineData("top-grade-not-below-sentinel")]
    [InlineData("thresholds-fall")]
    [InlineData("positive-loss")]
    public void A_grade_table_missing_a_required_part_is_refused_on_load(string defect)
    {
        using var archive = new TemporaryArchive(GradeFile(defect));
        var source = new PvfPvpGradeDefinitionSource(archive.Provider);

        Assert.Contains("pvp_ref", Assert.Throws<InvalidDataException>(() => source.Load()).Message);
    }

    [Theory]
    [InlineData("no-order")]
    [InlineData("unlisted-map")]
    [InlineData("only-random")]
    public void A_map_table_missing_a_required_part_is_refused_on_load(string defect)
    {
        using var archive = new TemporaryArchive(MapFile(defect));
        var source = new PvfPvpMapDefinitionSource(archive.Provider);

        Assert.Contains("pvpmapbasicdata", Assert.Throws<InvalidDataException>(() => source.Load()).Message);
    }

    [Fact]
    public void A_missing_file_is_refused_on_load()
    {
        using var archive = new TemporaryArchive(MapFile());
        Assert.Throws<InvalidDataException>(() => new PvfPvpGradeDefinitionSource(archive.Provider).Load());
    }

    private static PvfArchiveBuilder GradeFile(string? defect = null)
    {
        var script = new PvfArchiveBuilder().AddScript("etc", "pvp_ref.etc")
            .Tag("[pvp experience max grade]").Int(3)
            .Tag("[pvp experience]").Int(1).Int(100).Int(2).Int(defect == "thresholds-fall" ? 50 : 300).Int(3).Int(999)
            .Tag("[/pvp experience]");
        if (defect != "no-grade-point")
        {
            script.Tag("[pvp grade point]").Int(0).Int(50).Int(0).Int(1).Int(50).Int(-20).Int(2).Int(50)
                .Int(defect == "positive-loss" ? 30 : -30).Tag("[/pvp grade point]");
        }
        script.Tag("[last season pvp reward]").Tag("[pvp grade]").Int(2).Int(1).Tag("[/pvp grade]")
            .Tag("[pvp grade]").Int(1).Int(0).Tag("[/pvp grade]").Tag("[/last season pvp reward]")
            .Tag("[pvp experience loss]").Int(defect == "top-grade-not-below-sentinel" ? 3 : 2).Int(0);
        if (defect != "no-battle-time") script.Tag("[pvp normal battle time]").Int(180000);
        return script.Tag("[pvp relay battle time]").Int(120000).End();
    }

    private static PvfArchiveBuilder MapFile(string? defect = null)
    {
        var script = new PvfArchiveBuilder().AddScript("etc", "pvpmapbasicdata.etc");
        if (defect != "no-order")
        {
            script.Tag("[pvp map order]").Int(50000);
            if (defect != "only-random") script.Int(defect == "unlisted-map" ? 50999 : 50042);
            script.Tag("[/pvp map order]");
        }
        foreach (var map in new[] { 50000, 50042 })
            script.Tag("[pvp map basic data]").Int(map).Int(1).Int(2).Int(1).Int(1).Int(1).Int(0).Tag("[/pvp map basic data]");
        return script.End();
    }

    private sealed class TemporaryArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pvp-{Guid.NewGuid():N}.pvf");

        public TemporaryArchive(PvfArchiveBuilder builder)
        {
            File.WriteAllBytes(path, builder.Build());
            Provider = new PvfArchiveProvider(new() { ArchivePath = path });
        }

        public PvfArchiveProvider Provider { get; }

        public void Dispose()
        {
            Provider.Dispose();
            File.Delete(path);
        }
    }
}
