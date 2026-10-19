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

        // The order is the A21 reference sequence, frame for frame. It is not cosmetic: the
        // reference opens with the character record and only then sends the inventory, and this
        // server had it the other way round -- item lists arriving before the character they hang
        // off. Everything below is ordered against a captured A21 entry sequence rather than
        // against what reads well.
        var equipment = result.CharacterInventory.Containers
            .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
        messages.Add(new CurrentUserStateMessage(
            result.Character, result.CharacterEntry.Public, equipment, result.CharacterEntry.Features.Pets));

        var titleBooks = result.CharacterEntry.Features.Collections.TitleBooks
            .OrderBy(value => value.Category).ToArray();
        RequireAtLeast(titleBooks, 5, "title-book categories");
        messages.AddRange(titleBooks.Take(5).Select(value => new TitleBookStateMessage(value)));
        messages.Add(new AchievementStateMessage(result.CharacterEntry.Features.Collections.Achievements));
        messages.Add(new AvailableQuestListMessage(
            result.Character.Progression.Level, result.World.Content.AvailableQuests));
        messages.Add(new QuestVisibilityMessage(result.CharacterEntry.Quests.VisibilityFlags));
        // The captured sequence carries neither of these job-specific frames, so their position
        // relative to the skill block is left exactly where it already was rather than moved on
        // no evidence.
        if (result.Character.Identity.Job == 12
            && result.CharacterEntry.Skills.KnightShieldDeck is { } knightDeck)
            messages.Add(new KnightShieldDeckMessage(knightDeck));
        messages.Add(new SkillStateMessage(result.CharacterEntry.Skills));
        if (result.Character.Identity.Job == 9
            && result.CharacterEntry.Skills.DarkKnightCombos is { } combos)
            messages.Add(new DarkKnightComboStateMessage(combos));
        messages.Add(new CreatureListMessage(result.CharacterEntry.Features.Pets));
        messages.Add(new ExtendedUserStateMessage(
            result.Character, result.CharacterEntry.Public, result.CharacterEntry.Skills,
            equipment, result.World.PreparedAt));
        messages.Add(new UserStateBaselineMessage());
        messages.Add(new VillageObjectListMessage());
        messages.Add(new DungeonPermissionStateMessage(result.CharacterEntry.Progression.Dungeons));
        AddInventorySnapshots(messages, result);
        messages.Add(new GuildMedalBaselineMessage());
        messages.Add(new EpicBookStateMessage(0, result.EpicPieces ?? []));
        messages.Add(new UsableCountBaselineMessage());
        messages.Add(new DailyChallengeStateMessage(result.CharacterEntry.Progression));
        messages.Add(new AccountGameOptionsMessage(result.Account.Settings));
        messages.Add(new HotkeyStateMessage(result.Account.Settings, result.CharacterEntry.Settings, result.Character.Identity.Job == 10 ? (byte)1 : (byte)0));
        if (result.Events is { } events)
        {
            messages.Add(new Dfo.Server.Protocol.Game.WeeklyAttendance.EventInformationMessage(events.Information));
            messages.Add(new Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceStateMessage(events.Attendance));
            messages.AddRange(events.ScheduledDungeons.Select(x => new Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledDungeonStateMessage(x)));
            if (events.Joust is { } joust) messages.AddRange(Dfo.Server.Protocol.Game.Joust.JoustPayloadCodec.Initialization(joust));
        }
        else messages.Add(new ServerEventBaselineMessage());
        messages.Add(new CharacterOptionStateMessage(result.CharacterEntry.Settings.OptionBitmap));
        messages.Add(new SecondaryNoticeStateMessage(result.CharacterEntry.Settings.SeenNotices));
        messages.Add(new SkillPointSlotStateMessage(result.CharacterEntry.Skills.SkillPointSlots));
        messages.Add(new ItemCooldownStateMessage(result.CharacterInventory.ItemValues.Cooldowns));
        messages.Add(new ItemEffectStateMessage(result.CharacterInventory.ItemValues.Effects));
        messages.Add(new ClientEffectBaselineMessage());

        messages.Add(new CollectionBoxCompatibilityMessage(1));
        messages.Add(new CollectionBoxCompatibilityMessage(2));
        messages.Add(new BossTowerBaselineMessage());
        messages.Add(new RentalStateMessage(
            result.CharacterInventory.Rentals, result.Account.Wallet.LuckyStar, result.World.PreparedAt));
        messages.Add(new EquipmentLockStateMessage(
            result.CharacterInventory.EquipmentLocks, result.AccountInventory.Cargo.EquipmentLocks));
        messages.Add(new ExpertJobStateMessage(result.CharacterEntry.Features.ExpertJob));
        messages.Add(new DailyReviveCoinStateMessage(result.CharacterEntry.Progression.DailyReviveCoinClaimed));
        messages.Add(new CubeProgressionStateMessage(
            result.CharacterEntry.Progression.CubeType, result.CharacterEntry.Progression.CubeGrade));
        // PREMIUM_SERVICE is deliberately absent. The A21 reference session never sends it --
        // not at 0x036F and not at the older 0x0312 -- anywhere in its 458 frames, while the
        // client does register a receive handler for it. Pushing seventy-seven bytes at a live
        // handler the reference leaves alone is a difference we introduced, not one it asked
        // for. The record and its encoder stay registered for a path that does ask.
        if (result.Engagement is { IsMarried: true } married)
        {
            messages.Add(new Dfo.Server.Protocol.Game.MarriageProposal.WeddingInfoMessage(married));
            if (result.Housing is { Placements.Count: > 0 } || result.Housing?.Tree is not null) messages.Add(new HousingRoomMessage(result.Housing!));
            else messages.Add(new CoupleRoomEmptyCompatibilityMessage());
        }
        else
        {
            messages.Add(new CoupleRoomEmptyCompatibilityMessage());
            if (result.Engagement is { } engagement) messages.Add(new Dfo.Server.Protocol.Game.MarriageProposal.WeddingInfoMessage(engagement));
        }
        if (result.CollectionBoxes is { } collections) messages.AddRange(Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxContentsMessage.Full(collections));
        if (result.Capsules is { } capsules) messages.Add(new Dfo.Server.Protocol.Game.MarriageHousing.CapsuleStateMessage(capsules));
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
        messages.Add(new PartyBaselineMessage());
        messages.Add(new PurchaseRestrictionCompatibilityMessage(result.Account.Relations.Unknown730Entries));
        messages.Add(new EntryMarkerMessage());
        // Subtype six, not a second copy of the subtype the batch opened with. The older client
        // took nothing here; A21's own server sends this frame once, right after the marker.
        messages.Add(new TownReadyStateMessage(result.Character.Identity.CharacterId));
        messages.Add(new MercenarySupportStateMessage());
        messages.Add(new CeraBalanceStateMessage(result.Account.Wallet));
        messages.Add(new CrossServerFriendBaselineMessage());
        messages.Add(new CharacterPositionStateMessage(
            result.Character.Identity.CharacterId, result.World.Channel.Spawn));
        // UPDATE_ITEM_LIST is not sent from here. The reference carries exactly one 0x000E in
        // this window and it is not an empty envelope: it is the daily refill's own result, five
        // entries in slots 0xB1..0xB5. Sending a zero-count frame here and the real delta later
        // produced two frames where the reference has one, in neither of its positions.

        // NOTI 0x019F and 0x019D are deliberately absent. The reference entry sequence carries
        // neither: it answers 0x019F on the client's own request after entry, and never sends
        // 0x019D in this window. Both encoders stay registered for the paths that do ask.
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
