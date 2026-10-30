using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfLotteryDefinitionCacheTests
{
    [SkippableFact]
    public async Task Startup_caches_every_pot_and_reads_without_an_open_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var ids = new List<int>();
        foreach (var row in PvfListFile.Read(provider.Archive, "stackable/stackable.lst"))
        {
            var index = provider.Archive.FindFile("stackable/" + row.RelativePath);
            if (index < 0) continue;
            var script = provider.Archive.OpenScript(index);
            var nextIsType = false;
            while (script.Read(out var token))
            {
                if (nextIsType)
                {
                    if (token.IsString && provider.Archive.ResolveString(token.Value) == "[upgradable legacy]") ids.Add(row.Id);
                    break;
                }
                nextIsType = token.Kind == PvfTokenKind.Identifier
                    && provider.Archive.ResolveString(token.Value) == "[stackable type]";
            }
        }
        Assert.NotEmpty(ids);
        var source = new PvfPackageOpeningDefinitionSource(provider,
            new PvfCeraShopDefinitionSource(provider), new PvfItemPlacementDefinitionSource(provider));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.InitializeAsync(new(true)).AsTask());
        Assert.Equal(ids.Count, await source.InitializeAsync());
        var definitions = new Dictionary<int, OpeningPackageDefinition>();
        foreach (var id in ids) definitions.Add(id, Assert.IsType<OpeningPackageDefinition>(await source.GetAsync(id, default)));
        provider.Dispose();
        Assert.Equal(ids.Count, await source.InitializeAsync());
        Assert.Contains("-999", Assert.Throws<Dfo.Server.Data.Inventory.OpeningRejectedException>(() => source.GetRequired(-999)).Message);
        Assert.Equal(98002, source.GetRequired(7110).CumulativeWeights[^1]);
        foreach (var id in ids)
        {
            var definition = await source.GetAsync(id, default);
            Assert.Same(definitions[id], definition);
            Assert.Same(definition, source.GetRequired(id));
            if (!definition!.CumulativeWeights.IsEmpty)
                Assert.Equal(definition.Pools[0].Choices.Sum(x => (long)x.Weight), definition.CumulativeWeights[^1]);
            Assert.Equal(OpeningPackageKind.Lottery, definition!.Kind);
        }
    }
}
