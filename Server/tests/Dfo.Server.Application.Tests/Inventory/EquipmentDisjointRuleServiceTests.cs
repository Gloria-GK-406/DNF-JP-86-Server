using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentDisjointRuleServiceTests
{
    [Fact]
    public async Task System_rule_uses_sell_value_rarity_level_and_the_archive_table()
    {
        var rules = new EquipmentDisjointRuleService(new Content());

        var result = await rules.ResolveAsync(Piece(), EquipmentDisjointSettings.System, 17, default);

        Assert.Equal(EquipmentDisjointRejection.None, result.Rejection);
        Assert.Equal(
            [new(3037, 1), new(3166, 10), new(10100115, 2)],
            result.Rewards);
    }

    [Fact]
    public async Task Expert_machine_is_a_separate_strategy_not_system_rules_with_a_bonus()
    {
        var rules = new EquipmentDisjointRuleService(new Content());

        var result = await rules.ResolveAsync(Piece(),
            new(EquipmentDisjointMethod.ExpertMachine, ExpertLevel: 7, MachineGrade: 4),
            17, default);

        Assert.Equal(EquipmentDisjointRejection.UnsupportedMethod, result.Rejection);
        Assert.Null(result.Rewards);
    }

    [Fact]
    public async Task Locked_or_unidentified_amplified_equipment_never_reaches_a_reward_roll()
    {
        var rules = new EquipmentDisjointRuleService(new Content());
        var locked = Piece() with { Core = Piece().Core with { EquipmentLockId = 1 } };
        var unidentified = Piece() with { Core = Piece().Core with { AmplifyType = 0x80 } };

        Assert.Equal(EquipmentDisjointRejection.CannotDisjoint,
            (await rules.ResolveAsync(locked, EquipmentDisjointSettings.System, 1, default)).Rejection);
        Assert.Equal(EquipmentDisjointRejection.CannotDisjoint,
            (await rules.ResolveAsync(unidentified, EquipmentDisjointSettings.System, 1, default)).Rejection);
    }

    private static EquipmentDisjointCandidate Piece() =>
        new(3000, StackableItemCore.Create(1, 3000, 1234));

    private sealed class Content : IEquipmentDisjointDefinitionSource
    {
        public ValueTask<EquipmentDisjointDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<EquipmentDisjointDefinition?>(new(150, 10, 1, true));

        public ValueTask<EquipmentDisjointTable?> GetSystemTableAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<EquipmentDisjointTable?>(new(
                3037,
                150,
                [1.12, 1.35],
                [Array.Empty<int>(), new[] { 3166 }],
                [new(0, 0, 0, 0), new(1, 1, 100, 0)],
                [new(false, 0, 0, 0, 0), new(true, 10100115, 10, 100, 100)]));
    }
}
