using Dfo.Server.Application.World;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.Tests.World;

public sealed class MirrorDungeonEntryTestSettingsTests
{
    [Fact]
    public void Absent_or_disabled_settings_do_not_change_normal_entry()
    {
        Assert.False(MirrorDungeonEntryTestSettingsSource.From(new ConfigurationBuilder().Build()).Enabled);
        var config = Build(new() { ["Enabled"] = "false", ["SkipEntryQuota"] = "true", ["DungeonIds:0"] = "17" });
        var result = MirrorDungeonEntryTestSettingsSource.From(config);
        Assert.False(result.Enabled);
        Assert.False(result.SkipEntryQuota);
        Assert.Empty(result.DungeonIds);
    }

    [Fact]
    public void Explicit_test_settings_preserve_the_configured_order()
    {
        var result = MirrorDungeonEntryTestSettingsSource.From(Build(new()
        {
            ["Enabled"] = "true", ["SkipEntryQuota"] = "true",
            ["DungeonIds:0"] = "17", ["DungeonIds:1"] = "23", ["DungeonIds:2"] = "60", ["DungeonIds:3"] = "4"
        }));
        Assert.True(result.Enabled);
        Assert.True(result.SkipEntryQuota);
        Assert.Equal(new[] { 17, 23, 60, 4 }, result.DungeonIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("not-a-dungeon")]
    public void Empty_or_invalid_enabled_rotation_is_rejected(string? id)
    {
        Assert.Throws<InvalidDataException>(() => MirrorDungeonEntryTestSettingsSource.From(
            Build(new() { ["Enabled"] = "true", ["DungeonIds:0"] = id })));
    }

    private static IConfiguration Build(Dictionary<string, string?> values) => new ConfigurationBuilder()
        .AddInMemoryCollection(values.ToDictionary(x => "MirrorDungeon:EntryTest:" + x.Key, x => x.Value)).Build();
}
