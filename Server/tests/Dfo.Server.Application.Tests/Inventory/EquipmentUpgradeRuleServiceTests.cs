using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentUpgradeRuleServiceTests
{
    [Theory]
    [InlineData("amulet", 7)]
    [InlineData("wrist", 8)]
    [InlineData("ring", 9)]
    public async Task Jewelry_without_durability_supports_rules_and_maps_cost_by_type(string type, int weight)
    {
        var rules = Rules(new(80, 82, 2, type, null));
        var core = Piece() with { Durability = 123 };
        var quote = await rules.QuoteReinforcementAsync(core, default);
        Assert.Equal(EquipmentReinforcementRejection.None, quote.Rejection);
        Assert.Equal(100 * weight, Assert.IsAssignableFrom<IEquipmentReinforcementRule>(quote.Rule).GoldCost);
        var invest = await rules.DecideOutworldAsync(EquipmentOutworldAction.Invest,
            3000, 0, 0, 1286, 3, 0, default);
        Assert.Equal(EquipmentOutworldRejection.None, invest.Rejection);
        Assert.Equal((byte)3, invest.AmplifyType);
        Assert.Equal(EquipmentAmplificationRejection.None,
            (await rules.CheckAmplificationAsync(core with { AmplifyType = 3 }, default)).Rejection);
        Assert.Equal(EquipmentForgingRejection.NotWeapon,
            (await rules.DecideForgingAsync(3000, 123, 0, 3326, 0, default)).Rejection);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(45, 44, false)]
    [InlineData(45, 45, true)]
    public async Task Declared_durability_requires_exact_full_value(ushort maximum, ushort current, bool allowed)
    {
        var rules = Rules(new(80, 82, 2, "weapon", maximum));
        var core = Piece() with { Durability = current };
        Assert.Equal(allowed ? EquipmentReinforcementRejection.None : EquipmentReinforcementRejection.Durability,
            (await rules.QuoteReinforcementAsync(core, default)).Rejection);
        Assert.Equal(allowed ? EquipmentForgingRejection.None : EquipmentForgingRejection.Durability,
            (await rules.DecideForgingAsync(3000, current, 0, 3326, 0, default)).Rejection);
        Assert.Equal(allowed ? EquipmentAmplificationRejection.None : EquipmentAmplificationRejection.Durability,
            (await rules.CheckAmplificationAsync(core with { AmplifyType = 3 }, default)).Rejection);
    }

    [Fact]
    public async Task Original_upgrade_prohibition_is_interpreted_by_rules()
    {
        var definition = new EquipmentDefinition(80, 82, 2, "weapon", 45)
            { ImpossibleContents = new HashSet<string> { "upgrade" } };
        var rules = Rules(definition);
        Assert.Equal(EquipmentReinforcementRejection.UnsupportedContent,
            (await rules.QuoteReinforcementAsync(Piece(), default)).Rejection);
        Assert.Equal(EquipmentAmplificationRejection.Forbidden,
            (await rules.CheckAmplificationAsync(Piece() with { AmplifyType = 3 }, default)).Rejection);
    }

    [Theory]
    [InlineData("title name")]
    [InlineData("unimplemented equipment type")]
    public async Task Valid_but_unsupported_type_is_a_business_rejection(string type)
    {
        var rules = Rules(new(80, 82, 2, type, null));
        Assert.Equal(EquipmentReinforcementRejection.UnsupportedContent,
            (await rules.QuoteReinforcementAsync(Piece(), default)).Rejection);
        Assert.Equal(EquipmentOutworldRejection.UnsupportedContent,
            (await rules.DecideOutworldAsync(EquipmentOutworldAction.Invest, 3000, 0, 0, 1286, 3, 0, default)).Rejection);
    }

    [Theory]
    [InlineData(0, EquipmentAmplificationRejection.NoOption)]
    [InlineData(128, EquipmentAmplificationRejection.UnidentifiedOption)]
    [InlineData(1, EquipmentAmplificationRejection.None)]
    [InlineData(4, EquipmentAmplificationRejection.None)]
    public async Task Amplification_query_distinguishes_attribute_states(byte type, EquipmentAmplificationRejection reason)
    {
        Assert.Equal(reason, (await Rules(new(80, 82, 2, "wrist", null))
            .CheckAmplificationAsync(Piece() with { AmplifyType = type }, default)).Rejection);
    }

    private static InventoryItemCoreState Piece() => StackableItemCore.Create(1, 3000, 1000)
        with { Durability = 45 };

    private static EquipmentUpgradeRuleService Rules(EquipmentDefinition definition)
    {
        var content = new Content(definition);
        return new(content, content, content, content, NullLogger<EquipmentUpgradeRuleService>.Instance);
    }

    private sealed class Content(EquipmentDefinition definition) : IEquipmentDefinitionSource,
        IEquipmentReinforcementDefinitionSource, IEquipmentOutworldContentSource, IEquipmentForgingContentSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(id == 3000 ? definition : null);
        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(null);
        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(new([new(1, new double[19], 0, 1, 0, 3037, 10)],
                Enumerable.Repeat(100, 100).ToArray(), [1, 1, 1], Enumerable.Range(0, 12).Select(x => (double)x).ToArray(),
                new Dictionary<int, double> { [0] = 1 }, new Dictionary<int, int>(), new Dictionary<int, int>(),
                new(3243, 40, 10, new Dictionary<int, double>(), new Dictionary<int, int>()), 12));
        ValueTask<EquipmentOutworldTable?> IEquipmentOutworldContentSource.GetAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentOutworldTable?>(new(55, new Dictionary<int, int>(),
                new Dictionary<int, int>(), new Dictionary<int, EquipmentOutworldMaterialDefinition> { [1286] = new(0, 1) },
                new Dictionary<int, EquipmentOutworldMaterialDefinition>(),
                new Dictionary<int, IReadOnlyDictionary<byte, ushort>> { [2] = new Dictionary<byte, ushort> { [3] = 5 } }, []));
        ValueTask<EquipmentForgingTable?> IEquipmentForgingContentSource.GetAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentForgingTable?>(new(1, [new(1, 10000, 1)],
                new Dictionary<int, EquipmentForgingMaterial> { [82] = new(3326, 39) }, [1, 1, 1]));
    }
}
