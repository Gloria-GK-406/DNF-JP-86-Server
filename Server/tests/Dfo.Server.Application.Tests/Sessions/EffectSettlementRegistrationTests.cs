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
        Assert.Equal(new ushort[] { 0x0019, 0x001A, 0x001B, 0x001D, 0x001E, 0x0022, 0x002C, 0x0050, 0x0051, 0x0063, 0x0064,
            0x00A0, 0x00C9, 0x00CC, 0x00CD, 0x00CE, 0x00D0, 0x00D7, 0x00ED, 0x00FB, 0x0110, 0x0131, 0x0132, 0x0133, 0x0134, 0x0191, 0x0198, 0x01A2, 0x01AF, 0x01B6, 0x01B7, 0x01C8, 0x0204, 0x0207, 0x0313, 0x0379, 0x037A, 0x03E5, 0x03E6, 0x03E7, 0x03E8, 0x03F4, 0x03F5,
            0x03FD, 0x03FE, 0x03FF, 0x0400, 0x0401, 0x0402, 0x0403, 0x0404, 0x0405, 0x0406, 0x0450, 0x04E7 },
            wire.Select(x => x.Registration.Type).Order());
        Assert.All(wire, x => { Assert.Equal(1, x.Registration.Selector); Assert.Single(x.Productions); });
        Assert.Equal(new[] { "AvatarCompoundRequest", "AvatarPackageOpeningRequest", "CapsuleRequest", "CollectionBoxRequest", "CompoundItemRequest", "DisjointItemRequest", "EnchantByBeadRequest", "EpicCraftRequest", "FinishQuestRequest",
            "ForgingRequest", "HousingNetworkRequest", "IncreaseStatusRequest", "ItemSocketRequest", "JoustBetRequest", "LearnSkillRequest", "LevelUpTicketRequest", "LotteryOpeningRequest", "MagicBoxBatchOpeningRequest", "MagicBoxSingleOpeningRequest",
            "MagicSealRequest", "OutworldRequest", "PackageOpeningRequest", "QuestClearCouponRequest", "ReinforceItemRequest",
            "RenameCreatureRequest", "ResetItemQualityRequest", "ResetRandomOptionRequest", "SharedEffectRequest", "TeleportRequest", "UseStackableRequest", "VendingMachineRequest", "WarehouseRequest", "WeddingLovePointRequest" },
            wire.SelectMany(x => x.Productions).Distinct().Select(x => x.Name).Order());
        var commandRegistry = services.GetRequiredService<CommandHandlerRegistry>();
        var commands = services.GetServices<CommandHandlerRegistration>().Where(x => typeof(IRequestSettlementCommand).IsAssignableFrom(x.CommandType)).ToArray();
        Assert.Equal(new[] { "AvatarCompoundCommand", "AvatarPackageOpeningCommand", "CapsuleCommand", "CollectionBoxCommand", "CompoundItemCommand", "EnchantByBeadCommand", "EpicCraftCommand", "FinishQuestCommand", "ForgingCommand",
            "HousingBuyItemCommand", "HousingCommand", "IncreaseStatusCommand", "ItemSocketCommand", "JoustBetCommand", "LearnSkillCommand", "LotteryOpeningCommand", "MagicBoxBatchOpeningCommand", "MagicBoxSingleOpeningCommand", "MagicSealCommand",
            "NpcDisjointItemCommand", "OutworldCommand", "PackageOpeningCommand", "PortableDisjointItemCommand", "QuestClearCouponCommand",
            "ReinforceItemCommand", "RenameCreatureCommand", "ResetItemQualityCommand", "ResetRandomOptionCommand", "SharedEffectCommand", "TeleportCommand", "UseEntryAllowanceCommand", "UseFatiguePotionCommand", "UseLevelUpTicketCommand", "UseStackableCommand", "VendingMachineCommand", "WarehouseCommand", "WeddingLovePointCommand" },
            commands.Select(x => x.CommandType.Name).Order());
        Assert.All(commands, x => {
            Assert.Same(x, commandRegistry.GetRegistration(x.CommandType));
            Assert.Equal(CommandExecutionKind.Transactional, x.ExecutionKind);
        });
        var protectedNonMain = services.GetServices<CommandHandlerRegistration>().Where(x => typeof(ISelectedSessionCommand).IsAssignableFrom(x.CommandType)
            && !typeof(IRequestSettlementCommand).IsAssignableFrom(x.CommandType)).Select(x => x.CommandType.Name).Order().ToArray();
        Assert.Equal(new[] { "ActivateClientHotkeyProfileCommand", "AltarActionCommand", "AltarDeathBatchCommand", "ApplyMonsterKillCommand", "BeginTournamentRewardsCommand", "ClaimWeeklyAttendanceCommand", "CleanupOnlineExpiredItemsCommand", "CompleteWeddingCleanupCommand", "EliminateTournamentCommand", "EnterMarriageChurchCommand", "EquipCarriedTitleReplacingMirrorCommand", "GrantClearExperienceCommand", "GrantKillExperienceCommand",
            "MarriageCeremonyCommand", "MarriageProposalCommand", "PrepareMarriageChurchCommand", "ProcessMonsterDeathCommand", "QueryJoustHistoryCommand", "QueryJoustStateCommand", "QueryTitleBookCommand", "ReadReplayStateCommand", "ReconcileAchievementCommand", "RefillOnlineTauTicketCommand", "SaveCharacterOptionCommand", "SaveClientSettingsCommand", "SelectTournamentRewardCommand", "SetTitleAppearanceCommand", "StoreTitleCommand", "WeddingCommentCommand", "WeddingGiftCommand", "WeddingLetterCommand", "WithdrawTitleCommand" }, protectedNonMain);
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
