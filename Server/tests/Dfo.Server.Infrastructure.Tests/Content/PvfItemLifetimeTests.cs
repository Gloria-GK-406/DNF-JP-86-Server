using Dfo.Server.Infrastructure.Content;
using Dfo.Pvf.Tests.Support;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfItemLifetimeTests
{
    [Theory]
    [InlineData("[usable period]", -1)]
    [InlineData("[daily delete item]", 2)]
    [InlineData("[expiration date]", 10)]
    public void Invalid_declarations_are_rejected_instead_of_becoming_permanent(string tag, int value)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("stackable", "test.stk").Tag(tag).Int(value).End();
        var path = Path.Combine(Path.GetTempPath(), $"pvf-lifetime-{Guid.NewGuid():N}.pvf");
        try
        {
            File.WriteAllBytes(path, builder.Build());
            using var provider = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = path });
            Assert.Throws<InvalidDataException>(() => PvfItemLifetimeReader.Read(provider.Archive, "stackable/test.stk"));
        }
        finally { File.Delete(path); }
    }

    [SkippableTheory]
    [InlineData(490002925, 0, false)]
    [InlineData(490003187, 7, false)]
    [InlineData(10100300, 0, true)]
    [InlineData(28269, 0, false)]
    public async Task Actual_item_placement_exposes_the_declared_lifetime(int id, int days, bool daily)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var item = await new PvfItemPlacementDefinitionSource(provider).GetAsync(id, default);
        Assert.NotNull(item);
        Assert.Equal(days, item.Lifetime.Days);
        Assert.Equal(daily, item.Lifetime.DailyDelete);
        var absolute = id == 490002925
            ? new DateTimeOffset(2016, 11, 17, 6, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds() : 0;
        Assert.Equal(absolute, item.Lifetime.AbsoluteExpiration);
    }
}
