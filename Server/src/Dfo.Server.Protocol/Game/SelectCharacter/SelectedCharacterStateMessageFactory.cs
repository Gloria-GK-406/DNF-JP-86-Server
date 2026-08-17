using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

public interface ISelectedCharacterStateMessageFactory
{
    IReadOnlyList<IGameServerMessage> CreateValidated(SelectCharacterResult result);
}

internal sealed class SelectedCharacterStateMessageFactoryService
    : ISelectedCharacterStateMessageFactory
{
    public IReadOnlyList<IGameServerMessage> CreateValidated(SelectCharacterResult result)
    {
        var messages = SelectedCharacterStateMessageFactory.Create(result);
        foreach (var message in messages)
            _ = message.EncodeBody();
        return messages;
    }
}

internal static class SelectedCharacterStateMessageFactory
{
    public static IReadOnlyList<GameStateSynchronizationMessage> Create(SelectCharacterResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var messages = new List<GameStateSynchronizationMessage>(59)
        {
            new CharacterEntryAcceptedMessage(
                result.Character, result.Account, result.CharacterEntry.Quests,
                result.World.Channel.Spawn.TownId, result.World.PreparedAt),
        };

        AddInventorySnapshots(messages, result);
        var equipment = result.CharacterInventory.Containers
            .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
        messages.Add(new CreatureListMessage(result.CharacterEntry.Features.Pets));
        messages.Add(new CurrentUserStateMessage(
            result.Character, result.CharacterEntry.Public, equipment, result.CharacterEntry.Features.Pets));
        messages.Add(new ExtendedUserStateMessage(
            result.Character, result.CharacterEntry.Public, result.CharacterEntry.Skills,
            equipment, result.World.PreparedAt));
        if (result.Character.Identity.Job == 12
            && result.CharacterEntry.Skills.KnightShieldDeck is { } knightDeck)
            messages.Add(new KnightShieldDeckMessage(knightDeck));
        messages.Add(new SkillStateMessage(result.CharacterEntry.Skills));
        if (result.Character.Identity.Job == 9
            && result.CharacterEntry.Skills.DarkKnightCombos is { } combos)
            messages.Add(new DarkKnightComboStateMessage(combos));
        messages.Add(new AvailableQuestListMessage(
            result.Character.Progression.Level, result.World.Content.AvailableQuests));
        messages.Add(new QuestVisibilityMessage(result.CharacterEntry.Quests.VisibilityFlags));
        messages.Add(new DailyChallengeStateMessage(result.CharacterEntry.Progression));
        messages.Add(new AccountGameOptionsMessage(result.Account.Settings));
        messages.Add(new HotkeyStateMessage(result.Account.Settings, result.CharacterEntry.Settings));
        messages.Add(new ServerEventBaselineMessage());
        messages.Add(new DungeonPermissionStateMessage(result.CharacterEntry.Progression.Dungeons));
        messages.Add(new CharacterOptionStateMessage(result.CharacterEntry.Settings.OptionBitmap));
        messages.Add(new GrowthWeaponStateMessage(result.CharacterEntry.Progression.GrowthWeaponStages));

        var titleBooks = result.CharacterEntry.Features.Collections.TitleBooks
            .OrderBy(value => value.Category).ToArray();
        RequireAtLeast(titleBooks, 5, "title-book categories");
        messages.AddRange(titleBooks.Take(5).Select(value => new TitleBookStateMessage(value)));
        messages.Add(new AchievementStateMessage(result.CharacterEntry.Features.Collections.Achievements));
        messages.Add(new SkillPointSlotStateMessage(result.CharacterEntry.Skills.SkillPointSlots));
        messages.Add(new ItemCooldownStateMessage(result.CharacterInventory.ItemValues.Cooldowns));
        messages.Add(new ItemEffectStateMessage(result.CharacterInventory.ItemValues.Effects));
        messages.Add(new PurchaseRestrictionCompatibilityMessage(result.Account.Relations.Unknown730Entries));
        messages.Add(new ClientEffectBaselineMessage());

        var boxes = result.CharacterEntry.Features.Collections.CollectionBoxes
            .OrderBy(value => value.BoxIndex).ToArray();
        RequireAtLeast(boxes, 2, "collection boxes");
        messages.AddRange(boxes.Take(2).Select(value => new CollectionBoxStateMessage(value)));
        messages.Add(new BossTowerBaselineMessage());
        messages.Add(new RentalStateMessage(
            result.CharacterInventory.Rentals, result.Account.Wallet.LuckyStar, result.World.PreparedAt));
        messages.Add(new EquipmentLockStateMessage(
            result.CharacterInventory.EquipmentLocks, result.AccountInventory.Cargo.EquipmentLocks));
        messages.Add(new ExpertJobStateMessage(result.CharacterEntry.Features.ExpertJob));
        messages.Add(new SupportCharacterStateMessage(
            result.Character.Identity.CharacterId,
            result.Account.Relations.CharacterRelations,
            result.World.PreparedAt));
        messages.Add(new DailyReviveCoinStateMessage(result.CharacterEntry.Progression.DailyReviveCoinClaimed));
        messages.Add(new CubeProgressionStateMessage(
            result.CharacterEntry.Progression.CubeType, result.CharacterEntry.Progression.CubeGrade));
        messages.Add(new PremiumServiceStateMessage(
            result.Account.Entitlements, result.CharacterEntry.Progression.PremiumUsageSlots));
        messages.Add(new LotteryProgressStateMessage(result.Account.Progression));
        messages.Add(new LuckyStarStateMessage(result.Account.Wallet.LuckyStar));
        messages.Add(new ChampionBreakStateMessage(result.CharacterEntry.Progression));
        messages.Add(new GoldLimitStateMessage(result.CharacterEntry.Progression.GoldLimitUpgradeLevel));
        messages.Add(new FatigueAccelerationStateMessage(
            result.CharacterEntry.Progression.FatigueAccelerationValue,
            result.CharacterEntry.Progression.FatigueAccelerationState));
        messages.Add(new MailboxStateMessage(result.CharacterEntry.Features.Mailbox));
        messages.Add(new PvpMissionStateMessage(result.CharacterEntry.Features.PvpMissions));

        var schedules = result.CharacterEntry.Progression.ScheduleCompatibility
            .OrderBy(value => value.SlotIndex).ToArray();
        RequireAtLeast(schedules, 3, "daily schedules");
        messages.AddRange(schedules.Take(3).Select(value => new DailyScheduleStateMessage(value)));
        messages.Add(new PcRoomPlayTimeStateMessage(result.Account.Relations.PcRoomPlayTimeState));
        messages.Add(new EntryMarkerMessage());
        messages.Add(new CurrentUserStateMessage(
            result.Character, result.CharacterEntry.Public, equipment, result.CharacterEntry.Features.Pets));
        messages.Add(new CeraBalanceStateMessage(result.Account.Wallet));
        messages.Add(new CrossServerFriendBaselineMessage());
        messages.Add(new CharacterPositionStateMessage(
            result.Character.Identity.CharacterId, result.World.Channel.Spawn));
        if (result.World.Content.PetWelcome is { Lines.Count: > 0 } welcome)
            messages.Add(new PetWelcomeStateMessage(result.Character.Identity.CharacterId, welcome));
        return messages;
    }

    private static void AddInventorySnapshots(
        ICollection<GameStateSynchronizationMessage> messages,
        SelectCharacterResult result)
    {
        InventoryContainerType[] order =
        [
            InventoryContainerType.Main,
            InventoryContainerType.Avatar,
            InventoryContainerType.PersonalCargo,
            InventoryContainerType.Pet,
            InventoryContainerType.AccountCargo,
        ];
        foreach (var containerType in order)
        {
            var container = containerType == InventoryContainerType.AccountCargo
                ? result.AccountInventory.Cargo.Container
                : result.CharacterInventory.Containers.SingleOrDefault(
                    value => value.ContainerType == containerType)
                    ?? throw Missing($"CharacterInventory.{containerType}");
            messages.Add(new InventorySnapshotMessage(
                container,
                result.CharacterInventory.VirtualResources,
                result.AccountInventory.VirtualResources,
                result.AccountInventory.Cargo.SelectionKey,
                result.AccountInventory.Cargo.Value32,
                result.World.PreparedAt));
        }
    }

    private static void RequireAtLeast<T>(IReadOnlyCollection<T> values, int count, string name)
    {
        if (values.Count < count)
            throw Missing($"Required {name} count is {count}, actual {values.Count}.");
    }

    private static InvalidDataException Missing(string name) =>
        new($"Required state synchronization value '{name}' is unavailable.");
}
