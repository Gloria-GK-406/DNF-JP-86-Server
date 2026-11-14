using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.ServerBroadcast;
using Dfo.Server.Infrastructure.Configuration;
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
            new Equipment(), new Dungeons());

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
