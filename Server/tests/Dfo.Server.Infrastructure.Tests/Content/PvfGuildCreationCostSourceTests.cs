using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfGuildCreationCostSourceTests
{
    [SkippableFact]
    public void Reads_the_A21_guild_creation_fee_from_the_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        Assert.Equal(300_000, new PvfGuildCreationCostSource(provider).ReadGoldCost());
    }
}
