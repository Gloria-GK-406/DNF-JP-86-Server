using System.Text;
using System.Text.Json;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

internal static partial class SelectCharacterStaticPayloadCodec
{
    public static byte[] EncodeNotification(SelectCharacterResult result, ushort type, int occurrence = 0) =>
        type switch
        {
            0x0002 => EncodeUserInfo(result, occurrence),
            0x0005 => EncodeDungeonPermissions(result),
            0x000D => throw new ArgumentException("Inventory notification requires an explicit container type."),
            0x0013 => EncodeSkills(result),
            0x0015 => EncodeAvailableQuests(result),
            0x0016 => EncodePosition(result),
            0x0035 => EncodeCera(result),
            0x0061 => EncodeMailbox(result),
            0x0069 => EncodeCreatures(result),
            0x006C or 0x017B or 0x021F or 0x007C or 0x0111 => EncodeObservedConstant(type),
            0x00AC => EncodeItemValues(result.CharacterInventory.ItemValues.Cooldowns),
            0x00AD => EncodeGameOptions(result),
            0x00AE => EncodeItemValues(result.CharacterInventory.ItemValues.Effects),
            0x00B1 => [result.CharacterEntry.Progression.DailyReviveCoinClaimed ? (byte)1 : (byte)0],
            0x00CD => EncodeExpert(result),
            0x00FB => EncodeEquipmentLocks(result),
            0x0158 => EncodePvpMissions(result),
            0x015F => EncodeSkillPointSlots(result),
            0x0164 => EncodeQuestVisibility(result),
            0x0166 => EncodeTitleBook(result, occurrence),
            0x0167 => EncodeAchievements(result),
            0x0187 => EncodeCharacterOption(result),
            0x019D => U32(result.Account.Wallet.LuckyStar),
            0x019F => EncodeSupportCharacter(result),
            0x01A8 => [result.Account.Relations.PcRoomPlayTimeState],
            0x01B9 => EncodeGrowthWeapon(result),
            0x01C7 => EncodeHotkeys(result),
            0x01EB => EncodeFatigueAcceleration(result),
            0x0245 => EncodeKnightShieldDeck(result),
            0x025B => EncodeChampionBreak(result),
            0x0286 => EncodeDailyChallenge(result),
            0x02D5 => EncodeDailySchedule(result, occurrence),
            0x02DA => EncodeCompatibilityEntries(result),
            0x0300 => [result.CharacterEntry.Progression.CubeType, result.CharacterEntry.Progression.CubeGrade],
            0x0331 => [result.CharacterEntry.Progression.GoldLimitUpgradeLevel],
            0x0357 => EncodeRentals(result),
            0x0381 => EncodeCollectionBox(result, occurrence),
            0x03D8 => EncodeLotteryProgress(result),
            0x01C0 => EncodeDarkKnightCombos(result),
            0x0077 => EncodePetWelcome(result),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported static notification body."),
        };

    public static byte[] EncodePremiumService(SelectCharacterResult result)
    {
        var data = new byte[74];
        foreach (var premium in result.Account.Entitlements.Premiums)
        {
            var slot = premium.PremiumType - 580;
            if (slot is < 0 or >= 8) continue;
            WriteInt32(data, 6 + slot * 9,
                checked((int)Math.Min(premium.ExpiresAtUnixSeconds, int.MaxValue)));
        }
        foreach (var usage in result.CharacterEntry.Progression.PremiumUsageSlots)
        {
            if (usage.SlotIndex >= 8 || usage.UsedCount < 0)
                throw new InvalidDataException("Premium usage slot is outside the observed 8-slot layout.");
            WriteInt32(data, 10 + usage.SlotIndex * 9, usage.UsedCount);
        }
        using var stream = new MemoryStream(77);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write((ushort)1);
        writer.Write(data);
        return stream.ToArray();
    }

    private static byte[] EncodeCreatures(SelectCharacterResult result)
    {
        var pets = result.CharacterEntry.Features.Pets;
        if (pets.Count > byte.MaxValue) throw CountError("creature", "u8");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)pets.Count));
        foreach (var pet in pets)
        {
            writer.Write(checked((int)pet.PetKey)); writer.Write(checked((byte)pet.Field04));
            writer.Write(pet.ModeFlag); writer.Write(pet.ProgressValue);
            if (pet.ModeFlag == 1) { writer.Write(checked((byte)pet.Mode1Field0A)); writer.Write(checked((byte)pet.Mode1Field0B)); }
            writer.Write(checked((byte)pet.FieldAfterValue)); WriteDstr(writer, pet.CreatureText); writer.Write(pet.TailFlag);
        }
        return stream.ToArray();
    }

    private static byte[] EncodeLotteryProgress(SelectCharacterResult result)
    {
        const int bodyLength = 204;
        const int recordCount = 8;
        const int recordLength = 24;
        const int claimCapacity = 20;
        var records = result.Account.Progression.LotteryProgress
            ?? throw Missing("Account.Progression.LotteryProgress");
        var normalized = records.Where(value => value.ItemTemplateId > 0)
            .Take(recordCount).ToArray();
        var body = new byte[bodyLength];
        if (normalized.Length == 0)
            return body;

        WriteInt32(body, 0, 2);
        WriteInt32(body, 4, -1);
        WriteInt32(body, 8, -1);
        for (var recordIndex = 0; recordIndex < normalized.Length; recordIndex++)
        {
            var record = normalized[recordIndex];
            var offset = 12 + recordIndex * recordLength;
            WriteInt32(body, offset, record.ItemTemplateId);
            var claims = record.ClaimedRewardIndexes
                ?? throw Missing($"Account.Progression.LotteryProgress[{recordIndex}].ClaimedRewardIndexes");
            foreach (var (claim, claimIndex) in claims.Where(value => value < claimCapacity)
                         .Distinct().Order().Take(claimCapacity).Select((value, index) => (value, index)))
            {
                body[offset + 4 + claimIndex] = checked((byte)(claim + 1));
            }
        }
        return body;
    }

    private static byte[] EncodeSupportCharacter(SelectCharacterResult result)
    {
        var ownerId = result.Character.Identity.CharacterId;
        if (result.Account.Relations.CharacterRelations.Any(value =>
                value.SupportProjection is not null && value.OwnerCharacterId != ownerId))
            throw new InvalidDataException("Support-character projection belongs to a different selected owner.");
        var relations = result.Account.Relations.CharacterRelations
            .Where(value => value.OwnerCharacterId == ownerId).ToArray();
        if (relations.Length == 0)
            return new byte[2];
        if (relations.Length != 1 || relations[0].Slot != 0)
            throw new InvalidDataException("Support-character projection requires one owner relation in slot zero.");
        var relation = relations[0];
        var projection = relation.SupportProjection ?? throw Missing("Account.Relations.SupportProjection");
        if (relation.OwnerCharacterId != ownerId
            || projection.Character.Identity.CharacterId != relation.SupportCharacterId
            || projection.Entry.CharacterId != relation.SupportCharacterId
            || projection.Inventory.CharacterId != relation.SupportCharacterId)
            throw new InvalidDataException("Support-character projection does not match the owner relation.");
        var stats = projection.Entry.Public.Subtype1;
        var equipment = projection.Inventory.Containers
            .Where(value => value.ContainerType == InventoryContainerType.Equipment)
            .SelectMany(value => value.Items).Where(value => value.SlotIndex is >= 0 and <= 29)
            .OrderBy(value => value.SlotIndex).ToArray();
        var page = projection.Entry.Skills.Pages.SingleOrDefault(
            value => value.PageIndex == stats.SkillTreeIndex);
        var skills = page?.Skills.OrderBy(value => value.Slot).ToArray() ?? [];
        if (equipment.Length > byte.MaxValue || skills.Length is 0 or > byte.MaxValue)
            throw new InvalidDataException("Support-character equipment or active skill page is invalid.");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)ownerId.Value));
        WriteDstr(writer, projection.Character.Identity.Name);
        writer.Write(projection.Character.Progression.Level);
        writer.Write(projection.Character.Identity.Job);
        writer.Write(projection.Character.Identity.GrowType);
        writer.Write(checked((ushort)relation.SkillId));
        writer.Write(82u);
        writer.Write(EncodeSupportStats(stats));
        writer.Write(checked((byte)equipment.Length));
        foreach (var item in equipment)
            WriteNoti2EquippedItem(writer, item, result.World.PreparedAt);
        writer.Write(projection.Entry.Public.CloneTitleItemId);
        writer.Write(checked((byte)skills.Length));
        writer.Write(checked((byte)stats.SkillTreeIndex));
        foreach (var skill in skills)
        {
            writer.Write(checked((byte)skill.Slot));
            writer.Write(checked((ushort)skill.SkillId));
            writer.Write(skill.Level);
        }
        writer.Write((byte)0);
        writer.Write(0u);

        using var body = new MemoryStream();
        using var bodyWriter = new BinaryWriter(body);
        bodyWriter.Write((ushort)1);
        bodyWriter.Write(stream.ToArray());
        return body.ToArray();
    }

    private static void WriteNoti2EquippedItem(
        BinaryWriter writer,
        InventoryItemState item,
        DateTimeOffset preparedAt)
    {
        const byte avatarKind = 8;
        const byte creatureKind = 5;
        var core = item.Core ?? throw Missing($"SupportEquipment.Core[{item.StorageEntryId}]");
        if (item.SlotIndex is < 0 or > byte.MaxValue)
            throw new InvalidDataException("NOTI-2 equipment slot must fit u8.");
        if (core.ItemKind == avatarKind && item.AvatarDetail is null)
            throw Missing($"SupportEquipment.AvatarDetail[{item.StorageEntryId}]");
        if (core.ItemKind != avatarKind && item.AvatarDetail is not null)
            throw new InvalidDataException("Non-avatar support equipment carries avatar detail.");

        writer.Write(checked((byte)item.SlotIndex));
        writer.Write(core.ItemId);
        writer.Write(unchecked((uint)(core.ItemKind == avatarKind
            ? RemainingSeconds(item.AvatarDetail!.ExpireDate, preparedAt)
            : core.Value)));
        writer.Write(core.Attribute);
        writer.Write(core.Durability);
        writer.Write(core.ItemKind == avatarKind && item.AvatarDetail!.ClearAvatarId != 0
            ? unchecked((uint)item.AvatarDetail.ClearAvatarId)
            : core.SealFlag);
        writer.Write(core.EnchantCardId);
        writer.Write(core.EnchantUpgradeCount);
        writer.Write(core.AmplifyType);
        writer.Write(core.AmplifyValue);

        if (item.SlotIndex <= 10)
        {
            if (core.ItemKind == avatarKind)
            {
                if (item.AvatarDetail!.JewelSocket.Length != 30)
                    throw new InvalidDataException("Support avatar jewel socket must contain exactly 30 bytes.");
                writer.Write(30);
                writer.Write(item.AvatarDetail.JewelSocket);
            }
            else
            {
                writer.Write(0);
            }
            writer.Write(4);
            writer.Write(item.AvatarDetail?.Color1 ?? 0);
            writer.Write(item.AvatarDetail?.Color2 ?? 0);
        }

        if (item.SlotIndex == 24)
        {
            if (core.ItemKind == creatureKind && item.Noti2CreatureMarker is null)
                throw Missing($"SupportEquipment.Noti2CreatureMarker[{item.StorageEntryId}]");
            writer.Write(core.ItemKind == creatureKind
                ? item.Noti2CreatureMarker!.Value
                : core.Marker16 == -1 ? 0u : unchecked((uint)core.Marker16));
        }

        var chronicle = new[] { core.ChronicleOption0, core.ChronicleOption1 }
            .Where(value => value.Id != 0).ToArray();
        writer.Write(checked((byte)chronicle.Length));
        foreach (var option in chronicle)
        {
            writer.Write(option.Id);
            writer.Write(option.CharacterJob);
            writer.Write(option.FirstGrowType);
            writer.Write(option.EquipmentType);
            writer.Write(option.OptionNumber);
        }
        writer.Write(core.ExpireTime);

        var emblemCount = Math.Min(2, (int)core.EmblemSocketCount);
        writer.Write(checked((byte)emblemCount));
        if (emblemCount > 0) writer.Write(core.EmblemId1);
        if (emblemCount > 1) writer.Write(core.EmblemId2);
        writer.Write(core.Rune);

        var random = new[] { core.RandomOption0, core.RandomOption1, core.RandomOption2 }
            .Where(value => value.Type != 0).ToArray();
        writer.Write(checked((byte)random.Length));
        foreach (var option in random)
        {
            writer.Write(option.Type);
            writer.Write(option.Value1);
            writer.Write(option.Value2);
        }
        if (random.Length > 0)
        {
            writer.Write(core.RandomOptionState);
            var hasExplicitTail = core.RandomOptionState != 0
                || core.RandomOptionChangedIndex != 0
                || core.RandomOptionChangeState != 0
                || core.RandomOptionChange.Type != 0
                || core.RandomOptionChange.Value1 != 0
                || core.RandomOptionChange.Value2 != 0;
            var changedIndex = hasExplicitTail ? core.RandomOptionChangedIndex : byte.MaxValue;
            writer.Write(changedIndex);
            if (changedIndex != byte.MaxValue)
            {
                writer.Write(core.RandomOptionChangeState);
                writer.Write(core.RandomOptionChange.Type);
                writer.Write(core.RandomOptionChange.Value1);
                writer.Write(core.RandomOptionChange.Value2);
            }
        }

        writer.Write(core.GenuineUpgrade);
        writer.Write(core.EmancipateEquipmentLevel);
        writer.Write(core.TradeRestriction);
        writer.Write(core.TailUnknown0);
        writer.Write(core.TailUnknown1);
        writer.Write(core.TailUnknown2);
        writer.Write(core.TailUnknown3);
        writer.Write(core.RemainUseCount);
        writer.Write((byte)0);
    }

    private static int RemainingSeconds(int expiresAt, DateTimeOffset preparedAt) =>
        expiresAt <= 0 ? 0 : checked((int)Math.Clamp(
            (long)expiresAt - preparedAt.ToUnixTimeSeconds(), 0, int.MaxValue));

    private static byte[] EncodeSupportStats(CharacterSubtype1EntryState value)
    {
        using var stream = new MemoryStream(82);
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((uint)value.HpMaximum));
        writer.Write(checked((uint)value.MpMaximum));
        writer.Write(checked((short)value.PhysicalAttack));
        writer.Write(checked((short)value.PhysicalDefense));
        writer.Write(checked((short)value.MagicalAttack));
        writer.Write(checked((short)value.MagicalDefense));
        writer.Write(value.FireResistance);
        writer.Write(value.WaterResistance);
        writer.Write(value.DarkResistance);
        writer.Write(value.LightResistance);
        writer.Write(new byte[34]);
        writer.Write(checked((uint)value.InventoryLimit));
        writer.Write(value.HpRegenSpeed);
        writer.Write(value.MpRegenSpeed);
        writer.Write(checked((uint)value.MoveSpeed));
        writer.Write(checked((ushort)value.AttackSpeed));
        writer.Write(checked((ushort)value.CastSpeed));
        writer.Write(value.HitRecovery);
        writer.Write(value.JumpPower);
        writer.Write(value.Weight);
        return stream.ToArray();
    }

    private static byte[] EncodeSkills(SelectCharacterResult result)
    {
        var skills = result.CharacterEntry.Skills;
        var mirror = skills.ProtocolMirror ?? throw Missing("Skills.ProtocolMirror");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        for (byte pageIndex = 0; pageIndex < 2; pageIndex++)
        {
            var page = skills.Pages.SingleOrDefault(value => value.PageIndex == pageIndex);
            var entries = page?.Skills.OrderBy(value => value.Slot).ToArray() ?? [];
            if (entries.Length > byte.MaxValue) throw CountError("skill page", "u8");
            writer.Write(pageIndex == 0 ? mirror.Page0Sp : mirror.Page1Sp);
            writer.Write(checked((byte)entries.Length));
            foreach (var skill in entries)
            {
                writer.Write(checked((byte)skill.Slot)); writer.Write(checked((ushort)skill.SkillId)); writer.Write(skill.Level);
                if (skill.ExtraValues.Count > byte.MaxValue) throw CountError("skill extra", "u8");
                writer.Write(checked((byte)skill.ExtraValues.Count)); writer.Write(skill.ExtraValues.ToArray());
            }
        }
        writer.Write(mirror.Page0Tp); writer.Write(mirror.Page1Tp); return stream.ToArray();
    }

    private static byte[] EncodeAvailableQuests(SelectCharacterResult result)
    {
        var quests = result.World.Content.AvailableQuests.Quests;
        if (quests.Count > ushort.MaxValue) throw CountError("available quest", "u16");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(result.Character.Progression.Level); writer.Write(checked((ushort)quests.Count));
        foreach (var quest in quests) writer.Write(checked((ushort)quest.QuestId)); return stream.ToArray();
    }

    private static byte[] EncodeQuestVisibility(SelectCharacterResult result)
    {
        var flags = result.CharacterEntry.Quests.VisibilityFlags;
        if (flags.Length != 30_000) throw new InvalidDataException("Quest visibility bitmap must contain exactly 30000 bytes.");
        using var stream = new MemoryStream(30_004); using var writer = new BinaryWriter(stream);
        writer.Write(flags.Length); writer.Write(flags); return stream.ToArray();
    }

    private static byte[] EncodeDailyChallenge(SelectCharacterResult result)
    {
        var state = result.CharacterEntry.Progression;
        if (state.DailyChallengeClaimFlags.Length != 6) throw new InvalidDataException("Daily challenge claim flags must contain exactly 6 bytes.");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Math.Max(1u, state.DailyChallengeCharacterLevel)); writer.Write(checked((uint)state.DailyChallenges.Count));
        foreach (var group in state.DailyChallenges.OrderBy(value => value.GroupIndex))
        {
            writer.Write(group.GroupId); writer.Write(checked((uint)group.Entries.Count));
            foreach (var entry in group.Entries) { writer.Write(entry.TrackId); writer.Write(entry.ValueA); writer.Write(entry.ValueB); }
        }
        writer.Write(6u); writer.Write(state.DailyChallengeClaimFlags); writer.Write(checked((uint)state.DailyChallengeTailIds.Count));
        foreach (var id in state.DailyChallengeTailIds) writer.Write(id); return stream.ToArray();
    }

    private static byte[] EncodeGameOptions(SelectCharacterResult result)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        WriteBlob(writer, result.Account.Settings.MainGameOptions); WriteBlob(writer, result.Account.Settings.QuickChatBank0);
        WriteBlob(writer, result.Account.Settings.QuickChatBank1); return stream.ToArray();
    }

    private static byte[] EncodeHotkeys(SelectCharacterResult result)
    {
        var slots = result.CharacterEntry.Settings.HotkeySlots;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(result.Account.Settings.HotkeyKeyType); writer.Write(checked(slots.Count * 2));
        foreach (var slot in slots) writer.Write(slot); return stream.ToArray();
    }

    private static byte[] EncodeDungeonPermissions(SelectCharacterResult result)
    {
        var entries = result.CharacterEntry.Progression.Dungeons;
        if (entries.Count > ushort.MaxValue) throw CountError("dungeon", "u16");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)entries.Count)); foreach (var entry in entries) { writer.Write(checked((ushort)entry.DungeonId)); writer.Write(entry.State); }
        return stream.ToArray();
    }

    private static byte[] EncodeCharacterOption(SelectCharacterResult result)
    {
        var option = result.CharacterEntry.Settings.OptionBitmap;
        if (option is not null) return [.. option];
        return new byte[4];
    }

    private static byte[] EncodeGrowthWeapon(SelectCharacterResult result)
    {
        var values = result.CharacterEntry.Progression.GrowthWeaponStages;
        if (values.Count > byte.MaxValue) throw CountError("growth weapon", "u8");
        return [checked((byte)values.Count), .. values];
    }

    private static byte[] EncodeTitleBook(SelectCharacterResult result, int occurrence)
    {
        var categories = result.CharacterEntry.Features.Collections.TitleBooks.OrderBy(value => value.Category).ToArray();
        if (occurrence < 0 || occurrence >= categories.Length) throw Missing($"TitleBooks[{occurrence}]");
        var category = categories[occurrence];
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(category.InfoType); writer.Write(category.OwnerId16); writer.Write((int)category.Category); writer.Write(category.Entries.Count);
        foreach (var entry in category.Entries)
        {
            writer.Write(entry.SlotIndex); writer.Write(entry.ItemId); writer.Write(entry.Value); writer.Write(entry.Attribute);
            writer.Write(entry.Durability); writer.Write(entry.SealFlag); writer.Write(entry.EnchantIndex);
            writer.Write(entry.EnchantUpgradeCount); writer.Write(entry.AmplifyType); writer.Write(entry.AmplifyValue);
        }
        return stream.ToArray();
    }

    private static byte[] EncodeAchievements(SelectCharacterResult result)
    {
        var entries = result.CharacterEntry.Features.Collections.Achievements;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(entries.Count);
        foreach (var e in entries) { writer.Write(e.AchievementId); writer.Write(e.P1); writer.Write(e.P2); writer.Write(e.P3); writer.Write(e.P4); }
        return stream.ToArray();
    }

    private static byte[] EncodeSkillPointSlots(SelectCharacterResult result)
    {
        var entries = result.CharacterEntry.Skills.SkillPointSlots;
        if (entries.Count > byte.MaxValue) throw CountError("skill point slot", "u8");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(checked((byte)entries.Count));
        foreach (var entry in entries) { writer.Write(entry.SlotIndex); writer.Write(checked((ushort)entry.Value)); } return stream.ToArray();
    }

    private static byte[] EncodeItemValues(IReadOnlyList<InventoryItemValueState> entries)
    {
        if (entries.Count > byte.MaxValue) throw CountError("item value", "u8");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(checked((byte)entries.Count));
        foreach (var entry in entries.OrderBy(value => value.SortOrder)) { writer.Write(entry.ItemId); writer.Write(entry.Value); } return stream.ToArray();
    }

    private static byte[] EncodeCompatibilityEntries(SelectCharacterResult result)
    {
        var entries = result.Account.Relations.Unknown730Entries;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(entries.Count);
        foreach (var e in entries) { writer.Write(e.EntryId); writer.Write(e.SentinelOrValue); writer.Write(e.Flag); } return stream.ToArray();
    }

    private static byte[] EncodeCollectionBox(SelectCharacterResult result, int occurrence)
    {
        var boxes = result.CharacterEntry.Features.Collections.CollectionBoxes.OrderBy(value => value.BoxIndex).ToArray();
        if (occurrence < 0 || occurrence >= boxes.Length) throw Missing($"CollectionBoxes[{occurrence}]");
        var box = boxes[occurrence]; var protocol = box.Protocol ?? throw Missing($"CollectionBoxes[{occurrence}].Protocol");
        if (box.Slots.Count > byte.MaxValue) throw CountError("collection box", "u8");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(box.BoxIndex); writer.Write((byte)1); writer.Write(protocol.RemainingSeconds); writer.Write(protocol.StatusFlags);
        writer.Write(checked((byte)box.Slots.Count)); foreach (var slot in box.Slots.OrderBy(value => value.SlotIndex)) writer.Write(checked((uint)slot.ItemId));
        return stream.ToArray();
    }

    private static byte[] EncodeRentals(SelectCharacterResult result)
    {
        var items = result.CharacterInventory.Rentals.Items.Where(value => value.ExpireTime > result.World.PreparedAt.ToUnixTimeSeconds()).ToArray();
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((uint)result.Account.Wallet.LuckyStar); writer.Write(checked((uint)items.Length));
        foreach (var item in items) { writer.Write(item.InventoryTemplateId != 0 ? item.InventoryTemplateId : item.ItemId); writer.Write(item.ExpireTime); }
        return stream.ToArray();
    }

    private static byte[] EncodeEquipmentLocks(SelectCharacterResult result)
    {
        var locks = result.CharacterInventory.EquipmentLocks.Concat(result.AccountInventory.Cargo.EquipmentLocks).OrderBy(value => value.EquipmentLockId).ToArray();
        if (locks.Length > ushort.MaxValue) throw CountError("equipment lock", "u16");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(checked((ushort)locks.Length));
        foreach (var value in locks)
        {
            writer.Write((byte)value.ContainerType); writer.Write(checked((ushort)value.SlotIndex)); writer.Write(value.State);
            if (value.RemainingSeconds != 0) writer.Write(value.RemainingSeconds);
        }
        return stream.ToArray();
    }

    private static byte[] EncodeExpert(SelectCharacterResult result)
    {
        var expert = result.CharacterEntry.Features.ExpertJob;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)expert.GiveupCount)); writer.Write(expert.ProfessionType);
        if (expert.ProfessionType is 1 or 2 or 4)
        {
            if (expert.LearnedRecipeIds.Count > byte.MaxValue) throw CountError("expert recipe", "u8");
            writer.Write(checked((byte)expert.LearnedRecipeIds.Count)); foreach (var id in expert.LearnedRecipeIds) writer.Write(id);
            if (expert.ProfessionType == 1)
            {
                var protocol = expert.Protocol ?? throw Missing("ExpertJob.Protocol");
                if (protocol.CardQualificationLevels.Count > byte.MaxValue) throw CountError("card qualification", "u8");
                writer.Write(checked((byte)protocol.CardQualificationLevels.Count)); foreach (var level in protocol.CardQualificationLevels) writer.Write(level);
                writer.Write(protocol.EnchanterLevel); writer.Write(expert.EnchanterEndurance);
            }
        }
        else if (expert.ProfessionType == 3) { writer.Write(expert.DisjointMachineGrade); writer.Write(expert.DisjointMachineEndurance); }
        return stream.ToArray();
    }

    private static byte[] EncodeFatigueAcceleration(SelectCharacterResult result)
    {
        using var stream = new MemoryStream(3); using var writer = new BinaryWriter(stream);
        writer.Write(result.CharacterEntry.Progression.FatigueAccelerationValue); writer.Write(result.CharacterEntry.Progression.FatigueAccelerationState); return stream.ToArray();
    }

    private static byte[] EncodeChampionBreak(SelectCharacterResult result)
    {
        var state = result.CharacterEntry.Progression; using var stream = new MemoryStream(9); using var writer = new BinaryWriter(stream);
        writer.Write(state.ChampionBreakKey); writer.Write(state.ChampionBreakMode); writer.Write(state.ChampionBreakValue); return stream.ToArray();
    }

    private static byte[] EncodePvpMissions(SelectCharacterResult result)
    {
        var entries = result.CharacterEntry.Features.PvpMissions; if (entries.Count > byte.MaxValue) throw CountError("PvP mission", "u8");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(checked((byte)entries.Count));
        foreach (var entry in entries) { writer.Write(entry.MissionId); writer.Write(entry.Progress); } return stream.ToArray();
    }

    private static byte[] EncodeDailySchedule(SelectCharacterResult result, int occurrence)
    {
        var entries = result.CharacterEntry.Progression.ScheduleCompatibility.OrderBy(value => value.SlotIndex).ToArray();
        if (occurrence < 0 || occurrence >= entries.Length) throw Missing($"ScheduleCompatibility[{occurrence}]"); var e = entries[occurrence];
        using var stream = new MemoryStream(16); using var writer = new BinaryWriter(stream); writer.Write(e.Value0); writer.Write(e.Value1); writer.Write(e.Value2); writer.Write(e.Value3); return stream.ToArray();
    }

    private static byte[] EncodeCera(SelectCharacterResult result)
    {
        var wallet = result.Account.Wallet; using var stream = new MemoryStream(13); using var writer = new BinaryWriter(stream);
        writer.Write((byte)1); writer.Write(wallet.Cera); writer.Write(wallet.TokenCera); writer.Write(wallet.HappyTokenCera); return stream.ToArray();
    }

    private static byte[] EncodePosition(SelectCharacterResult result)
    {
        var spawn = result.World.Channel.Spawn; using var stream = new MemoryStream(11); using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)result.Character.Identity.CharacterId.Value)); writer.Write(checked((ushort)spawn.X)); writer.Write(checked((ushort)spawn.Y)); writer.Write(spawn.Direction); writer.Write((ushort)100); return stream.ToArray();
    }

    private static byte[] EncodeKnightShieldDeck(SelectCharacterResult result)
    {
        var deck = result.CharacterEntry.Skills.KnightShieldDeck ?? throw Missing("Skills.KnightShieldDeck");
        if (deck.Count != 5) throw new InvalidDataException("Knight shield deck must contain exactly five slots.");
        using var stream = new MemoryStream(20); using var writer = new BinaryWriter(stream); foreach (var item in deck) writer.Write(item); return stream.ToArray();
    }

    private static byte[] EncodeDarkKnightCombos(SelectCharacterResult result)
    {
        var combos = result.CharacterEntry.Skills.DarkKnightCombos ?? throw Missing("Skills.DarkKnightCombos");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var combo in combos.OrderBy(value => value.PageIndex)) writer.Write(combo.EncodedBody); return stream.ToArray();
    }

    private static byte[] EncodePetWelcome(SelectCharacterResult result)
    {
        var welcome = result.World.Content.PetWelcome ?? throw Missing("World.Content.PetWelcome");
        if (welcome.Lines.Count == 0) throw Missing("World.Content.PetWelcome.Lines");
        var index = (int)((uint)(result.Character.Identity.CharacterId.Value * 397) ^ (uint)welcome.CreatureId) % welcome.Lines.Count;
        var text = Encoding.UTF8.GetBytes(welcome.Lines[index]);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)3); writer.Write(checked((ushort)result.Character.Identity.CharacterId.Value)); writer.Write((byte)0); WriteDstr(writer, text); return stream.ToArray();
    }

    private static byte[] EncodeMailbox(SelectCharacterResult result)
    {
        var mailbox = result.CharacterEntry.Features.Mailbox;
        if (mailbox.Messages.Count > ushort.MaxValue || mailbox.NotLoadedCount is < 0 or > ushort.MaxValue)
            throw CountError("mailbox", "u16");
        var summaries = new List<(CharacterMailboxMessageEntryState Message, CharacterMailboxAttachmentEntryState? Attachment, bool IncludeGold, bool SeedOnly)>();
        foreach (var message in mailbox.Messages)
        {
            var wroteAttachment = false;
            foreach (var attachment in message.Attachments.Where(value => value.ItemTemplateId > 0 && value.ItemCount > 0))
            {
                summaries.Add((message, attachment, !wroteAttachment, false));
                wroteAttachment = true;
            }
            if (!wroteAttachment && message.Gold > 0)
            {
                summaries.Add((message, null, true, false));
                wroteAttachment = true;
            }
            if (!wroteAttachment && message.Body.Length > 0)
                summaries.Add((message, null, false, true));
        }
        if (summaries.Count > byte.MaxValue) throw CountError("mailbox summary", "u8");

        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)summaries.Count)); writer.Write((byte)0);
        foreach (var summary in summaries)
            WriteMailboxSummary(writer, summary.Message, summary.Attachment, summary.IncludeGold, summary.SeedOnly);
        writer.Write(checked((ushort)mailbox.NotLoadedCount));
        writer.Write(checked((ushort)mailbox.Messages.Count));
        foreach (var message in mailbox.Messages)
        {
            writer.Write(checked((int)message.MessageId)); writer.Write(MailboxSenderId(message)); WriteMailboxString(writer, MailboxSenderName(message), 30);
            var display = message.Title.Length == 0 || message.Title.SequenceEqual(message.Body)
                ? message.Body
                : [.. message.Title, (byte)'\n', .. message.Body];
            WriteMailboxString(writer, display, 512); writer.Write(Math.Max(0, message.CreatedAtUnixSeconds)); writer.Write((ushort)message.LetterState);
        }
        return stream.ToArray();
    }

    private static void WriteMailboxSummary(
        BinaryWriter writer,
        CharacterMailboxMessageEntryState message,
        CharacterMailboxAttachmentEntryState? attachment,
        bool includeGold,
        bool seedOnly)
    {
        var hasItem = attachment is { ItemTemplateId: > 0, ItemCount: > 0 };
        var core = hasItem ? DecodeMailboxCore(attachment!) : null;
        var messageId = checked((int)message.MessageId);
        writer.Write(hasItem ? checked(0x40000000 + (int)attachment!.AttachmentId) : messageId);
        writer.Write(MailboxSenderId(message));
        WriteMailboxString(writer, MailboxSenderName(message), 30);
        writer.Write(includeGold ? message.Gold : 0);
        writer.Write(hasItem ? attachment!.ItemTemplateId : 0);
        writer.Write(hasItem ? (byte)1 : (byte)0);

        var stackable = hasItem && string.Equals(attachment!.ItemKind, "stackable", StringComparison.OrdinalIgnoreCase);
        var creature = hasItem && string.Equals(attachment!.ItemKind, "creature", StringComparison.OrdinalIgnoreCase);
        var avatar = hasItem && string.Equals(attachment!.ItemKind, "avatar", StringComparison.OrdinalIgnoreCase);
        writer.Write(hasItem ? (stackable ? attachment!.ItemCount : core!.Value) : 0);
        writer.Write(hasItem ? core!.Durability : (ushort)0);
        writer.Write(hasItem ? core!.Attribute : (byte)0);
        writer.Write(hasItem ? core!.EnchantCardId : 0);
        writer.Write(hasItem ? core!.EnchantUpgradeCount : (byte)0);
        writer.Write(hasItem ? core!.AmplifyType : (byte)0);
        writer.Write(hasItem ? core!.AmplifyValue : (ushort)0);
        if (creature) writer.Write(core!.Marker16);
        writer.Write(hasItem ? core!.ExpireTime : 0);
        var emblems = hasItem
            ? new[] { core!.EmblemId1, core.EmblemId2 }.Take(Math.Min(2, (int)core.EmblemSocketCount)).ToArray()
            : [];
        writer.Write(checked((byte)emblems.Length)); foreach (var emblem in emblems) writer.Write(emblem);
        writer.Write(hasItem ? core!.Rune : (ushort)0);
        var random = hasItem
            ? new[] { core!.RandomOption0, core.RandomOption1, core.RandomOption2 }
                .Where(value => value.Type != 0).ToArray()
            : [];
        writer.Write(checked((byte)random.Length));
        foreach (var value in random) { writer.Write(value.Type); writer.Write(value.Value1); writer.Write(value.Value2); }
        if (random.Length > 0)
        {
            writer.Write(core!.RandomOptionState); writer.Write(core.RandomOptionChangedIndex);
            if (core.RandomOptionChangedIndex != 0xFF)
            {
                writer.Write(core.RandomOptionChangeState); writer.Write(core.RandomOptionChange.Type);
                writer.Write(core.RandomOptionChange.Value1); writer.Write(core.RandomOptionChange.Value2);
            }
        }
        writer.Write(hasItem ? core!.GenuineUpgrade : (byte)0);
        writer.Write(hasItem ? core!.EmancipateEquipmentLevel : (byte)0);
        writer.Write(hasItem ? core!.TradeRestriction : (byte)0);
        writer.Write(hasItem ? core!.TailUnknown0 : (ushort)0);
        writer.Write(hasItem ? core!.TailUnknown1 : (byte)0);
        writer.Write(hasItem ? core!.TailUnknown2 : (byte)0);
        writer.Write(hasItem ? core!.TailUnknown3 : (byte)0);
        writer.Write(hasItem ? core!.RemainUseCount : (byte)0);
        writer.Write(hasItem ? core!.SortLockFlag : (byte)0);
        if (avatar) WriteMailboxAvatarDetail(writer, attachment!);
        writer.Write(Math.Max(0, message.RemainingSeconds));
        writer.Write(seedOnly ? 0 : messageId);
    }

    private static InventoryItemCoreState DecodeMailboxCore(CharacterMailboxAttachmentEntryState attachment)
    {
        var core = attachment.ItemCore;
        if (core.Length != 82) throw Missing($"Mailbox.Attachment[{attachment.AttachmentId}].ItemCore");
        return new InventoryItemCoreState(
            core[0], System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1)), System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5)),
            core[9], System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(core.AsSpan(10)), core[12], System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(13)), core[17], core[18],
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(core.AsSpan(19)), System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(21)),
            DecodeMailboxChronicle(core.AsSpan(25)), DecodeMailboxChronicle(core.AsSpan(33)), System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(41)),
            core[45], System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(46)), System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(50)),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(core.AsSpan(54)), DecodeMailboxRandom(core.AsSpan(56)), DecodeMailboxRandom(core.AsSpan(59)), DecodeMailboxRandom(core.AsSpan(62)),
            core[65], core[66], core[67], DecodeMailboxRandom(core.AsSpan(68)), core[71], core[72], core[73],
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(core.AsSpan(74)), core[76], core[77], core[78], core[79], core[80], core[81]);
    }

    private static ChronicleOptionState DecodeMailboxChronicle(ReadOnlySpan<byte> value) => new(
        System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(value), value[4], value[5], value[6], value[7]);
    private static RandomOptionState DecodeMailboxRandom(ReadOnlySpan<byte> value) => new(value[0], value[1], value[2]);

    private static void WriteMailboxAvatarDetail(BinaryWriter writer, CharacterMailboxAttachmentEntryState attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.DetailJson)) throw Missing($"Mailbox.Attachment[{attachment.AttachmentId}].DetailJson");
        try
        {
            using var document = JsonDocument.Parse(attachment.DetailJson);
            var avatar = document.RootElement.GetProperty("Avatar");
            var socket = avatar.GetProperty("JewelSocket").GetBytesFromBase64();
            if (socket.Length != 30) throw new InvalidDataException("Mailbox avatar jewel socket must contain exactly 30 bytes.");
            writer.Write(30); writer.Write(socket); writer.Write(4);
            writer.Write(avatar.GetProperty("Color1").GetUInt16()); writer.Write(avatar.GetProperty("Color2").GetUInt16());
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException($"Mailbox avatar detail {attachment.AttachmentId} is malformed.", error);
        }
    }

    private static int MailboxSenderId(CharacterMailboxMessageEntryState message) => message.MailType != 0 ? 0 : message.SenderCharacterId;
    private static byte[] MailboxSenderName(CharacterMailboxMessageEntryState message) => message.MailType != 0 ? Encoding.UTF8.GetBytes("DNFadmin") : message.SenderName;
    private static void WriteMailboxString(BinaryWriter writer, byte[] value, int maximumBytes)
    {
        var bytes = value.Length < maximumBytes ? value : value[..(maximumBytes - 1)];
        WriteDstr(writer, bytes);
    }

    private static byte[] EncodeUserInfo(SelectCharacterResult result, int occurrence) => occurrence switch
    {
        0 or 2 => EncodeUserInfoSubtype0(result),
        1 => EncodeUserInfoSubtype1(result),
        _ => throw new ArgumentOutOfRangeException(nameof(occurrence)),
    };

    private static byte[] EncodeUserInfoSubtype0(SelectCharacterResult result)
    {
        var character = result.Character; var entry = result.CharacterEntry.Public;
        var identity = entry.Identity ?? throw Missing("CharacterEntry.Public.Identity");
        var t = entry.Subtype0; var s1 = entry.Subtype1;
        var equipment = result.CharacterInventory.Containers.SingleOrDefault(c => c.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
        var appearance = equipment.Where(item => item.SlotIndex <= 12 || item.SlotIndex == 23).OrderBy(item => item.SlotIndex).ToArray();
        if (appearance.Length > byte.MaxValue) throw CountError("appearance", "u8");
        var creature = equipment.SingleOrDefault(item => item.SlotIndex == 24 && item.Core.ItemKind == 5);
        var pet = creature is null ? null : result.CharacterEntry.Features.Pets.SingleOrDefault(value => value.PetKey == creature.Core.Value);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)0); writer.Write((ushort)1); writer.Write(checked((ushort)character.Identity.CharacterId.Value)); WriteDstr(writer, character.Identity.Name);
        writer.Write(character.Identity.Job); writer.Write(character.Identity.GrowType); writer.Write(character.Progression.Level);
        writer.Write(identity.PvpGrade); writer.Write(identity.PvpRatingGrade); writer.Write(identity.UserState); writer.Write(checked((byte)appearance.Length));
        foreach (var item in appearance)
        {
            writer.Write(checked((byte)item.SlotIndex)); writer.Write(item.Core.ItemId); writer.Write(4); writer.Write(0);
            writer.Write(checked((byte)((item.Core.Attribute & 0x1F) * 2 + (item.Core.AmplifyType != 0 ? 1 : 0))));
            writer.Write(0); writer.Write(0u); writer.Write(item.Core.EnchantUpgradeCount);
        }
        writer.Write(entry.CloneTitleItemId); writer.Write((byte)0); writer.Write(t.CreatureField2); writer.Write(t.CreatureField3); writer.Write(t.CreatureField4);
        writer.Write(s1.NameTagItemId); writer.Write(s1.NameTagExpireTime); writer.Write(t.Stamina); writer.Write(t.FatiguePenalty); writer.Write(t.EventCharacter ? (byte)1 : (byte)0);
        writer.Write(creature is null ? 0u : checked((uint)creature.Core.ItemId)); WriteDstr(writer, pet?.CreatureText ?? []); writer.Write(pet?.Field04 > 0 ? (byte)1 : (byte)0);
        writer.Write(t.PremiumPcRoom ? (byte)1 : (byte)0); writer.Write(t.ServerGroupId); writer.Write(t.BlackCount); writer.Write(t.GuildLevel); WriteDstr(writer, []);
        writer.Write(t.ChaosPoint); writer.Write((byte)1); writer.Write(t.DisguiseKind); writer.Write(t.Disguised ? (byte)1 : (byte)0); writer.Write(t.ExpertJobType); writer.Write(t.ExpertJobExperience);
        writer.Write((byte)0); writer.Write(0u); writer.Write((ushort)0); writer.Write(t.HardcoreMode ? (byte)1 : (byte)0); writer.Write(t.HardcoreDead ? (byte)1 : (byte)0);
        writer.Write(t.HardcoreDeathCount); writer.Write(checked((uint)s1.Progress1)); writer.Write(checked((uint)s1.Progress2)); writer.Write(checked((byte)t.UserStateBits)); writer.Write(t.ChatBanEndTime);
        writer.Write((byte)100); writer.Write(t.FatigueUpdate); writer.Write(t.ReturnUserFlag); writer.Write(t.ChannelDisplayMode); writer.Write(t.ChannelType); writer.Write(t.MoodValue);
        writer.Write(checked((byte)s1.SkillTreeIndex)); writer.Write(t.ReturnUser ? (byte)1 : (byte)0); writer.Write(t.LinkSlotEnabled ? (byte)1 : (byte)0); writer.Write(t.LinkTypeA); writer.Write(t.LinkTypeB);
        writer.Write(t.EmotionIndex); writer.Write(t.ActionByte); writer.Write(t.FatigueDisplayUpdate); writer.Write(t.CostumeFlag); writer.Write(t.AuraFlag); writer.Write(t.PetDisplayFlag); writer.Write(t.TitleDisplayFlag);
        writer.Write(t.PvpStatA); writer.Write(t.PvpWinStreak); writer.Write(t.PvpLoseStreak); writer.Write(t.PvpRankPoint); writer.Write(t.TrailingByte); return stream.ToArray();
    }

    internal static byte[] EncodePvpSubtype0(SelectCharacterResult result) => EncodeUserInfoSubtype0(result);

    private static byte[] EncodeUserInfoSubtype1(SelectCharacterResult result)
    {
        var character = result.Character; var value = result.CharacterEntry.Public.Subtype1; var skills = result.CharacterEntry.Skills;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)1); writer.Write((ushort)1); writer.Write(checked((ushort)character.Identity.CharacterId.Value)); writer.Write(value.CharacterExperience);
        writer.Write(83); writer.Write(checked((uint)value.HpMaximum)); writer.Write(checked((uint)value.MpMaximum)); writer.Write(checked((short)value.PhysicalAttack)); writer.Write(checked((short)value.PhysicalDefense));
        writer.Write(checked((short)value.MagicalAttack)); writer.Write(checked((short)value.MagicalDefense)); writer.Write(value.FireResistance); writer.Write(value.WaterResistance); writer.Write(value.DarkResistance); writer.Write(value.LightResistance);
        writer.Write(new byte[34]); writer.Write(checked((uint)value.InventoryLimit)); writer.Write(value.HpRegenSpeed); writer.Write(value.MpRegenSpeed); writer.Write(checked((uint)value.MoveSpeed));
        writer.Write(checked((ushort)value.AttackSpeed)); writer.Write(checked((ushort)value.CastSpeed)); writer.Write(value.HitRecovery); writer.Write(value.JumpPower); writer.Write(value.Weight); writer.Write(value.StatLevel); writer.Write(value.ExtraEquipmentSlotState);
        var equipment = result.CharacterInventory.Containers.SingleOrDefault(c => c.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
        if (equipment.Count > byte.MaxValue) throw CountError("equipped item", "u8"); writer.Write(checked((byte)equipment.Count));
        foreach (var item in equipment.OrderBy(item => item.SlotIndex)) WriteNoti2Item(writer, item, result.World.PreparedAt);
        writer.Write(result.CharacterEntry.Public.CloneTitleItemId); writer.Write(value.NameTagItemId); writer.Write(value.NameTagExpireTime); writer.Write(checked((byte)value.SkillTreeIndex));
        for (byte page = 0; page < 2; page++)
        {
            var learned = skills.Pages.SingleOrDefault(p => p.PageIndex == page)?.Skills.Where(s => s.Level > 0).ToArray() ?? [];
            if (learned.Length > byte.MaxValue) throw CountError("subtype1 skill", "u8"); writer.Write(checked((byte)learned.Length));
            foreach (var skill in learned) { writer.Write(checked((ushort)skill.SkillId)); writer.Write(skill.Level); }
        }
        writer.Write(value.EquippedCreatureLevel); writer.Write(checked((byte)value.Dimensions.Count)); foreach (var d in value.Dimensions) { writer.Write(d.Key); writer.Write(d.Value1); writer.Write(d.Value2); }
        writer.Write(value.DimensionFlags.Flag1); writer.Write(value.DimensionFlags.Flag2); writer.Write(value.DimensionFlags.Flag3); writer.Write(value.DimensionFlags.Flag4);
        var pvpResults = value.PvpResults ?? throw Missing("CharacterEntry.Public.Subtype1.PvpResults");
        var specialRewards = value.SpecialRewardQuestIds ?? throw Missing("CharacterEntry.Public.Subtype1.SpecialRewardQuestIds");
        if (pvpResults.Count > byte.MaxValue) throw CountError("subtype1 PvP result", "u8");
        writer.Write(checked((byte)pvpResults.Count));
        foreach (var pvpResult in pvpResults)
        {
            writer.Write(pvpResult.Value32); writer.Write(pvpResult.Value16A); writer.Write(pvpResult.Value16B);
        }
        writer.Write(value.ManageLevel); writer.Write(checked((uint)specialRewards.Count));
        foreach (var questId in specialRewards) writer.Write(questId);
        writer.Write(value.FlagByte); writer.Write(value.GuildPowerWar); writer.Write(value.ServerTimestamp); writer.Write(value.QuestShopCount);
        writer.Write(checked((uint)value.Progress1)); writer.Write(checked((uint)value.Progress2)); return stream.ToArray();
    }

    private static void WriteNoti2Item(BinaryWriter writer, InventoryItemState item, DateTimeOffset preparedAt)
    {
        var core = item.Core; var avatar = item.AvatarDetail;
        if (core.ItemKind == 8 && avatar is null) throw Missing($"AvatarDetail[{item.StorageEntryId}]");
        writer.Write(checked((byte)item.SlotIndex)); writer.Write(core.ItemId); writer.Write(checked((uint)(core.ItemKind == 8 ? Math.Max(0, avatar!.ExpireDate - preparedAt.ToUnixTimeSeconds()) : core.Value)));
        writer.Write(core.Attribute); writer.Write(core.Durability); writer.Write(core.ItemKind == 8 && avatar!.ClearAvatarId != 0 ? checked((uint)avatar.ClearAvatarId) : core.SealFlag);
        writer.Write(core.EnchantCardId); writer.Write(core.EnchantUpgradeCount); writer.Write(core.AmplifyType); writer.Write(core.AmplifyValue);
        if (item.SlotIndex <= 10) { writer.Write(core.ItemKind == 8 ? 30 : 0); if (core.ItemKind == 8) writer.Write(avatar!.JewelSocket); writer.Write(4); writer.Write(avatar?.Color1 ?? 0); writer.Write(avatar?.Color2 ?? 0); }
        if (item.SlotIndex == 24) writer.Write(checked((uint)Math.Max(0, core.Marker16)));
        writer.Write((byte)2); WriteChronicleDynamic(writer, core.ChronicleOption0); WriteChronicleDynamic(writer, core.ChronicleOption1); writer.Write(core.ExpireTime);
        writer.Write(core.EmblemSocketCount); if (core.EmblemSocketCount > 0) writer.Write(core.EmblemId1); if (core.EmblemSocketCount > 1) writer.Write(core.EmblemId2); writer.Write(core.Rune);
        writer.Write((byte)3); WriteRandomDynamic(writer, core.RandomOption0); WriteRandomDynamic(writer, core.RandomOption1); WriteRandomDynamic(writer, core.RandomOption2);
        writer.Write(core.RandomOptionState); writer.Write(core.RandomOptionChangedIndex); writer.Write(core.RandomOptionChangeState); WriteRandomDynamic(writer, core.RandomOptionChange);
        writer.Write(core.GenuineUpgrade); writer.Write(core.EmancipateEquipmentLevel); writer.Write(core.TradeRestriction); writer.Write(core.TailUnknown0); writer.Write(core.TailUnknown1); writer.Write(core.TailUnknown2); writer.Write(core.TailUnknown3); writer.Write(core.RemainUseCount); writer.Write((byte)0);
    }

    private static void WriteChronicleDynamic(BinaryWriter writer, ChronicleOptionState value) { writer.Write(value.Id); writer.Write(value.CharacterJob); writer.Write(value.FirstGrowType); writer.Write(value.EquipmentType); writer.Write(value.OptionNumber); }
    private static void WriteRandomDynamic(BinaryWriter writer, RandomOptionState value) { writer.Write(value.Type); writer.Write(value.Value1); writer.Write(value.Value2); }
    private static void WriteBlob(BinaryWriter writer, byte[] value) { writer.Write(value.Length); writer.Write(value); }
    private static void WriteDstr(BinaryWriter writer, byte[] value) { writer.Write(value.Length); writer.Write(value); }
    private static byte[] U32(uint value) { var body = new byte[4]; WriteInt32(body, 0, checked((int)value)); return body; }
    private static void WriteInt32(byte[] target, int offset, int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value);
    private static InvalidDataException CountError(string name, string width) => new($"{name} count cannot be represented as {width}.");
}
