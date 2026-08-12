using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEntryLifecycleContentSourceTests
{
    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 4, 6, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void DailyRefill_ParsesCompleteTuplesSkipsInvalidRowsAndRejectsTruncatedSection()
    {
        string[] values =
        [
            "2680738", "2", "2030-01-01 06:00:00", "0",
            "123", "0", "2030-01-01 06:00:00", "0",
            "124", "1", "2015-01-01 06:00:00", "0",
            "690001556", "1", "2030-01-01 06:00:00", "1",
        ];

        var rules = PvfEntryLifecycleContentSource.Parse(values, PreparedAt,
            _ => new PvfEntryLifecycleContentSource.RefillItemMetadata(100, 2, 65, 120));

        Assert.Equal([2680738, 690001556], rules.Select(value => value.ItemId));
        Assert.Equal(2, PvfEntryLifecycleContentSource.CalculateGrant(rules[0], 0));
        Assert.Equal(1, PvfEntryLifecycleContentSource.CalculateGrant(rules[1], 99));
        Assert.Throws<FormatException>(() => PvfEntryLifecycleContentSource.Parse(
            ["1", "2", "2030-01-01 06:00:00"], PreparedAt,
            _ => new PvfEntryLifecycleContentSource.RefillItemMetadata(100, 2, 65, 120)));

        Assert.Equal(7, PvfEntryLifecycleContentSource.ResolveStackableItemKind(1, "[creature]"));
        Assert.Equal(9, PvfEntryLifecycleContentSource.ResolveStackableItemKind(1, "[avatar emblem]"));
        Assert.Equal(10, PvfEntryLifecycleContentSource.ResolveStackableItemKind(1, "[material expert job]"));
        Assert.Equal(4, PvfEntryLifecycleContentSource.ResolveStackableItemKind(1, "[quest]"));
        Assert.Equal(3, PvfEntryLifecycleContentSource.ResolveStackableItemKind(100, "[material]"));
        Assert.Equal(11, PvfEntryLifecycleContentSource.ResolveStackableItemKind(3033, "[material]"));
        Assert.Equal(2, PvfEntryLifecycleContentSource.ResolveStackableItemKind(1, "[consumable]"));
    }

    [SkippableFact]
    public async Task DailyRefill_RealArchiveProducesAuthoritativeRulesWithStackLimits()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEntryLifecycleContentSource(provider);

        var rules = await source.GetDailyRefillRulesAsync(PreparedAt, CancellationToken.None);
        Assert.NotEmpty(rules);
        Assert.All(rules, rule =>
        {
            Assert.True(rule.ItemId > 0);
            Assert.True(rule.Quantity > 0);
            Assert.True(rule.ExpiresAt > PreparedAt);
            Assert.Contains(rule.ItemKind, new byte[] { 1, 2, 3, 4, 7, 9, 10, 11 });
            Assert.Equal(177, rule.SlotStart);
            Assert.Equal(232, rule.SlotEnd);
        });
        Assert.Equal(
            new Dictionary<int, byte>
            {
                [2680738] = 4,
                [2749110] = 4,
                [10002520] = 4,
                [10093393] = 4,
                [10003235] = 4,
                [690001556] = 4,
            },
            rules.ToDictionary(rule => rule.ItemId, rule => rule.ItemKind));
    }
}
