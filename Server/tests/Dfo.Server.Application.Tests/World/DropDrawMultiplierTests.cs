using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.World;

internal sealed class FixedDrawMultipliers(params (DropDrawEntry Entry, int Multiplier)[] values) : IDropDrawMultiplierDomainService
{
    public ValueTask<DropDrawMultipliers> CurrentAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new DropDrawMultipliers(values.ToDictionary(value => value.Entry, value => value.Multiplier)));
}

[Trait("Category", "Unit")]
public sealed class DropDrawMultiplierTests
{
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
