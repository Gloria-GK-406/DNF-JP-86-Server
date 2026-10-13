using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEquipmentResealContentSourceTests
{
    [SkippableFact]
    public void Real_table_preserves_grade_rarity_and_previous_count_weights()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentResealContentSource(provider);
        var table = source.Load();
        Assert.Equal(new[] { 1, 1, 1, 2, 2, 3, 3, 4, 4 }, table.GradeBands);
        Assert.Equal(new[] { 1, 2, 3, 4, 8, 8, 6 }, table.RarityWeights);
        Assert.Equal(new[] { 0, 0, 1, 1, 1, 1, 1 }, table.RepeatWeights);
        Assert.Equal(new[] { 2, 3, 6 }, table.AllowedRarities);
        Assert.Same(table, source.Load());
    }

    [Theory]
    [InlineData("grade")]
    [InlineData("rarity-length")]
    [InlineData("rarity-weight")]
    [InlineData("repeat")]
    [InlineData("allowed")]
    public void Invalid_table_is_not_cached_or_accepted(string defect)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("etc", "reseal.etc").Tag("[grade]").Int(defect == "grade" ? 0 : 1)
            .Tag("[/grade]").Tag("[rarity weight]");
        for (var i = 0; i < (defect == "rarity-length" ? 6 : 7); i++) script.Int(defect == "rarity-weight" ? -1 : 1);
        script.Tag("[/rarity weight]").Tag("[extended rarity weight]");
        for (var i = 0; i < 7; i++) script.Int(defect == "repeat" ? -1 : 0);
        script.Tag("[/extended rarity weight]").End();
        var wax = builder.AddScript("stackable/cash", "candle_goldwax.stk").Tag("[Rarity possible explain]");
        for (var i = 0; i < 7; i++) wax.Int(defect == "allowed" ? 2 : 1);
        wax.Tag("[/Rarity possible explain]").End();
        var path = Path.Combine(Path.GetTempPath(), $"reseal-{Guid.NewGuid():N}.pvf");
        try
        {
            File.WriteAllBytes(path, builder.Build());
            using var provider = new PvfArchiveProvider(new() { ArchivePath = path });
            var source = new PvfEquipmentResealContentSource(provider);
            for (var i = 0; i < 2; i++) Assert.Contains("reseal", Assert.Throws<InvalidDataException>(() => source.Load()).Message);
        }
        finally { File.Delete(path); }
    }
}
