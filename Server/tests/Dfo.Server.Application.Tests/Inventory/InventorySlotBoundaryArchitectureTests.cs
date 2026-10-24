using System.Reflection;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DisjointItem;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.RepairItem;
using Dfo.Server.Protocol.Game.SellItem;
using Dfo.Server.Protocol.Game.UseStackable;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class InventorySlotBoundaryArchitectureTests
{
    [Fact]
    public void Legacy_item_commands_and_whole_use_preserve_their_respective_slot_boundaries()
    {
        AssertSlotProperty<GameSellItemCommand>(nameof(GameSellItemCommand.Input), typeof(SellItemRequest));
        AssertSlotProperty<SellItemRequest>(nameof(SellItemRequest.Slot), typeof(GameSlot));
        Assert.Equal(typeof(BusinessSlot), typeof(IItemSaleDomainService).GetMethod(nameof(IItemSaleDomainService.SellAsync))!
            .GetParameters().Single(parameter => parameter.Name == "slot").ParameterType);
        AssertSlotProperty<DiscardItemCommand>(nameof(DiscardItemCommand.Slot), typeof(BusinessSlot));
        AssertSlotProperty<RepairItemCommand>(nameof(RepairItemCommand.Slot), typeof(BusinessSlot?));
        AssertSlotProperty<GameDisjointItemCommand>(nameof(GameDisjointItemCommand.Input), typeof(DisjointItemRequest));
        AssertSlotProperty<DisjointItemRequest>(nameof(DisjointItemRequest.TargetSlot), typeof(GameSlot));
        foreach (var method in typeof(IEquipmentDisjointDomainService).GetMethods())
            Assert.All(method.GetParameters().Where(parameter => parameter.Name is "targetSlot" or "disjointToolSlot"),
                parameter => Assert.Equal(typeof(BusinessSlot), parameter.ParameterType));
        AssertSlotProperty<MoveItemSpaceCommand>(nameof(MoveItemSpaceCommand.SourceSlot), typeof(BusinessSlot));
        AssertSlotProperty<MoveItemSpaceCommand>(nameof(MoveItemSpaceCommand.DestinationSlot), typeof(BusinessSlot));
        AssertSlotProperty<UseStackableRequest>(nameof(UseStackableRequest.Slot), typeof(GameSlot));

        AssertNoGameAddress<DiscardItemCommand>();
        AssertNoGameAddress<RepairItemCommand>();
        AssertNoGameAddress<GameDisjointItemCommand>();
        AssertNoGameAddress<MoveItemSpaceCommand>();
        var use = typeof(GameUseStackableCommandHandler).GetMethod("UseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(typeof(BusinessSlot), use.GetParameters().Single(parameter => parameter.Name == "slotAddress").ParameterType);
        Assert.Equal(typeof(BusinessSlot), typeof(IItemUseEffectDomainService).GetMethod(nameof(IItemUseEffectDomainService.UseAsync))!
            .GetParameters().Single(parameter => parameter.Name == "source").ParameterType);
    }

    [Fact]
    public void The_six_item_domain_interfaces_accept_no_game_address()
    {
        foreach (var contract in new[]
        {
            typeof(IItemSaleDomainService),
            typeof(IItemDiscardDomainService),
            typeof(IEquipmentRepairDomainService),
            typeof(IEquipmentDisjointDomainService),
            typeof(IItemMovementDomainService),
            typeof(IItemSlotRuleService),
        })
        {
            var forbidden = contract.GetMethods()
                .SelectMany(method => method.GetParameters().Select(parameter =>
                    $"{method.Name}({parameter.ParameterType.Name} {parameter.Name})"))
                .Where(signature => signature.Contains(nameof(GameSlot), StringComparison.Ordinal)
                    || signature.Contains(nameof(InventoryContainerType), StringComparison.Ordinal))
                .ToArray();

            Assert.Empty(forbidden);
        }
    }

    [Fact]
    public void Disjoint_provider_report_stops_at_the_protocol_boundary()
    {
        Assert.Equal(typeof(int), typeof(DisjointItemRequest)
            .GetProperty(nameof(DisjointItemRequest.DisjointProviderId))!.PropertyType);
        Assert.Equal(typeof(DisjointItemRequest), typeof(GameDisjointItemCommand).GetProperty(nameof(GameDisjointItemCommand.Input))!.PropertyType);
        Assert.Null(typeof(GameDisjointItemCommand).GetProperty("DisjointProviderId"));
        Assert.DoesNotContain(typeof(IEquipmentDisjointDomainService).GetMethods()
            .SelectMany(method => method.GetParameters()), parameter =>
                parameter.Name?.Contains("provider", StringComparison.OrdinalIgnoreCase) == true
                || parameter.Name?.Contains("context", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Business_receipts_carry_no_game_address()
    {
        AssertNoGameAddress<ItemSaleReceipt>();
        AssertNoGameAddress<ItemDiscardReceipt>();
        AssertNoGameAddress<EquipmentRepairReceipt>();
        AssertNoGameAddress<EquipmentDisjointReceipt>();
        AssertNoGameAddress<EquipmentDisjointMaterialReceipt>();
    }

    [Fact]
    public void Placement_and_domain_binding_use_business_coordinates_only()
    {
        Assert.Equal(
            typeof(BusinessSlotRange),
            typeof(ItemPlacement).GetProperty(nameof(ItemPlacement.Destination))!.PropertyType);
        Assert.Null(typeof(ItemPlacement).GetProperty("SlotStart"));
        Assert.Null(typeof(ItemPlacement).GetProperty("SlotEnd"));

        foreach (var method in typeof(IBusinessInventorySlotDomainService).GetMethods())
        {
            Assert.NotEqual(typeof(GameSlot), method.ReturnType);
            Assert.DoesNotContain(method.GetParameters(), parameter =>
                parameter.ParameterType == typeof(GameSlot)
                || parameter.ParameterType == typeof(GameSlot?));
        }
    }

    [Fact]
    public void Protocol_models_carry_game_slots_and_never_business_slots()
    {
        foreach (var model in new[]
        {
            typeof(SellItemRequest), typeof(SellItemResponse),
            typeof(DeleteItemRequest), typeof(DeleteItemResponse),
            typeof(RepairItemRequest), typeof(RepairItemResponse),
            typeof(DisjointItemRequest), typeof(DisjointItemResponse),
            typeof(MoveItemSpaceRequest), typeof(MoveItemSpaceResponse),
            typeof(UseStackableRequest), typeof(UseStackableResponse),
        })
        {
            Assert.DoesNotContain(model.GetProperties(), property =>
                property.PropertyType == typeof(BusinessSlot)
                || property.PropertyType == typeof(BusinessSlot?));
        }
    }

    [Fact]
    public void Business_model_boundary_rejects_a_real_wire_address_counterexample() =>
        Assert.ThrowsAny<Exception>(() => AssertNoGameAddress<BadBusinessModel>());

    private sealed record BadBusinessModel(GameSlot Slot);

    private static void AssertSlotProperty<T>(string name, Type expected) =>
        Assert.Equal(expected, typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.PropertyType);

    private static void AssertNoGameAddress<T>() =>
        Assert.DoesNotContain(typeof(T).GetProperties(), property =>
            property.PropertyType == typeof(GameSlot)
            || property.PropertyType == typeof(GameSlot?)
            || property.PropertyType == typeof(InventoryContainerType));
}
