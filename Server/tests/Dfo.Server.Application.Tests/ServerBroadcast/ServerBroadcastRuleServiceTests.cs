using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.ServerBroadcast;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Testing;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.Tests.ServerBroadcast;

[Trait("Category", "Unit")]
public sealed class ServerBroadcastRuleServiceTests
{
    private const int Epic = 2681592;
    private const int Unique = 2681591;
    private const int Ancient = 7;
    private const int Ordinary = 8;

    [Fact]
    public void Every_kind_is_announced_by_default()
    {
        var rules = Rules();
        Assert.All(Enum.GetValues<ServerBroadcastKind>(), kind => Assert.True(rules.Announces(kind)));
        Assert.Equal(6, rules.ForgingMinimumStage);
    }

    [Fact]
    public void One_kind_or_all_of_them_can_be_switched_off()
    {
        var one = Rules(new() { ["ServerBroadcast:Reinforcement"] = "false" });
        Assert.False(one.Announces(ServerBroadcastKind.Reinforcement));
        Assert.True(one.Announces(ServerBroadcastKind.Amplification));

        var all = Rules(new() { ["ServerBroadcast:Enabled"] = "false" });
        Assert.All(Enum.GetValues<ServerBroadcastKind>(), kind => Assert.False(all.Announces(kind)));
    }

    [Theory]
    [InlineData(85, 86, 86)]
    [InlineData(80, 86, 86)]
    [InlineData(84, 85, null)]
    [InlineData(86, 86, null)]
    public void Only_a_rise_onto_the_top_level_reaches_it(int before, int after, int? reached) =>
        Assert.Equal(reached, Rules().TopLevelReached(before, after));

    [Theory]
    [InlineData(0x21, true)]
    [InlineData(0x20, true)]
    [InlineData(0x11, false)]
    [InlineData(0x01, false)]
    public void Only_the_second_awakening_nibble_is_the_second_awakening(int growType, bool second) =>
        Assert.Equal(second, Rules().IsSecondAwakening(growType));

    [Fact]
    public void A_threshold_must_be_positive() =>
        Assert.Throws<InvalidOperationException>(() => Rules(new() { ["ServerBroadcast:ForgingMinimumStage"] = "0" }));

    [Fact]
    public async Task A_lottery_prize_is_notable_from_the_configured_rarity()
    {
        var rules = Rules();
        Assert.True(await rules.IsNotableLotteryPrizeAsync(Epic, default));
        Assert.True(await rules.IsNotableLotteryPrizeAsync(Unique, default));
        Assert.False(await rules.IsNotableLotteryPrizeAsync(1, default));
        Assert.False(await Rules(new() { ["ServerBroadcast:LotteryMinimumRarity"] = "4" }).IsNotableLotteryPrizeAsync(Unique, default));
    }

    [Fact]
    public async Task An_ancient_find_needs_both_the_dungeon_tag_and_the_rarity()
    {
        var rules = Rules();
        Assert.True(await rules.IsNotableAncientFindAsync(Ancient, Epic, default));
        Assert.False(await rules.IsNotableAncientFindAsync(Ancient, Unique, default));
        Assert.False(await rules.IsNotableAncientFindAsync(Ordinary, Epic, default));
        Assert.False(await rules.IsNotableAncientFindAsync(99, Epic, default));
    }

    private static ServerBroadcastRuleService Rules(Dictionary<string, string?>? overrides = null) =>
        new(new ServerConfigService(new ConfigurationBuilder().AddInMemoryCollection(overrides ?? []).Build()),
            Experience(), new Equipment(), new Dungeons());

    // The server's 86: thresholds for 85 rises.
    private static IExperienceRuleService Experience()
    {
        var scale = new CharacterExperienceScale([.. Enumerable.Range(1, 85).Select(level => (uint)level)]);
        return TestRequired.Absent<IExperienceRuleService>((method, _) =>
            method.Name == nameof(IExperienceRuleService.CharacterScale) ? scale : null);
    }

    private sealed class Equipment : IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0);

        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<EquipmentDefinition?>(itemId switch
            {
                Epic => new(80, 80, 4, "weapon", 45),
                Unique => new(80, 80, 3, "weapon", 45),
                _ => null,
            });
    }

    private sealed class Dungeons : IExtraDropContentSource
    {
        public ExtraDropContent Load() => new(
            new Dictionary<(DropItemKind, int, ExtraDropDungeonType), DropItemPool>(),
            new Dictionary<int, ExtraDropDungeonType> { [Ancient] = ExtraDropDungeonType.Ancient, [Ordinary] = ExtraDropDungeonType.Normal }, 0);
    }
}
