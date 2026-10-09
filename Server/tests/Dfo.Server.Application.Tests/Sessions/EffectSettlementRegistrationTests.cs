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
                .Select(x => RequestType(handlerRegistry.GetHandlerType(x.MessageType))).Where(x => typeof(IEffectSettlementRequest).IsAssignableFrom(x)).Distinct().ToArray()))
            .Where(x => x.Productions.Length > 0).ToArray();
        Assert.Equal(new ushort[] { 0x0019, 0x001A, 0x001B, 0x001D, 0x0022, 0x002C, 0x0050, 0x0064,
            0x00A0, 0x00CC, 0x00CD, 0x00D0, 0x00D7, 0x00ED, 0x0110, 0x0131, 0x0132, 0x0133, 0x0134, 0x0191, 0x0198, 0x01A2, 0x01AF, 0x01B6, 0x01B7, 0x0204, 0x0207, 0x0450, 0x04E7 },
            wire.Select(x => x.Registration.Type).Order());
        Assert.All(wire, x => { Assert.Equal(1, x.Registration.Selector); Assert.Single(x.Productions); });
        Assert.Equal(new[] { "AvatarPackageOpeningRequest", "CompoundItemRequest", "DisjointItemRequest", "EnchantByBeadRequest", "FinishQuestRequest",
            "ForgingRequest", "JoustBetRequest", "LearnSkillRequest", "LevelUpTicketRequest", "LotteryOpeningRequest", "MagicBoxBatchOpeningRequest", "MagicBoxSingleOpeningRequest",
            "MagicSealRequest", "OutworldRequest", "PackageOpeningRequest", "QuestClearCouponRequest", "ReinforceItemRequest",
            "RenameCreatureRequest", "TeleportRequest", "UseStackableRequest", "VendingMachineRequest", "WarehouseRequest" },
            wire.SelectMany(x => x.Productions).Distinct().Select(x => x.Name).Order());
        var commandRegistry = services.GetRequiredService<CommandHandlerRegistry>();
        var commands = services.GetServices<CommandHandlerRegistration>().Where(x => typeof(IRequestSettlementCommand).IsAssignableFrom(x.CommandType)).ToArray();
        Assert.Equal(new[] { "AvatarPackageOpeningCommand", "CompoundItemCommand", "EnchantByBeadCommand", "FinishQuestCommand", "ForgingCommand",
            "JoustBetCommand", "LearnSkillCommand", "LotteryOpeningCommand", "MagicBoxBatchOpeningCommand", "MagicBoxSingleOpeningCommand", "MagicSealCommand",
            "NpcDisjointItemCommand", "OutworldCommand", "PackageOpeningCommand", "PortableDisjointItemCommand", "QuestClearCouponCommand",
            "ReinforceItemCommand", "RenameCreatureCommand", "TeleportCommand", "UseFatiguePotionCommand", "UseLevelUpTicketCommand", "UseStackableCommand", "VendingMachineCommand", "WarehouseCommand" },
            commands.Select(x => x.CommandType.Name).Order());
        Assert.All(commands, x => {
            Assert.Same(x, commandRegistry.GetRegistration(x.CommandType));
            Assert.Equal(CommandExecutionKind.Transactional, x.ExecutionKind);
        });
        var protectedNonMain = services.GetServices<CommandHandlerRegistration>().Where(x => typeof(ISelectedSessionCommand).IsAssignableFrom(x.CommandType)
            && !typeof(IRequestSettlementCommand).IsAssignableFrom(x.CommandType)).Select(x => x.CommandType.Name).Order().ToArray();
        Assert.Equal(new[] { "ApplyMonsterKillCommand", "ClaimWeeklyAttendanceCommand", "EquipCarriedTitleReplacingMirrorCommand", "GrantClearExperienceCommand", "GrantKillExperienceCommand",
            "QueryJoustHistoryCommand", "QueryJoustStateCommand", "QueryTitleBookCommand", "ReadReplayStateCommand", "ReconcileAchievementCommand", "SetTitleAppearanceCommand", "StoreTitleCommand", "WithdrawTitleCommand" }, protectedNonMain);
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
