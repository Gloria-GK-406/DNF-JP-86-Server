using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The adventure-group tables, read from <c>etc/linksystem/charactermanage.etc</c>.
/// </summary>
/// <remarks>
/// The synthetic archive reproduces the A21 file token for token, defects included: <c>[manage
/// level max]</c> has no closing tag and the file ends on an unclosed <c>[contents revision]</c>.
/// The real-archive case checks the same figures against <c>Script.pvf</c> when it is present.
/// </remarks>
public sealed class PvfCharacterManageContentSourceTests
{
    internal static readonly int[] Thresholds = [1375, 5425, 10850, 21700, 37975, 54250, 75950, 101575, 133725, 175350];

    [Fact]
    public void The_a21_file_reads_whole_despite_its_unclosed_tags()
    {
        using var archive = new TemporaryArchive(A21File());

        AssertA21(new PvfCharacterManageContentSource(archive.Provider).Table);
    }

    [SkippableFact]
    public void The_real_archive_declares_the_a21_tables()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        AssertA21(new PvfCharacterManageContentSource(provider).Table);
    }

    [Theory]
    [InlineData("thresholds-fall")]
    [InlineData("max-past-thresholds")]
    [InlineData("band-not-triples")]
    public void A_table_that_cannot_be_right_is_refused(string defect)
    {
        using var archive = new TemporaryArchive(A21File(defect));
        var source = new PvfCharacterManageContentSource(archive.Provider);

        Assert.Contains("charactermanage", Assert.Throws<InvalidDataException>(() => source.Table).Message);
    }

    private static void AssertA21(CharacterManageTable table)
    {
        Assert.Equal(61, table.PointBands.Count);
        Assert.Equal(new CharacterManagePointBand(40, 40, 100), table.PointBands[0]);
        Assert.Equal(new CharacterManagePointBand(70, 70, 250), table.PointBands[30]);
        Assert.Equal(new CharacterManagePointBand(71, 71, 260), table.PointBands[31]);
        Assert.Equal(new CharacterManagePointBand(100, 100, 550), table.PointBands[60]);
        Assert.Equal(Thresholds, table.LevelThresholds);
        Assert.Equal(10, table.MaxLevel);
        Assert.Equal(10, table.ExperienceBonusPercent.Count);
        Assert.All(Enumerable.Range(1, 10), level => Assert.Equal(10, table.ExperienceBonusPercent[level]));
        Assert.Empty(table.GoldBonusPercent);
    }

    private static PvfArchiveBuilder A21File(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("etc/linksystem", "charactermanage.etc").Tag("[point bonus]");
        for (var level = 40; level <= 100; level++)
        {
            script.Int(level).Int(level);
            if (defect != "band-not-triples" || level != 100)
                script.Int(level <= 70 ? 100 + 5 * (level - 40) : 260 + 10 * (level - 71));
        }

        script.Tag("[/point bonus]").Tag("[manage level point]");
        foreach (var threshold in Thresholds)
            script.Int(defect == "thresholds-fall" && threshold == 21700 ? 100 : threshold);
        script.Tag("[/manage level point]")
            .Tag("[manage level max]").Int(defect == "max-past-thresholds" ? 11 : 10)
            .Tag("[exp bonus]");
        for (var level = 1; level <= 10; level++)
            script.Int(level).Int(10);
        script.Tag("[/exp bonus]")
            .Tag("[gold bonus]").Tag("[/gold bonus]")
            .Tag("[manage option]");
        for (var level = 1; level <= 10; level++)
            script.Int(level).Int(10 * level);
        return script.Tag("[/manage option]")
            .Tag("[contents revision]").Tag("[pvp]").Int(0).Tag("[fair pvp]").Int(0).Tag("[assault]").Int(0)
            .Tag("[contents revision]")
            .End();
    }

    private sealed class TemporaryArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"charactermanage-{Guid.NewGuid():N}.pvf");

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
