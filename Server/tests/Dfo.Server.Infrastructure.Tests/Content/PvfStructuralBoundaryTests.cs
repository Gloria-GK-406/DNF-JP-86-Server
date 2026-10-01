using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfStructuralBoundaryTests
{
    [Theory]
    [InlineData("[exp reward table]", "plain", true)]
    [InlineData("[exp reward table]", "pairs", true)]
    [InlineData("[exp reward table]", "negative", false)]
    [InlineData("[exp reward table]", "tail", false)]
    [InlineData("[exp reward table]", "shifted", false)]
    [InlineData("[gold reward table]", "plain", true)]
    [InlineData("[gold reward table]", "pairs", true)]
    [InlineData("[gold reward table]", "negative", false)]
    [InlineData("[gold reward table]", "tail", false)]
    [InlineData("[gold reward table]", "shifted", false)]
    public void Reward_table_is_complete_before_level_prices_are_projected(string field, string shape, bool valid)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("n_Quest", "questParameter.etc");
        if (field == "[gold reward table]") script.Tag("[exp reward table]").Int(100).Int(-1);
        script.Tag(field).Int(100);
        if (shape == "pairs") script.Int(-1).Int(126).Int(-1);
        else if (shape == "plain") script.Int(126);
        else if (shape == "negative") script.Int(-2).Int(126).Int(-1);
        else if (shape == "tail") script.Int(-1).Int(126);
        else script.Int(126).Int(-1).Int(-1);
        script.End();
        using var file = new BuiltArchive(builder);
        var source = new PvfQuestParameterSource(file.Provider);
        if (valid)
        {
            var parameters = source.Load();
            Assert.Equal(new[] { 100, 126 }, field == "[exp reward table]" ? parameters.ExperienceByLevel : parameters.GoldByLevel);
        }
        else
        {
            var error = Assert.Throws<InvalidDataException>(() => source.Load());
            Assert.Contains(field, error.Message);
            Assert.Contains("n_Quest/questParameter.etc", error.Message);
        }
    }

    [Theory]
    [InlineData("unclosed")]
    [InlineData("reopened")]
    [InlineData("orphan-close")]
    [InlineData("missing-id")]
    [InlineData("missing-map")]
    [InlineData("marker-as-map")]
    [InlineData("rectangle-before-header")]
    [InlineData("rectangle-string")]
    [InlineData("rectangle-short")]
    public async Task Broken_area_cannot_become_a_missing_area_or_an_incomplete_usable_record(string shape)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("town", "town.lst").Int(1).Quoted("test.twn").End();
        var script = builder.AddScript("town", "test.twn").Tag("[name]").Quoted("test");
        if (shape == "orphan-close") script.Tag("[/area]");
        else
        {
            script.Tag("[area]");
            if (shape == "missing-id") script.Quoted("test.map");
            else if (shape == "rectangle-before-header") script.Quoted("[minimap rect]");
            else
            {
                script.Int(0);
                if (shape == "marker-as-map") script.Quoted("[normal]");
                else if (shape != "missing-map") script.Quoted("test.map");
            }
            if (shape.StartsWith("rectangle-", StringComparison.Ordinal) && shape != "rectangle-before-header")
            {
                script.Quoted("[minimap rect]").Int(1).Int(2).Int(3);
                if (shape == "rectangle-string") script.Quoted("four");
                script.Quoted("[/minimap rect]");
            }
            if (shape == "reopened") script.Tag("[area]").Int(1).Quoted("another.map");
            if (shape != "unclosed") script.Tag("[/area]");
        }
        script.End();
        using var file = new BuiltArchive(builder);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfTownDefinitionSource(file.Provider).GetAllAsync(default).AsTask());
        Assert.Contains("town/test.twn", error.Message);
    }

    [Fact]
    public async Task Complete_area_preserves_opaque_permission_and_quoted_modifiers()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("town", "town.lst").Int(1).Quoted("test.twn").End();
        builder.AddScript("town", "test.twn").Tag("[area]").Int(0).Quoted("test.map")
            .Tag("[permission]").Tag("[unknown permission]").Quoted("opaque").Tag("[/permission]")
            .Quoted("[minimap rect]").Int(1).Int(2).Int(3).Int(4).Quoted("[/minimap rect]")
            .Quoted("[unknown modifier]").Int(99).Quoted("[gate]").Int(11).Int(22).Tag("[/area]").End();
        using var file = new BuiltArchive(builder);
        var town = Assert.Single(await new PvfTownDefinitionSource(file.Provider).GetAllAsync(default));
        var area = Assert.Single(town.Areas);
        Assert.Equal((0, "test.map", 11, 22), (area.AreaId, area.MapPath, area.Operand1, area.Operand2));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Independent_geometry_declarations_cannot_complete_each_others_partial_rows(bool transit, bool complete)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("map", "test.map");
        var field = transit ? "[town movable area]" : "[virtual movable area]";
        var closing = transit ? "[/town movable area]" : "[/virtual movable area]";
        var width = transit ? 6 : 4;
        for (var row = 0; row < 2; row++)
        {
            script.Tag(field);
            for (var value = 0; value < (complete ? width : width / 2); value++) script.Int(value);
            script.Tag(closing);
        }
        // The other family's tags must exist so a missing tag is not the failure under test.
        script.Tag(transit ? "[virtual movable area]" : "[town movable area]")
            .Tag(transit ? "[/virtual movable area]" : "[/town movable area]").End();
        using var file = new BuiltArchive(builder);
        var source = new PvfTownDefinitionSource(file.Provider);
        if (complete)
        {
            var geometry = await source.GetMapGeometryAsync("test.map", default);
            Assert.NotNull(geometry);
            Assert.Equal(2, transit ? geometry.TransitAreas.Count : geometry.WalkableAreas.Count);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => source.GetMapGeometryAsync("test.map", default).AsTask());
            Assert.Contains(field, error.Message);
            Assert.Contains("map/test.map", error.Message);
        }
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pvf-structural-{Guid.NewGuid():N}.pvf");
        internal PvfArchiveProvider Provider { get; }
        internal BuiltArchive(PvfArchiveBuilder builder)
        {
            File.WriteAllBytes(path, builder.Build());
            Provider = new(new PvfContentOptions { ArchivePath = path });
        }
        public void Dispose() { Provider.Dispose(); File.Delete(path); }
    }
}
