using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfRefillCacheTests
{
    [Fact]
    public async Task Inactive_declarations_still_validate_the_strict_item_list()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("etc", "chn_server_limititemusageinfo.etc").Tag("[refill item]")
            .Int(100).Int(0).Quoted("2010-01-01 06:00:00").Int(0).End();
        builder.AddScript("stackable", "stackable.lst").Int(100).Quoted("first.stk")
            .Int(100).Quoted("duplicate.stk").End();
        var path = Path.Combine(Path.GetTempPath(), $"refill-invalid-list-{Guid.NewGuid():N}.pvf");
        File.WriteAllBytes(path, builder.Build());
        try
        {
            using var provider = new PvfArchiveProvider(new() { ArchivePath = path });
            var source = new PvfEntryLifecycleContentSource(provider);
            await Assert.ThrowsAsync<ArgumentException>(() => source.GetDailyRefillRulesAsync(DateTimeOffset.UtcNow, default).AsTask());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Static_rules_are_reused_while_expiry_and_inactive_filtering_remain_per_read()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("etc", "chn_server_limititemusageinfo.etc").Tag("[refill item]")
            .Int(100).Int(2).Quoted("2030-01-01 06:00:00").Int(0)
            .Int(101).Int(0).Quoted("2030-01-01 06:00:00").Int(0)
            .Int(102).Int(1).Quoted("2010-01-01 06:00:00").Int(1).End();
        var list = builder.AddScript("stackable", "stackable.lst");
        for (var id = 100; id < 2100; id++) list.Int(id).Quoted($"item{id}.stk");
        list.End();
        builder.AddScript("equipment", "equipment.lst").Int(9000).Quoted("unused.equ").End();
        for (var id = 100; id <= 102; id++)
            builder.AddScript("stackable", $"item{id}.stk").Tag("[stack limit]").Int(100)
                .Tag("[stackable type]").Quoted("[consumable]").Int(0).End();
        var path = Path.Combine(Path.GetTempPath(), $"refill-cache-{Guid.NewGuid():N}.pvf");
        File.WriteAllBytes(path, builder.Build());
        try
        {
            using var provider = new PvfArchiveProvider(new() { ArchivePath = path });
            var source = new PvfEntryLifecycleContentSource(provider);
            var at = new DateTimeOffset(2026, 10, 27, 6, 0, 0, TimeSpan.FromHours(8));
            var declared = await source.GetDeclaredDailyRefillRulesAsync(at, default);
            Assert.Equal(3, declared.Count);
            var active = Assert.Single(await source.GetDailyRefillRulesAsync(at, default));
            Assert.Same(declared[0], active);
            Assert.Empty(await source.GetDailyRefillRulesAsync(declared[0].ExpiresAt, default));
            Assert.Same(active, Assert.Single(await source.GetDailyRefillRulesAsync(at, default)));

            // Repeated reads must not decode the 2,000-row index again. Budget covers returned lists,
            // and remains far below even one decoded copy of that list.
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) await source.GetDailyRefillRulesAsync(at, default);
            Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 100_000);
        }
        finally { File.Delete(path); }
    }
}
