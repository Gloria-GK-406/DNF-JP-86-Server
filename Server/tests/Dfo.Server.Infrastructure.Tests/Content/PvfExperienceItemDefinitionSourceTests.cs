using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfExperienceItemDefinitionSourceTests
{
    [SkippableTheory]
    [InlineData(690001552, ExperienceItemGrantKind.Fixed, 1_000)]
    [InlineData(690001553, ExperienceItemGrantKind.Fixed, 10_000)]
    [InlineData(690001554, ExperienceItemGrantKind.Fixed, 100_000)]
    [InlineData(690001555, ExperienceItemGrantKind.PercentOfCurrentLevel, 1)]
    public async Task Tau_books_are_discovered_from_their_effect_declaration(
        int itemId,
        ExperienceItemGrantKind kind,
        int value)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var definition = await new PvfExperienceItemDefinitionSource(provider)
            .GetAsync(itemId, default);

        Assert.NotNull(definition);
        Assert.True(definition.IsReadable);
        Assert.Equal(kind, definition.GrantKind);
        Assert.Equal((uint)value, definition.Value);
        Assert.Equal(20, definition.MinimumLevel);
        Assert.Equal(84, definition.MaximumLevel);
    }

    [Fact]
    public async Task Synthetic_archive_recognizes_arbitrary_ids_and_quarantines_malformed_experience()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("stackable", "stackable.lst")
            .Int(777).Quoted("fixed.stk")
            .Int(778).Quoted("broken.stk")
            .Int(779).Quoted("other.stk")
            .Int(780).Quoted("large-percent.stk")
            .End();
        builder.AddScript("stackable", "fixed.stk")
            .Tag("[stackable type]").Quoted("[etc]").Int(0)
            .Tag("[minimum level]").Int(7)
            .Tag("[maximum level]").Int(70)
            .Tag("[increase status type]").Quoted("[expUp]").Int(4321)
            .Tag("[/increase status type]")
            .End();
        builder.AddScript("stackable", "broken.stk")
            .Tag("[stackable type]").Quoted("[etc]").Int(0)
            .Tag("[increase status type]").Quoted("[expPercentUp]")
            .Tag("[/increase status type]")
            .End();
        builder.AddScript("stackable", "other.stk")
            .Tag("[stackable type]").Quoted("[etc]").Int(0)
            .Tag("[increase status type]").Quoted("[strengthUp]").Int(1)
            .Tag("[/increase status type]")
            .End();
        builder.AddScript("stackable", "large-percent.stk")
            .Tag("[stackable type]").Quoted("[etc]").Int(0)
            .Tag("[increase status type]").Quoted("[expPercentUp]").Int(150)
            .Tag("[/increase status type]")
            .End();
        var path = Path.Combine(Path.GetTempPath(), $"experience-items-{Guid.NewGuid():N}.pvf");
        try
        {
            File.WriteAllBytes(path, builder.Build());
            using var provider = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = path });
            var source = new PvfExperienceItemDefinitionSource(provider);

            var fixedDefinition = await source.GetAsync(777, default);
            Assert.Equal(new(ExperienceItemGrantKind.Fixed, 4321, 7, 70, true), fixedDefinition);
            var broken = await source.GetAsync(778, default);
            Assert.NotNull(broken);
            Assert.False(broken.IsReadable);
            Assert.Null(await source.GetAsync(779, default));
            Assert.Equal(new(ExperienceItemGrantKind.PercentOfCurrentLevel, 150, 1,
                int.MaxValue, true), await source.GetAsync(780, default));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
