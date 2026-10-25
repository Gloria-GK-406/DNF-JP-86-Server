using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Tests.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class EffectSettlementRegistrationTests
{
    [Fact]
    public void Actual_DI_has_expected_settlement_requests_wire_keys_and_primary_commands()
    {
        using var f = new ShopFixture(); var services = f.Services;
        var handlerRegistry = services.GetRequiredService<MessageHandlerRegistry>();
        var productions = services.GetServices<ClientMessageProductionRegistration>().ToArray();
        var wire = services.GetServices<GameMessageDecoderRegistration>().Select(registration => (
            Registration: registration, Productions: productions.Where(x => x.DecoderType == registration.DecoderType)
                .SelectMany(x => handlerRegistry.TryGet(x.MessageType, out var route) && route.AllowedMessageClrTypes is { } family
                    ? family : new[] { RequestType(handlerRegistry.GetHandlerType(x.MessageType)) }).Where(x => typeof(IEffectSettlementRequest).IsAssignableFrom(x)).Distinct().ToArray()))
            .Where(x => x.Productions.Length > 0).ToArray();
        Assert.Equal(new ushort[] { 0x0015, 0x0019, 0x001A, 0x001B, 0x001D, 0x001E, 0x0022, 0x002C, 0x0050, 0x0051, 0x0063, 0x0064,
            0x00A0, 0x00C9, 0x00CC, 0x00CD, 0x00CE, 0x00D0, 0x00D7, 0x00ED, 0x00FB, 0x0110, 0x0131, 0x0132, 0x0133, 0x0134, 0x0191, 0x0198, 0x01A2, 0x01AF, 0x01B6, 0x01B7, 0x01C8, 0x0204, 0x0207, 0x0313, 0x0379, 0x037A, 0x03CF, 0x03D0, 0x03E5, 0x03E6, 0x03E7, 0x03E8, 0x03F4, 0x03F5,
            0x03FD, 0x03FE, 0x03FF, 0x0400, 0x0401, 0x0402, 0x0403, 0x0404, 0x0405, 0x0406, 0x0450, 0x04E7 },
            wire.Select(x => x.Registration.Type).Order());
        Assert.All(wire, x => { Assert.Equal(1, x.Registration.Selector); Assert.Single(x.Productions); });
        Assert.Equal(new[] { "AvatarCompoundRequest", "AvatarPackageOpeningRequest", "CapsuleRequest", "ChargeRentPointRequest", "CollectionBoxRequest", "CompoundItemRequest", "DisjointItemRequest", "EnchantByBeadRequest", "EpicCraftRequest", "FinishQuestRequest",
            "ForgingRequest", "HousingBuyItemRequest", "HousingNetworkRequest", "IncreaseStatusRequest", "ItemSocketRequest", "JoustBetRequest", "LearnSkillRequest", "LevelUpTicketRequest", "LotteryOpeningRequest", "MagicBoxBatchOpeningRequest", "MagicBoxSingleOpeningRequest",
            "MagicSealRequest", "OutworldRequest", "PackageOpeningRequest", "QuestClearCouponRequest", "ReinforceItemRequest",
            "RenameCreatureRequest", "RentEquipmentRequest", "ResetItemQualityRequest", "ResetRandomOptionRequest", "SharedEffectRequest", "TeleportRequest", "UseStackableRequest", "VendingMachineRequest", "WarehouseRequest", "WeddingLovePointRequest" },
            wire.SelectMany(x => x.Productions).Distinct().Select(x => x.Name).Order());
        var commandRegistry = services.GetRequiredService<CommandHandlerRegistry>();
        var commands = services.GetServices<CommandHandlerRegistration>().Where(x => typeof(IRequestSettlementCommand).IsAssignableFrom(x.CommandType)).ToArray();
        Assert.Equal(new[] { "CapsuleCommand", "FinishQuestCommand", "HousingCommand", "IncreaseStatusCommand", "JoustBetCommand", "LearnSkillCommand", "QuestClearCouponCommand",
             "UseEntryAllowanceCommand", "UseFatiguePotionCommand", "UseLevelUpTicketCommand", "WeddingLovePointCommand" },
            commands.Select(x => x.CommandType.Name).Order());
        Assert.All(commands, x => {
            Assert.Same(x, commandRegistry.GetRegistration(x.CommandType));
            Assert.Equal(CommandExecutionKind.Transactional, x.ExecutionKind);
        });
        var use = commandRegistry.GetRegistration(typeof(Dfo.Server.Protocol.Game.UseStackable.GameUseStackableCommand));
        Assert.Equal(CommandRouteKind.Protocol, use.Route);
        Assert.Equal(new[] { CommandRegistrationExit.SettledRequiredExplicit, CommandRegistrationExit.CommittedReplayCurrentStateBatch }, use.RegistrationExits);
        var buy = commandRegistry.GetRegistration(typeof(Dfo.Server.Protocol.Game.BuyItem.GameBuyItemCommand));
        Assert.Equal(CommandRouteKind.Protocol, buy.Route);
        Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, buy.RegistrationExits);
        Assert.Equal(new[] { typeof(Dfo.Server.Protocol.Game.BuyItem.BuyItemRequest), typeof(Dfo.Server.Protocol.Game.BuyItem.HousingBuyItemRequest) }, buy.ProtocolMessages!.Select(x => x.MessageClrType));
        Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(buy.CommandType));
        foreach (var type in new[] { typeof(Dfo.Server.Protocol.Game.ReinforceItem.GameReinforceItemCommand), typeof(Dfo.Server.Protocol.Game.DisjointItem.GameDisjointItemCommand), typeof(Dfo.Server.Protocol.Game.EnchantByBead.GameEnchantByBeadCommand) })
        {
            var equipment = commandRegistry.GetRegistration(type);
            Assert.Equal(CommandRouteKind.Protocol, equipment.Route);
            Assert.Equal(CommandExecutionKind.Direct, equipment.ExecutionKind);
            Assert.Single(equipment.ProtocolMessages!);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, equipment.RegistrationExits);
            Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(type));
        }
        foreach (var (type, count) in new[] { (typeof(Dfo.Server.Protocol.Game.MagicSeal.GameMagicSealCommand), 2), (typeof(Dfo.Server.Protocol.Game.Forging.GameForgingCommand), 1), (typeof(Dfo.Server.Protocol.Game.Outworld.GameOutworldCommand), 2) })
        {
            var option = commandRegistry.GetRegistration(type);
            Assert.Equal(CommandRouteKind.Protocol, option.Route);
            Assert.Equal(CommandExecutionKind.Direct, option.ExecutionKind);
            Assert.Equal(count, option.ProtocolMessages!.Count);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, option.RegistrationExits);
            Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(type));
        }
        foreach (var type in new[] { typeof(Dfo.Server.Protocol.Game.CompoundItem.GameCompoundItemCommand), typeof(Dfo.Server.Protocol.Game.LotteryOpening.GameLotteryOpeningCommand), typeof(Dfo.Server.Protocol.Game.VendingMachine.GameVendingMachineCommand), typeof(Dfo.Server.Protocol.Game.EpicCraft.GameEpicCraftCommand) })
        {
            var opening = commandRegistry.GetRegistration(type);
            Assert.Equal(CommandRouteKind.Protocol, opening.Route); Assert.Equal(CommandExecutionKind.Direct, opening.ExecutionKind);
            Assert.Single(opening.ProtocolMessages!); Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, opening.RegistrationExits);
        }
        foreach (var type in new[] { typeof(Dfo.Server.Protocol.Game.MonsterDeath.GameMonsterDeathCommand), typeof(Dfo.Server.Protocol.Game.FinishLoading.GameFinishLoadingCommand), typeof(Dfo.Server.Protocol.Game.DungeonEntry.GameMoveMapCommand), typeof(Dfo.Server.Protocol.Game.IllusionTowerStage.GameIllusionTowerStageCommand) })
        {
            var loading = commandRegistry.GetRegistration(type);
            Assert.Equal(CommandRouteKind.Protocol, loading.Route); Assert.Equal(CommandExecutionKind.Direct, loading.ExecutionKind);
            Assert.Single(loading.ProtocolMessages!); Assert.Empty(loading.RegistrationExits ?? []);
            Assert.False(typeof(ISelectedSessionCommand).IsAssignableFrom(type));
            Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(type));
        }
        var protectedNonMain = services.GetServices<CommandHandlerRegistration>().Where(x => typeof(ISelectedSessionCommand).IsAssignableFrom(x.CommandType)
            && !typeof(IRequestSettlementCommand).IsAssignableFrom(x.CommandType)).Select(x => x.CommandType.Name).Order().ToArray();
        Assert.Equal(new[] { "ActivateClientHotkeyProfileCommand", "AltarActionCommand", "AltarDeathBatchCommand",  "BeginTournamentRewardsCommand", "ClaimWeeklyAttendanceCommand", "CleanupOnlineExpiredItemsCommand", "CompleteWeddingCleanupCommand", "EliminateTournamentCommand", "EnterMarriageChurchCommand", "EquipCarriedTitleReplacingMirrorCommand", "GrantClearExperienceCommand",
            "MarriageCeremonyCommand", "MarriageProposalCommand", "PrepareMarriageChurchCommand",  "QueryJoustHistoryCommand", "QueryJoustStateCommand", "QueryTitleBookCommand", "ReadReplayStateCommand", "ReconcileAchievementCommand", "RefillOnlineSouthernTicketCommand", "RefillOnlineTauTicketCommand", "SaveCharacterOptionCommand", "SaveClientSettingsCommand", "SelectTournamentRewardCommand", "SetTitleAppearanceCommand", "StoreTitleCommand", "WeddingCommentCommand", "WeddingGiftCommand", "WeddingLetterCommand", "WithdrawTitleCommand", "ExpireCharacterNameTagCommand" }.Concat(new[] { "LeaveDungeonCommand", "StartBossTowerCommand","DieInBossTowerCommand","LeaveBossTowerCommand","SynchronizeBossTowerLeaderCommand","SelectBossTowerResultCommand","PickupBossTowerItemCommand","RefillOnlineBossTowerTicketCommand" }).Order(), protectedNonMain);
        Assert.DoesNotContain(services.GetServices<CommandHandlerRegistration>(), x => x.CommandType.Name.Contains("ReplayGuard"));
        Assert.NotNull(services.GetRequiredService<EffectOperationLedger>());
    }
    private static Type RequestType(Type handler)
    {
        for (Type? type = handler; type is not null; type = type.BaseType)
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ClientMessageHandler<>)) return type.GetGenericArguments()[0];
        throw new InvalidOperationException($"No typed request for {handler}.");
    }
}
