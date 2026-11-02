using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.World;

internal sealed class FixedDrawMultipliers(params (DropDrawEntry Entry, int Multiplier)[] values) : IDropDrawMultiplierDomainService
{
    public ValueTask<DropDrawMultipliers> CurrentAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new DropDrawMultipliers(values.ToDictionary(value => value.Entry, value => value.Multiplier)));

    public ValueTask SetAsync(DropDrawEntry entry, int multiplier, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

[Trait("Category", "Unit")]
public sealed class DropDrawMultiplierTests
{
    private sealed class MemoryStore : IDropDrawMultiplierPersistence
    {
        public Dictionary<string, int> Rows { get; } = [];

        public ValueTask<IReadOnlyList<(string Key, int Multiplier)>> ReadAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<(string, int)>>(this.Rows.Select(row => (row.Key, row.Value)).ToArray());

        public ValueTask SaveAsync(string key, int multiplier, CancellationToken cancellationToken)
        {
            this.Rows[key] = multiplier;
            return ValueTask.CompletedTask;
        }
    }

    private static DropDrawMultiplierDomainService Service(MemoryStore store) =>
        new(store, NullLogger<DropDrawMultiplierDomainService>.Instance);

    [Fact]
    public async Task Unset_entries_stay_at_one_and_setting_one_leaves_the_others()
    {
        var store = new MemoryStore();
        var service = Service(store);
        await service.SetAsync(DropDrawEntry.World, 20, default);
        var current = await service.CurrentAsync(default);
        Assert.Equal(20, current.For(DropDrawEntry.World));
        Assert.All(DropDrawEntries.All.Where(entry => entry != DropDrawEntry.World),
            entry => Assert.Equal(1, current.For(entry)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Values_outside_the_range_are_rejected(int multiplier)
    {
        var store = new MemoryStore();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await Service(store).SetAsync(DropDrawEntry.World, multiplier, default));
        Assert.Empty(store.Rows);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task The_range_ends_are_accepted(int multiplier)
    {
        var service = Service(new MemoryStore());
        await service.SetAsync(DropDrawEntry.MonsterGold, multiplier, default);
        Assert.Equal(multiplier, (await service.CurrentAsync(default)).For(DropDrawEntry.MonsterGold));
    }

    [Fact]
    public async Task Unknown_or_invalid_rows_are_ignored()
    {
        var store = new MemoryStore();
        store.Rows["retired.entry"] = 5;
        store.Rows["world"] = 500;
        var current = await Service(store).CurrentAsync(default);
        Assert.Equal(1, current.For(DropDrawEntry.World));
    }

    [Fact]
    public void Every_entry_has_a_unique_key_that_parses_back()
    {
        var entries = Enum.GetValues<DropDrawEntry>();
        Assert.Equal(entries.Length, entries.Select(DropDrawEntries.KeyOf).Distinct().Count());
        foreach (var entry in entries)
        {
            Assert.True(DropDrawEntries.TryParse(DropDrawEntries.KeyOf(entry), out var parsed));
            Assert.Equal(entry, parsed);
        }
    }

    [Fact]
    public void Round_zero_keeps_the_seed_and_later_rounds_differ_by_round_and_entry()
    {
        const ulong seed = 0x1234_5678_9ABC_DEF0;
        Assert.Equal(seed, DropRandom.ForRound(seed, default));
        Assert.Equal(seed, DropRandom.ForRound(seed, new(DropDrawEntry.World, 0)));
        var derived = new[]
        {
            DropRandom.ForRound(seed, new(DropDrawEntry.World, 1)),
            DropRandom.ForRound(seed, new(DropDrawEntry.World, 2)),
            DropRandom.ForRound(seed, new(DropDrawEntry.MonsterGold, 1)),
        };
        Assert.Equal(3, derived.Distinct().Count());
        Assert.DoesNotContain(seed, derived);
    }
}
