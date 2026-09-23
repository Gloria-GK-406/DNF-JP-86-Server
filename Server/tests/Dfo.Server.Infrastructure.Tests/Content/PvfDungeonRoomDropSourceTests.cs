using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;
using Dfo.Pvf.Tokens;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfDungeonRoomDropSourceTests
{
    [SkippableFact]
    public void All_A21_map_action_sections_parse_without_losing_object_boundaries()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var archive = provider.Archive;
        var count = 0;
        for (var file = 0; file < archive.FileCount; file++)
        {
            var entry = archive.GetEntry(file);
            if (!entry.IsScript || !archive.ResolveString(entry.NameOffset).EndsWith(".map", StringComparison.Ordinal)) continue;
            var reader = archive.OpenScript(file);
            var values = new List<string>();
            var active = false;
            while (reader.Read(out var token))
            {
                if (token.Kind == PvfTokenKind.Identifier)
                {
                    if (archive.ResolveString(token.Value) == "[special passive object]") { active = true; count++; continue; }
                    if (active) break;
                }
                else if (active) values.Add(token.IsString ? archive.ResolveString(token.Value)
                    : token.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            PvfDungeonRoomDropSource.ParseActions(values, file);
        }
        Assert.Equal(5473, count);
    }

    [SkippableTheory]
    [InlineData(10000, 61001)]
    [InlineData(10005, 61032)]
    public async Task Tutorial_declaration_preserves_guaranteed_shoulder(int dungeon, int map)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomDropSource(provider);
        var action = Assert.Single(await source.GetAsync(dungeon, map, CancellationToken.None));
        Assert.Equal(0, action.ObjectIndex);
        Assert.Equal(1, action.SpecificAttempts);
        Assert.Equal(0, action.RandomAttempts);
        var entry = Assert.Single(action.Entries);
        Assert.Equal(14400, entry.ItemId);
        Assert.Equal(25, entry.Durability);
        Assert.Equal(10000, entry.Weight);
    }

    [SkippableFact]
    public async Task Three_in_map_means_three_attempts_and_the_full_pool_is_retained()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonRoomDropSource(provider);
        var drops = await source.GetAsync(144, 55611, CancellationToken.None);
        var action = Assert.Single(drops);
        Assert.Equal(3, action.SpecificAttempts);
        Assert.NotEmpty(action.Entries);
        Assert.Same(drops, await source.GetAsync(144, 55611, CancellationToken.None));
    }

    [SkippableTheory]
    [InlineData(10000, 61000)] [InlineData(10005, 61030)] [InlineData(10005, 61031)]
    [InlineData(10000, int.MaxValue)]
    public async Task Rooms_without_item_actions_are_empty(int dungeon, int map)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        Assert.Empty(await new PvfDungeonRoomDropSource(provider).GetAsync(dungeon, map, CancellationToken.None));
    }

    [Fact]
    public void Zero_actions_multiple_actions_and_hellparty_keep_original_object_indices()
    {
        var values = ("7 1 2 3 0 " +
            "8 1 2 3 2 [trap] 1 2 3 4 5 [item] 0 3 2 -1 -1 " +
            "9 1 2 3 2 [hellparty] 10 50 1 11 50 1 [/hellparty] [item] 1 -1 1 -1 -1").Split(' ');
        var actions = PvfDungeonRoomDropSource.ParseActions(values, 1);
        Assert.Equal(2, actions.Count);
        Assert.Equal(new(1, 8, 1, 0, 3, 2), actions[0]);
        Assert.Equal(new(2, 9, 1, 1, 0, 1), actions[1]);
    }

    [Theory]
    [InlineData("1 2 3 4 1 [item] 0 1")]
    [InlineData("1 2 3 4 1 [unknown] 0 1 1 -1 -1")]
    [InlineData("1 2 3 4 1 [item] 0 -2 1 -1 -1")]
    [InlineData("1 2 3 4 1 [hellparty] 1 2 3")]
    public void Malformed_actions_are_not_silently_reinterpreted(string values) =>
        Assert.Contains("123", Assert.Throws<InvalidDataException>(() =>
            PvfDungeonRoomDropSource.ParseActions(values.Split(' '), 123)).Message);
}
