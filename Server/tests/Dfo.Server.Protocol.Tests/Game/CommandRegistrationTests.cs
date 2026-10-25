using Dfo.Server.Protocol.Game.AccountRoster;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Data.Networking;
using Dfo.Server.Application;
using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Application.Commands;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.CreateCharacter;
using Dfo.Server.Protocol.Game.DeleteCharacter;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Protocol.Game.ReturnToSelection;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.TaggedIdReport;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// That every command the server registers can actually be built.
/// </summary>
/// <remarks>
/// Command handlers are registered scoped, so a dependency nobody registered does not stop the host
/// from starting -- <c>ChannelHostStartupTests</c> boots the packaged host and would still pass. It
/// throws the first time a player triggers that command, which for an item command means the first
/// key press of a live session. Resolving each registration's invoker constructs its handler and the
/// whole chain behind it, so the gap closes at build time instead of in front of a player.
/// </remarks>
public sealed class CommandRegistrationTests
{
    [Fact]
    public void Inventory_move_wire_has_one_exact_NonEffect_whole_and_a_reusable_non_command_domain_request()
    {
        using var provider=BuildProductionGraph();var rows=provider.GetServices<CommandHandlerRegistration>().ToArray();
        var row=Assert.Single(rows,x=>x.CommandType==typeof(GameMoveItemSpaceCommand));
        Assert.Equal(CommandExecutionKind.Direct,row.ExecutionKind);Assert.Equal(CommandRouteKind.Protocol,row.Route);
        Assert.Equal(new[]{new ProtocolCommandMessageBinding(ClientMessageType.GameMoveItemSpace,typeof(MoveItemSpaceRequest))},row.ProtocolMessages);
        Assert.True(row.RegistrationExits is null or {Count:0});
        Assert.False(typeof(Dfo.Server.Abstractions.Networking.IEffectSettlementRequest).IsAssignableFrom(typeof(MoveItemSpaceRequest)));
        Assert.False(typeof(ICommand<ItemMoveResult>).IsAssignableFrom(typeof(InventoryMoveRequest)));
        Assert.NotNull(provider.GetRequiredService<IInventoryMoveDomainService>());
        var registry=provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        Assert.True(registry.TryGet(ClientMessageType.GameMoveItemSpace,out var route));Assert.Equal(row.CommandType,route!.ProtocolCommandType);
        Assert.Equal(new[]{typeof(MoveItemSpaceRequest)},route.AllowedMessageClrTypes);
        Assert.DoesNotContain(rows,x=>x.CommandType.Name=="MoveItemSpaceCommand");
        Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.MoveItemSpace.MoveItemSpaceCommand"));
        Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.MoveItemSpace.MoveItemSpaceCommandHandler"));
    }

    [Fact]
    public void Crystal_selection_wire_binds_one_exact_NonEffect_whole_without_registration_permission()
    {
        using var provider=BuildProductionGraph();var rows=provider.GetServices<CommandHandlerRegistration>().ToArray();
        var row=Assert.Single(rows,x=>x.CommandType==typeof(Dfo.Server.Protocol.Game.CrystalContract.GameCrystalContractCommand));
        Assert.Equal(CommandExecutionKind.Direct,row.ExecutionKind);Assert.Equal(CommandRouteKind.Protocol,row.Route);
        Assert.Equal(new[]{new ProtocolCommandMessageBinding(ClientMessageType.GameCrystalContract,typeof(Dfo.Server.Protocol.Game.CrystalContract.CrystalContractRequest))},row.ProtocolMessages);
        Assert.True(row.RegistrationExits is null or {Count:0});
        Assert.False(typeof(Dfo.Server.Abstractions.Networking.IEffectSettlementRequest).IsAssignableFrom(typeof(Dfo.Server.Protocol.Game.CrystalContract.CrystalContractRequest)));
        var registry=provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        Assert.True(registry.TryGet(ClientMessageType.GameCrystalContract,out var route));Assert.Equal(row.CommandType,route!.ProtocolCommandType);
        Assert.Equal(new[]{typeof(Dfo.Server.Protocol.Game.CrystalContract.CrystalContractRequest)},route.AllowedMessageClrTypes);
        Assert.DoesNotContain(rows,x=>x.CommandType.Name=="SelectCrystalContractCommand");
        Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.CrystalContract.SelectCrystalContractCommand"));
        Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.CrystalContract.SelectCrystalContractCommandHandler"));
    }
    [Fact]
    public void Repair_ordinary_and_auto_wire_bind_one_exact_NonEffect_whole_without_registration_permission()
    {
        using var provider=BuildProductionGraph();var rows=provider.GetServices<CommandHandlerRegistration>().ToArray();
        var row=Assert.Single(rows,x=>x.CommandType==typeof(Dfo.Server.Protocol.Game.RepairItem.GameRepairItemCommand));
        Assert.Equal(CommandExecutionKind.Direct,row.ExecutionKind);Assert.Equal(CommandRouteKind.Protocol,row.Route);
        Assert.Equal(new[]{new ProtocolCommandMessageBinding(ClientMessageType.GameRepairItem,typeof(Dfo.Server.Protocol.Game.RepairItem.RepairItemRequest))},row.ProtocolMessages);
        Assert.True(row.RegistrationExits is null or {Count:0});
        Assert.False(typeof(Dfo.Server.Abstractions.Networking.IEffectSettlementRequest).IsAssignableFrom(typeof(Dfo.Server.Protocol.Game.RepairItem.RepairItemRequest)));
        var registry=provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        Assert.True(registry.TryGet(ClientMessageType.GameRepairItem,out var route));Assert.Equal(row.CommandType,route!.ProtocolCommandType);
        Assert.Equal(new[]{typeof(Dfo.Server.Protocol.Game.RepairItem.RepairItemRequest)},route.AllowedMessageClrTypes);
        Assert.DoesNotContain(rows,x=>x.CommandType.Name=="RepairItemCommand");
        Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.RepairItem.RepairItemCommand"));
        Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.RepairItem.RepairItemCommandHandler"));
    }

    [Fact]
    public void Delete_wire_has_one_exact_NonEffect_whole_and_no_registration_permission()
    {
        using var provider=BuildProductionGraph();var rows=provider.GetServices<CommandHandlerRegistration>().ToArray();
        var row=Assert.Single(rows,x=>x.CommandType==typeof(Dfo.Server.Protocol.Game.DeleteItem.GameDeleteItemCommand));
        Assert.Equal(CommandExecutionKind.Direct,row.ExecutionKind);Assert.Equal(CommandRouteKind.Protocol,row.Route);
        Assert.Equal(new[]{new ProtocolCommandMessageBinding(ClientMessageType.GameDeleteItem,typeof(Dfo.Server.Protocol.Game.DeleteItem.DeleteItemRequest))},row.ProtocolMessages);
        Assert.True(row.RegistrationExits is null or {Count:0});
        Assert.False(typeof(Dfo.Server.Abstractions.Networking.IEffectSettlementRequest).IsAssignableFrom(typeof(Dfo.Server.Protocol.Game.DeleteItem.DeleteItemRequest)));
        var registry=provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        Assert.True(registry.TryGet(ClientMessageType.GameDeleteItem,out var route));Assert.Equal(row.CommandType,route!.ProtocolCommandType);
        Assert.Equal(new[]{typeof(Dfo.Server.Protocol.Game.DeleteItem.DeleteItemRequest)},route.AllowedMessageClrTypes);
        foreach (var fine in new[] { "DiscardItemCommand", "ConsumeSkillMaterialsCommand", "ConsumeEffectMaterialsCommand" })
        {
            Assert.DoesNotContain(rows,x=>x.CommandType.Name==fine);
            Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.DeleteItem."+fine));
            Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.DeleteItem."+fine+"Handler"));
        }
    }

    [Fact]
    public void Sell_wire_has_one_exact_NonEffect_whole_and_no_registration_permission()
    {
        using var provider=BuildProductionGraph();var rows=provider.GetServices<CommandHandlerRegistration>().ToArray();
        var row=Assert.Single(rows,x=>x.CommandType==typeof(Dfo.Server.Protocol.Game.SellItem.GameSellItemCommand));
        Assert.Equal(CommandExecutionKind.Direct,row.ExecutionKind);Assert.Equal(CommandRouteKind.Protocol,row.Route);
        Assert.Equal(new[]{new ProtocolCommandMessageBinding(ClientMessageType.GameSellItem,typeof(Dfo.Server.Protocol.Game.SellItem.SellItemRequest))},row.ProtocolMessages);
        Assert.True(row.RegistrationExits is null or {Count:0});
        Assert.False(typeof(Dfo.Server.Abstractions.Networking.IEffectSettlementRequest).IsAssignableFrom(typeof(Dfo.Server.Protocol.Game.SellItem.SellItemRequest)));
        var registry=provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        Assert.True(registry.TryGet(ClientMessageType.GameSellItem,out var route));Assert.Equal(row.CommandType,route!.ProtocolCommandType);
        Assert.Equal(new[]{typeof(Dfo.Server.Protocol.Game.SellItem.SellItemRequest)},route.AllowedMessageClrTypes);
        Assert.DoesNotContain(rows,x=>x.CommandType.Name=="SellItemCommand");
        Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.SellItem.SellItemCommand"));
        Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.SellItem.SellItemCommandHandler"));
    }

    [Fact]
    public void Three_sort_wires_use_two_exact_NonEffect_wholes_without_registration_exits_or_old_fines()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        foreach (var pair in new[] {
            (typeof(Dfo.Server.Protocol.Game.InventorySort.GameSortItemCommand), new[] { ClientMessageType.GameSortItem }, typeof(Dfo.Server.Protocol.Game.InventorySort.SortItemRequest)),
            (typeof(Dfo.Server.Protocol.Game.InventorySort.GameSortLockCommand), new[] { ClientMessageType.GameSortLock, ClientMessageType.GameSortUnlock }, typeof(Dfo.Server.Protocol.Game.InventorySort.SortLockRequest)) })
        {
            var row = Assert.Single(commands, x => x.CommandType == pair.Item1);
            Assert.Equal(CommandRouteKind.Protocol, row.Route); Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind);
            Assert.Empty(row.RegistrationExits ?? []);
            Assert.Equal(pair.Item2.Select(logical => new ProtocolCommandMessageBinding(logical, pair.Item3)), row.ProtocolMessages);
            Assert.False(typeof(Dfo.Server.Abstractions.Networking.IEffectSettlementRequest).IsAssignableFrom(pair.Item3));
            foreach (var logical in pair.Item2)
            {
                var route = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>().TryGet(logical, out var found);
                Assert.True(route); Assert.Equal(pair.Item1, found!.ProtocolCommandType); Assert.Equal(new[] { pair.Item3 }, found.AllowedMessageClrTypes);
            }
        }
        foreach (var name in new[] { "SortItemCommand", "SortItemCommandHandler", "SortLockCommand", "SortLockCommandHandler" })
            Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.InventorySort." + name));
        Assert.DoesNotContain(commands, x => x.CommandType.Name is "SortItemCommand" or "SortLockCommand");
    }

    [Fact]
    public void Four_collection_box_wires_have_one_exact_whole_Main_and_only_replay_and_initial_permissions()
    {
        using var provider = BuildProductionGraph(); var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var row = Assert.Single(commands,x=>x.CommandType == typeof(Dfo.Server.Protocol.Game.CollectionBox.GameCollectionBoxCommand));
        Assert.Equal(CommandExecutionKind.Direct,row.ExecutionKind);Assert.Equal(CommandRouteKind.Protocol,row.Route);
        Assert.Equal(new[] {new ProtocolCommandMessageBinding(ClientMessageType.GameCollectionBox,typeof(Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxRequest))},row.ProtocolMessages);
        Assert.Equal(new[]{CommandRegistrationExit.CommittedReplayCurrentStateBatch,CommandRegistrationExit.SettledInitialBatch},row.RegistrationExits);
        var registry=provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        Assert.True(registry.TryGet(ClientMessageType.GameCollectionBox,out var route));Assert.Equal(row.CommandType,route!.ProtocolCommandType);
        Assert.Equal(new[]{typeof(Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxRequest)},route.AllowedMessageClrTypes);
        foreach(var type in new ushort[]{0x03E5,0x03E6,0x03E7,0x03E8})
        {
            var decoder=Assert.Single(provider.GetServices<Dfo.Server.Abstractions.Networking.GameMessageDecoderRegistration>(),x=>x.Selector==1&&x.Type==type);
            var body=type==0x03E5?new byte[]{4}:type==0x03E7?new byte[]{1,0xE8,3,0,0}:new byte[]{1,0,65,0,0xE8,3,0,0};
            var input=((Dfo.Server.Abstractions.Networking.IGameMessageDecoder)provider.GetRequiredService(decoder.DecoderType)).Decode(new(1,type,0,1),body);
            Assert.IsType<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxRequest>(input);Assert.Equal(ClientMessageType.GameCollectionBox,input!.MessageType);
        }
        Assert.DoesNotContain(commands,x=>x.CommandType.Name=="CollectionBoxCommand");
        Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxCommand"));
        Assert.Null(typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxCommandHandler"));
    }

    [Fact]
    public void Six_warehouse_logicals_share_one_exact_direct_whole_and_only_existing_replay_permission()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var row = Assert.Single(commands, x => x.CommandType == typeof(Dfo.Server.Protocol.Game.Warehouse.GameWarehouseCommand));
        Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind); Assert.Equal(CommandRouteKind.Protocol, row.Route);
        var expected = new[] { ClientMessageType.GameCreateAccountCargo, ClientMessageType.GameUpgradeAccountCargo, ClientMessageType.GameDepositAccountCargo, ClientMessageType.GameWithdrawAccountCargo, ClientMessageType.GameUpgradeCargo, ClientMessageType.GameUpgradeInventory };
        var shape = typeof(Dfo.Server.Protocol.Game.Warehouse.WarehouseRequest);
        Assert.Equal(expected.Select(x => new ProtocolCommandMessageBinding(x, shape)), row.ProtocolMessages);
        Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, row.RegistrationExits);
        Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(row.CommandType));
        var registry = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        foreach (var logical in expected)
        {
            Assert.True(registry.TryGet(logical, out var route)); Assert.Equal(row.CommandType, route!.ProtocolCommandType);
            Assert.Equal(new[] { shape }, route.AllowedMessageClrTypes);
        }
        Assert.DoesNotContain(commands, x => x.CommandType.Name == "WarehouseCommand");
        Assert.Null(typeof(Dfo.Server.Protocol.ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.Warehouse.WarehouseCommand"));
    }

    [Fact]
    public void Avatar_compound_has_one_exact_direct_whole_and_only_existing_replay_permission()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var row = Assert.Single(commands, x => x.CommandType == typeof(Dfo.Server.Protocol.Game.AvatarCompound.GameAvatarCompoundCommand));
        Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind); Assert.Equal(CommandRouteKind.Protocol, row.Route);
        Assert.Equal(new[] { new ProtocolCommandMessageBinding(ClientMessageType.GameAvatarCompound, typeof(Dfo.Server.Protocol.Game.AvatarCompound.AvatarCompoundRequest)) }, row.ProtocolMessages);
        Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, row.RegistrationExits);
        Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(row.CommandType));
        var registry = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        Assert.True(registry.TryGet(ClientMessageType.GameAvatarCompound, out var route)); Assert.Equal(row.CommandType, route!.ProtocolCommandType);
        Assert.Equal(new[] { typeof(Dfo.Server.Protocol.Game.AvatarCompound.AvatarCompoundRequest) }, route.AllowedMessageClrTypes);
        Assert.DoesNotContain(commands, x => x.CommandType.Name == "AvatarCompoundCommand");
    }

    [Fact]
    public void Four_socket_logicals_share_one_exact_direct_whole_with_only_existing_replay_permission()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var registry = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        var expected = new[]
        {
            (ClientMessageType.GameAvatarEmblem, typeof(Dfo.Server.Protocol.Game.ItemSockets.ItemSocketRequest), typeof(Dfo.Server.Protocol.Game.ItemSockets.GameItemSocketCommand)),
            (ClientMessageType.GameAvatarSocketOpen, typeof(Dfo.Server.Protocol.Game.ItemSockets.ItemSocketRequest), typeof(Dfo.Server.Protocol.Game.ItemSockets.GameItemSocketCommand)),
            (ClientMessageType.GameEquipmentEmblem, typeof(Dfo.Server.Protocol.Game.ItemSockets.ItemSocketRequest), typeof(Dfo.Server.Protocol.Game.ItemSockets.GameItemSocketCommand)),
            (ClientMessageType.GameEquipmentSocketOpen, typeof(Dfo.Server.Protocol.Game.ItemSockets.ItemSocketRequest), typeof(Dfo.Server.Protocol.Game.ItemSockets.GameItemSocketCommand)),
        };
        foreach (var (logical, input, command) in expected)
        {
            var row = Assert.Single(commands, row => row.CommandType == command);
            Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind); Assert.Equal(CommandRouteKind.Protocol, row.Route);
            Assert.Equal(expected.Select(x => new ProtocolCommandMessageBinding(x.Item1, x.Item2)), row.ProtocolMessages);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, row.RegistrationExits);
            Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(command));
            Assert.True(registry.TryGet(logical, out var route)); Assert.Equal(command, route!.ProtocolCommandType);
            Assert.Equal(new[] { input }, route.AllowedMessageClrTypes);
        }
        Assert.DoesNotContain(commands, row => row.CommandType.Name is "ItemSocketCommand");
    }



    [Fact]
    public void Two_quality_protocols_have_exact_direct_wholes_and_only_existing_replay_permission()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var registry = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        var expected = new[]
        {
            (ClientMessageType.GameResetItemQuality, typeof(Dfo.Server.Protocol.Game.ResetItemQuality.ResetItemQualityRequest), typeof(Dfo.Server.Protocol.Game.ResetItemQuality.GameResetItemQualityCommand)),
            (ClientMessageType.GameResetRandomOption, typeof(Dfo.Server.Protocol.Game.ResetRandomOption.ResetRandomOptionRequest), typeof(Dfo.Server.Protocol.Game.ResetRandomOption.GameResetRandomOptionCommand)),
        };
        foreach (var (logical, input, command) in expected)
        {
            var row = Assert.Single(commands, row => row.CommandType == command);
            Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind); Assert.Equal(CommandRouteKind.Protocol, row.Route);
            Assert.Equal(new[] { new ProtocolCommandMessageBinding(logical, input) }, row.ProtocolMessages);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, row.RegistrationExits);
            Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(command));
            Assert.True(registry.TryGet(logical, out var route)); Assert.Equal(command, route!.ProtocolCommandType);
            Assert.Equal(new[] { input }, route.AllowedMessageClrTypes);
        }
        Assert.DoesNotContain(commands, row => row.CommandType.Name is "ResetItemQualityCommand" or "ResetRandomOptionCommand");
    }


    [Fact]
    public void Eight_openings_each_have_one_exact_direct_whole_with_only_proven_replay_permission()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var registry = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        var expected = new[]
        {
            (ClientMessageType.GamePackageOpening, typeof(Dfo.Server.Protocol.Game.PackageOpening.PackageOpeningRequest), typeof(Dfo.Server.Protocol.Game.PackageOpening.GamePackageOpeningCommand)),
            (ClientMessageType.GameCompoundItem, typeof(Dfo.Server.Protocol.Game.CompoundItem.CompoundItemRequest), typeof(Dfo.Server.Protocol.Game.CompoundItem.GameCompoundItemCommand)),
            (ClientMessageType.GameLotteryOpening, typeof(Dfo.Server.Protocol.Game.LotteryOpening.LotteryOpeningRequest), typeof(Dfo.Server.Protocol.Game.LotteryOpening.GameLotteryOpeningCommand)),
            (ClientMessageType.GameVendingMachine, typeof(Dfo.Server.Protocol.Game.VendingMachine.VendingMachineRequest), typeof(Dfo.Server.Protocol.Game.VendingMachine.GameVendingMachineCommand)),
            (ClientMessageType.GameEpicCraft, typeof(Dfo.Server.Protocol.Game.EpicCraft.EpicCraftRequest), typeof(Dfo.Server.Protocol.Game.EpicCraft.GameEpicCraftCommand)),
            (ClientMessageType.GameAvatarPackageOpening, typeof(Dfo.Server.Protocol.Game.AvatarPackageOpening.AvatarPackageOpeningRequest), typeof(Dfo.Server.Protocol.Game.AvatarPackageOpening.GameAvatarPackageOpeningCommand)),
            (ClientMessageType.GameMagicBoxSingleOpening, typeof(Dfo.Server.Protocol.Game.MagicBoxSingleOpening.MagicBoxSingleOpeningRequest), typeof(Dfo.Server.Protocol.Game.MagicBoxSingleOpening.GameMagicBoxSingleOpeningCommand)),
            (ClientMessageType.GameMagicBoxBatchOpening, typeof(Dfo.Server.Protocol.Game.MagicBoxBatchOpening.MagicBoxBatchOpeningRequest), typeof(Dfo.Server.Protocol.Game.MagicBoxBatchOpening.GameMagicBoxBatchOpeningCommand)),
        };
        foreach (var (logical, input, command) in expected)
        {
            var row = Assert.Single(commands, row => row.CommandType == command);
            Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind); Assert.Equal(CommandRouteKind.Protocol, row.Route);
            Assert.Equal(new[] { new ProtocolCommandMessageBinding(logical, input) }, row.ProtocolMessages);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, row.RegistrationExits);
            Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(command));
            Assert.True(registry.TryGet(logical, out var route)); Assert.Equal(command, route!.ProtocolCommandType);
            Assert.Equal(new[] { input }, route.AllowedMessageClrTypes);
        }
        Assert.DoesNotContain(commands, row => row.CommandType.Name is "PackageOpeningCommand" or "AvatarPackageOpeningCommand" or "MagicBoxSingleOpeningCommand" or "MagicBoxBatchOpeningCommand" or "CompoundItemCommand" or "LotteryOpeningCommand" or "VendingMachineCommand" or "EpicCraftCommand");
    }

    [Fact]
    public void Five_option_logicals_are_closed_to_three_wholes_with_two_one_two_exact_bindings()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var registry = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        var expected = new[]
        {
            (typeof(Dfo.Server.Protocol.Game.MagicSeal.GameMagicSealCommand), typeof(Dfo.Server.Protocol.Game.MagicSeal.MagicSealRequest), new[] { ClientMessageType.GameUnsealRandomOption, ClientMessageType.GameChangeRandomOption }),
            (typeof(Dfo.Server.Protocol.Game.Forging.GameForgingCommand), typeof(Dfo.Server.Protocol.Game.Forging.ForgingRequest), new[] { ClientMessageType.GameSeparateUpgrade }),
            (typeof(Dfo.Server.Protocol.Game.Outworld.GameOutworldCommand), typeof(Dfo.Server.Protocol.Game.Outworld.OutworldRequest), new[] { ClientMessageType.GamePurifyItem, ClientMessageType.GameInvestAmplifyOption }),
        };
        foreach (var (command, input, logicals) in expected)
        {
            var row = Assert.Single(commands, row => row.CommandType == command);
            Assert.Equal(CommandRouteKind.Protocol, row.Route); Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind);
            Assert.Equal(logicals.Select(logical => new ProtocolCommandMessageBinding(logical, input)), row.ProtocolMessages);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, row.RegistrationExits);
            foreach (var logical in logicals)
            {
                Assert.True(registry.TryGet(logical, out var route)); Assert.Equal(command, route!.ProtocolCommandType);
                Assert.Equal(input, route.MessageClrType); Assert.Equal(new[] { input }, route.AllowedMessageClrTypes);
            }
        }
        Assert.DoesNotContain(commands, row => row.CommandType.Name is "MagicSealCommand" or "ForgingCommand" or "OutworldCommand");
    }

    [Fact]
    public void Three_equipment_protocols_each_have_one_exact_whole_and_only_replay_permission()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var registry = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        var expected = new[]
        {
            (ClientMessageType.GameReinforceItem, typeof(Dfo.Server.Protocol.Game.ReinforceItem.ReinforceItemRequest), typeof(Dfo.Server.Protocol.Game.ReinforceItem.GameReinforceItemCommand)),
            (ClientMessageType.GameDisjointItem, typeof(Dfo.Server.Protocol.Game.DisjointItem.DisjointItemRequest), typeof(Dfo.Server.Protocol.Game.DisjointItem.GameDisjointItemCommand)),
            (ClientMessageType.GameEnchantByBead, typeof(Dfo.Server.Protocol.Game.EnchantByBead.EnchantByBeadRequest), typeof(Dfo.Server.Protocol.Game.EnchantByBead.GameEnchantByBeadCommand)),
        };
        foreach (var (logical, input, command) in expected)
        {
            var row = Assert.Single(commands, row => row.CommandType == command);
            Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind); Assert.Equal(CommandRouteKind.Protocol, row.Route);
            Assert.Equal(new[] { new ProtocolCommandMessageBinding(logical, input) }, row.ProtocolMessages);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, row.RegistrationExits);
            Assert.True(registry.TryGet(logical, out var route)); Assert.Equal(command, route!.ProtocolCommandType);
            Assert.Equal(new[] { input }, route.AllowedMessageClrTypes);
        }
        Assert.DoesNotContain(commands, row => row.CommandType.Name is "ReinforceItemCommand" or "NpcDisjointItemCommand" or "PortableDisjointItemCommand" or "EnchantByBeadCommand");
    }

    [Fact]
    public void Other_detail_is_bound_to_one_protocol_command_with_its_exact_request()
    {
        using var provider = BuildProductionGraph();
        var registration = Assert.Single(provider.GetServices<CommandHandlerRegistration>(),
            row => row.CommandType == typeof(GameOtherCharacterDetailCommand));
        Assert.Equal(CommandRouteKind.Protocol, registration.Route);
        Assert.Equal(CommandExecutionKind.Direct, registration.ExecutionKind);
        var binding = Assert.Single(registration.ProtocolMessages!);
        Assert.Equal(ClientMessageType.GameGetOtherCharacterDetail, binding.MessageType);
        Assert.Equal(typeof(GetOtherCharacterDetailRequest), binding.MessageClrType);
        Assert.NotNull(provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>());
    }

    [Fact]
    public void Buy_has_one_logical_route_two_exact_shapes_and_only_the_existing_replay_exit()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var whole = Assert.Single(registrations, row => row.CommandType == typeof(Dfo.Server.Protocol.Game.BuyItem.GameBuyItemCommand));
        Assert.Equal(CommandRouteKind.Protocol, whole.Route);
        Assert.Equal(CommandExecutionKind.Direct, whole.ExecutionKind);
        Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, whole.RegistrationExits);
        Assert.All(whole.ProtocolMessages!, pair => Assert.Equal(ClientMessageType.GameBuyItem, pair.MessageType));
        var family = new[] { typeof(Dfo.Server.Protocol.Game.BuyItem.BuyItemRequest), typeof(Dfo.Server.Protocol.Game.BuyItem.HousingBuyItemRequest) };
        Assert.Equal(family, whole.ProtocolMessages!.Select(pair => pair.MessageClrType));
        Assert.DoesNotContain(registrations, row => row.CommandType.FullName is "Dfo.Server.Protocol.Game.BuyItem.BuyItemCommand" or "Dfo.Server.Protocol.Game.BuyItem.HousingBuyItemCommand");
        Assert.True(provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>().TryGet(ClientMessageType.GameBuyItem, out var route));
        Assert.Equal(typeof(Dfo.Server.Protocol.Game.BuyItem.GameBuyItemCommand), route!.ProtocolCommandType);
        Assert.Equal(typeof(Dfo.Server.Protocol.Game.BuyItem.BuyItemRequest), route.MessageClrType);
        Assert.Equal(family, route.AllowedMessageClrTypes);
    }

    [Fact]
    public void Every_registered_command_handler_resolves_from_the_production_graph()
    {
        using var provider = BuildProductionGraph();
        using var scope = provider.CreateScope();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();

        Assert.NotEmpty(registrations);
        foreach (var registration in registrations)
        {
            var invoker = scope.ServiceProvider.GetService(registration.InvokerType);
            Assert.NotNull(invoker);
        }
    }

    [Fact]
    public void All_four_Channel_protocols_are_bound_to_whole_commands()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();
        (Type Command, Dfo.Server.Data.Networking.ClientMessageType Message)[] channels =
        [
            (typeof(Dfo.Server.Protocol.Channel.Connect.ChannelConnectCommand), Dfo.Server.Data.Networking.ClientMessageType.ChannelConnect),
            (typeof(Dfo.Server.Protocol.Channel.ScriptVersion.ChannelScriptVersionCommand), Dfo.Server.Data.Networking.ClientMessageType.ChannelScriptVersion),
            (typeof(Dfo.Server.Protocol.Channel.Script.ChannelScriptCommand), Dfo.Server.Data.Networking.ClientMessageType.ChannelScript),
            (typeof(Dfo.Server.Protocol.Channel.Info.ChannelInfoCommand), Dfo.Server.Data.Networking.ClientMessageType.ChannelInfo),
        ];
        foreach (var (command, message) in channels)
        {
            var registration = Assert.Single(registrations, row => row.CommandType == command);
            Assert.Equal(CommandRouteKind.Protocol, registration.Route);
            Assert.Equal(message, Assert.Single(registration.ProtocolMessages!).MessageType);
        }
        Assert.NotNull(provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>());
    }

    /// <remarks>
    /// Resolving every registration proves each registered command can be built; it says nothing
    /// about a command nobody registered, because there is no registration to walk. The item
    /// handlers' own tests dispatch through a stand-in bus, so an unregistered item command would
    /// leave the whole suite green and throw at the registry on the first key press of a live
    /// session.
    /// </remarks>
    [Fact]
    public void The_item_commands_are_registered()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();

        Assert.Contains(registrations, value => value.CommandType == typeof(GameUseStackableCommand));
        Assert.Contains(registrations, value => value.CommandType == typeof(GameMoveItemSpaceCommand));
    }

    /// <summary>
    /// That the commands which can reach a persistent write are the transactional kind.
    /// </summary>
    /// <remarks>
    /// Only <c>Transactional</c> makes <c>UnitOfWorkBehavior</c> open a domain transaction, and
    /// without one a handler's writes each settle on their own with nobody able to take them back.
    /// The repository convention names these ten for exactly that reason.
    /// <para>
    /// This is pinned rather than left to the convention because entry save now depends on it for
    /// player data. It takes away every pile the character still holds and writes them again inside
    /// the same save, so the rows that used to be safe to lose -- the ones a lease had let go of --
    /// are no longer the only ones in the delete. Change the kind on <c>Prepare</c> and that pair
    /// stops being one change: the delete settles, the rewrite does not, and the piles are gone with
    /// nothing said. Before this assertion existed that edit left the whole suite green.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_commands_that_reach_a_persistent_write_are_transactional()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>()
            .ToDictionary(value => value.CommandType, value => value.ExecutionKind);

        Assert.Equal(CommandExecutionKind.Direct, registrations[typeof(GameUseStackableCommand)]);
        Assert.Equal(CommandRouteKind.Protocol, provider.GetServices<CommandHandlerRegistration>().Single(x => x.CommandType == typeof(GameUseStackableCommand)).Route);
        Assert.Equal(CommandExecutionKind.Direct, registrations[typeof(GameMoveItemSpaceCommand)]);
        Assert.Equal(CommandRouteKind.Protocol, provider.GetServices<CommandHandlerRegistration>().Single(x => x.CommandType == typeof(GameMoveItemSpaceCommand)).Route);
        Type[] reachPersistentWrites =
        [
            typeof(PrepareSelectedCharacterSessionCommand),
            typeof(CleanupExpiredCharacterItemsCommand),
            typeof(PrepareSelectedCharacterStateCommand),
            typeof(ApplySelectedCharacterDailyRefillCommand),
            typeof(SynchronizeSelectedCharacterQuestsCommand),
            typeof(ReviveSelectedCharacterPetCommand),
            typeof(GrantTutorialRewardCommand),
        ];

        foreach (var command in reachPersistentWrites)
        {
            Assert.True(
                registrations.TryGetValue(command, out var kind),
                $"{command.Name} is not registered at all.");
            Assert.Equal(CommandExecutionKind.Transactional, kind);
        }
    }

    [Fact]
    public void All_four_roster_protocols_use_whole_commands_and_explicit_write_segments()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();
        (Type Command, ClientMessageType Message, Type Request)[] expected =
        [
            (typeof(GameAccountRosterCommand), ClientMessageType.GameGetAccountCharacterRoster, typeof(GetAccountCharacterRosterRequest)),
            (typeof(GameCheckCharacterNameCommand), ClientMessageType.GameCheckCharacterName, typeof(CheckCharacterNameRequest)),
            (typeof(GameCreateCharacterCommand), ClientMessageType.GameCreateCharacter, typeof(CreateCharacterRequest)),
            (typeof(GameDeleteCharacterCommand), ClientMessageType.GameDeleteCharacter, typeof(DeleteCharacterRequest)),
        ];
        foreach (var (command, message, request) in expected)
        {
            var registration = Assert.Single(registrations, row => row.CommandType == command);
            Assert.Equal(CommandRouteKind.Protocol, registration.Route);
            Assert.Equal(CommandExecutionKind.Direct, registration.ExecutionKind);
            var binding = Assert.Single(registration.ProtocolMessages!);
            Assert.Equal(message, binding.MessageType);
            Assert.Equal(request, binding.MessageClrType);
        }
        Assert.NotNull(provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>());
        // CharacterRosterCommandFlowTests and GmInitializationHttpTests exercise the actual write,
        // commit-before-ACK, rollback and post-commit read failure instead of inferring it from Kind.
    }

    [Fact]
    public void GameLoginUsesTheProtocolRouteWithExplicitWorkUnits()
    {
        using var provider = BuildProductionGraph();
        var registration = Assert.Single(
            provider.GetServices<CommandHandlerRegistration>(),
            value => value.CommandType == typeof(GameLoginCommand));

        Assert.Equal(CommandExecutionKind.Direct, registration.ExecutionKind);
        Assert.Equal(CommandRouteKind.Protocol, registration.Route);
    }

    [Fact]
    public void Return_saves_in_a_transaction_and_cleans_up_outside_it()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>()
            .ToDictionary(value => value.CommandType);
        Assert.Equal(CommandExecutionKind.Transactional,
            registrations[typeof(PrepareReturnToSelectionCommand)].ExecutionKind);
        Assert.Equal(CommandExecutionKind.Workflow,
            registrations[typeof(CompleteReturnToSelectionCommand)].ExecutionKind);
    }

    /// <remarks>
    /// The registry itself refuses a command registered twice, so resolving it is the cheapest proof
    /// that the production modules together declare no duplicate.
    /// </remarks>
    [Fact]
    public void No_command_is_registered_twice()
    {
        using var provider = BuildProductionGraph();

        Assert.NotNull(provider.GetRequiredService<CommandHandlerRegistry>());
    }

    /// <remarks>
    /// The archive and the database both open lazily, so building the graph touches neither. A
    /// configuration this sparse is deliberate: anything that needed a real file here would be a
    /// dependency doing work in its constructor.
    /// <para>
    /// Logging is registered because every real host has it -- <c>Host.CreateApplicationBuilder</c>
    /// adds it before any module runs. Leaving it out would make this graph unfaithful in the one
    /// direction that matters here: a service that logs would fail to resolve in a test and resolve
    /// perfectly in production.
    /// </para>
    /// </remarks>
    [Fact]
    public void Creature_effect_and_ticket_entries_have_unique_whole_commands_and_only_original_replay_permission()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var routes = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        (ClientMessageType Message, Type Command, Type Input, bool Replay)[] expected = [
            (ClientMessageType.GameRenameCreature, typeof(Dfo.Server.Protocol.Game.Creatures.GameRenameCreatureCommand), typeof(Dfo.Server.Protocol.Game.Creatures.RenameCreatureRequest), true),
            (ClientMessageType.GameHatchCreature, typeof(Dfo.Server.Protocol.Game.Creatures.GameHatchCreatureCommand), typeof(Dfo.Server.Protocol.Game.Creatures.HatchCreatureRequest), false),
            (ClientMessageType.GameSharedEffect, typeof(Dfo.Server.Protocol.Game.UseStackable.GameSharedEffectCommand), typeof(Dfo.Server.Protocol.Game.UseStackable.SharedEffectRequest), true),
            (ClientMessageType.GameGenCeraTicket, typeof(Dfo.Server.Protocol.Game.GenCeraTicket.GameGenCeraTicketCommand), typeof(Dfo.Server.Protocol.Game.GenCeraTicket.GenCeraTicketRequest), false) ];
        foreach (var (message, command, input, replay) in expected)
        {
            var row = Assert.Single(registrations, x => x.CommandType == command);
            Assert.Equal(CommandRouteKind.Protocol, row.Route); Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind);
            var binding = Assert.Single(row.ProtocolMessages!); Assert.Equal(message, binding.MessageType); Assert.Equal(input, binding.MessageClrType);
            Assert.True(routes.TryGet(message, out var route)); Assert.Equal(command, route!.ProtocolCommandType);
            Assert.Equal(new[] { input }, route.AllowedMessageClrTypes);
            Assert.Equal(replay ? new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch } : [], row.RegistrationExits ?? []);
            Assert.Equal(CommandSelfSettlementCapability.None, row.SelfSettlement);
            Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(command));
        }
        Assert.DoesNotContain(registrations, x => x.CommandType.Name is "RenameCreatureCommand" or "HatchCreatureCommand" or "SharedEffectCommand");
    }

    private static ServiceProvider BuildProductionGraph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // GameServer provides this host fact; the module-only fixture has no game listener.
        services.AddSingleton<IGameServerReadiness, DisabledGameReadiness>();
        var configuration = new ConfigurationBuilder().Build();
        new ApplicationModule().ConfigureServices(services, configuration);
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new Dfo.Server.Networking.NetworkingModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }

    private sealed class DisabledGameReadiness : IGameServerReadiness
    {
        public bool NetworkEnabled => false;
        public bool IsReady => false;
    }
}
