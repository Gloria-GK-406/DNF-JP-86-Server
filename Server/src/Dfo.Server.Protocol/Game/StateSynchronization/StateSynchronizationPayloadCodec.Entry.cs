using System.Text;
using System.Text.Json;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal static partial class StateSynchronizationPayloadCodec
{
    public static byte[] EncodePremiumService(
        AccountEntitlementEntryState entitlements,
        IReadOnlyList<CharacterPremiumUsageSlotState> usageSlots)
    {
        var data = new byte[74];
        foreach (var premium in entitlements.Premiums)
        {
            var slot = premium.PremiumType - 580;
            if (slot is < 0 or >= 8) continue;
            WriteInt32(data, 6 + slot * 9,
                checked((int)Math.Min(premium.ExpiresAtUnixSeconds, int.MaxValue)));
        }
        foreach (var usage in usageSlots)
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

    public static byte[] EncodeCreatures(IReadOnlyList<CharacterPetRuntimeState> pets)
    {
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

    public static byte[] EncodeLotteryProgress(AccountProgressionEntryState progression)
    {
        const int bodyLength = 204;
        const int recordCount = 8;
        const int recordLength = 24;
        const int claimCapacity = 20;
        var records = progression.LotteryProgress
            ?? throw Missing("Progression.LotteryProgress");
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

    public static byte[] EncodeSupportCharacter(
        CharacterId ownerCharacterId,
        IReadOnlyList<AccountCharacterRelationEntryState> characterRelations,
        DateTimeOffset preparedAt)
    {
        if (characterRelations.Any(value =>
                value.SupportProjection is not null && value.OwnerCharacterId != ownerCharacterId))
            throw new InvalidDataException("Support-character projection belongs to a different selected owner.");
        var relations = characterRelations
            .Where(value => value.OwnerCharacterId == ownerCharacterId).ToArray();
        if (relations.Length == 0)
            return new byte[2];
        if (relations.Length != 1 || relations[0].Slot != 0)
            throw new InvalidDataException("Support-character projection requires one owner relation in slot zero.");
        var relation = relations[0];
        var projection = relation.SupportProjection ?? throw Missing("Account.Relations.SupportProjection");
        if (relation.OwnerCharacterId != ownerCharacterId
            || projection.Character.Identity.CharacterId != relation.SupportCharacterId
            || projection.Entry.CharacterId != relation.SupportCharacterId
            || projection.Inventory.CharacterId != relation.SupportCharacterId)
            throw new InvalidDataException("Support-character projection does not match the owner relation.");
        var stats = projection.Entry.Public.DerivedStats;
        var extras = projection.Entry.Public.Extras;
        var equipment = projection.Inventory.Containers
            .Where(value => value.ContainerType == InventoryContainerType.Equipment)
            .SelectMany(value => value.Items).Where(value => value.SlotIndex is >= 0 and <= 29)
            .OrderBy(value => value.SlotIndex).ToArray();
        var page = projection.Entry.Skills.Pages.SingleOrDefault(
            value => value.PageIndex == extras.SkillTreeIndex);
        var skills = page?.Skills.OrderBy(value => value.Slot).ToArray() ?? [];
        if (equipment.Length > byte.MaxValue || skills.Length is 0 or > byte.MaxValue)
            throw new InvalidDataException("Support-character equipment or active skill page is invalid.");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)ownerCharacterId.Value));
        WriteDstr(writer, projection.Character.Identity.Name);
        writer.Write(projection.Character.Progression.Level);
        writer.Write(projection.Character.Identity.Job);
        writer.Write(projection.Character.Identity.GrowType);
        writer.Write(checked((ushort)relation.SkillId));
        writer.Write(82u);
        writer.Write(EncodeSupportStats(projection.Entry.Public));
        writer.Write(checked((byte)equipment.Length));
        foreach (var item in equipment)
            WriteNoti2EquippedItem(writer, item, preparedAt);
        writer.Write(projection.Entry.Public.CloneTitleItemId);
        writer.Write(checked((byte)skills.Length));
        writer.Write(SkillTreeIndexOnTheWire(extras.SkillTreeIndex));
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

    private static byte[] EncodeSupportStats(CharacterPublicEntryState entry)
    {
        var stats = entry.DerivedStats; var extras = entry.Extras;
        using var stream = new MemoryStream(82);
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((uint)stats.HpMaximum));
        writer.Write(checked((uint)stats.MpMaximum));
        writer.Write(checked((short)stats.PhysicalAttack));
        writer.Write(checked((short)stats.PhysicalDefense));
        writer.Write(checked((short)stats.MagicalAttack));
        writer.Write(checked((short)stats.MagicalDefense));
        writer.Write(checked((short)stats.FireResistance));
        writer.Write(checked((short)stats.WaterResistance));
        writer.Write(checked((short)stats.DarkResistance));
        writer.Write(checked((short)stats.LightResistance));
        writer.Write(new byte[34]);
        writer.Write(checked((uint)stats.InventoryLimit));
        writer.Write(checked((ushort)stats.HpRegenSpeed));
        writer.Write(checked((ushort)stats.MpRegenSpeed));
        writer.Write(checked((uint)stats.MoveSpeed));
        writer.Write(checked((ushort)stats.AttackSpeed));
        writer.Write(checked((ushort)stats.CastSpeed));
        writer.Write(checked((ushort)stats.HitRecovery));
        writer.Write(checked((ushort)stats.JumpPower));
        writer.Write(checked((uint)stats.Weight));
        return stream.ToArray();
    }

    public static byte[] EncodeSkills(CharacterSkillsEntryState skills)
    {
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

    public static byte[] EncodeAvailableQuests(
        byte characterLevel,
        AvailableQuestEntryState availableQuests)
    {
        var quests = availableQuests.Quests;
        if (quests.Count > ushort.MaxValue) throw CountError("available quest", "u16");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(characterLevel); writer.Write(checked((ushort)quests.Count));
        foreach (var quest in quests) writer.Write(checked((ushort)quest.QuestId)); return stream.ToArray();
    }

    public static byte[] EncodeQuestVisibility(byte[] flags)
    {
        if (flags.Length != 30_000) throw new InvalidDataException("Quest visibility bitmap must contain exactly 30000 bytes.");
        using var stream = new MemoryStream(30_004); using var writer = new BinaryWriter(stream);
        writer.Write(flags.Length); writer.Write(flags); return stream.ToArray();
    }

    public static byte[] EncodeDailyChallenge(CharacterProgressionEntryState state)
    {
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

    public static byte[] EncodeGameOptions(AccountSettingsEntryState settings)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        WriteBlob(writer, settings.MainGameOptions); WriteBlob(writer, settings.QuickChatBank0);
        WriteBlob(writer, settings.QuickChatBank1); return stream.ToArray();
    }

    public static byte[] EncodeHotkeys(
        AccountSettingsEntryState accountSettings,
        CharacterSettingsEntryState characterSettings)
    {
        var slots = characterSettings.HotkeySlots;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(accountSettings.HotkeyKeyType); writer.Write(checked(slots.Count * 2));
        foreach (var slot in slots) writer.Write(slot); return stream.ToArray();
    }

    /// <summary>The client's account-scoped hotkey slot when the account has none stored.</summary>
    /// <remarks>
    /// The first slot of the legacy default hotkey table. An account that has never saved its
    /// hotkeys still has to be told what they are, and a slot count of zero is not the same
    /// statement -- the legacy server substituted its default table here rather than sending an
    /// empty list, so a fresh account receives the same bytes it would from a working server.
    /// </remarks>
    private const ushort DefaultAccountHotkeySlot = 0x0002;

    /// <summary>The same 0x01C7 layout, carrying only the account-scoped slot.</summary>
    /// <remarks>
    /// Sent at login, where no character is selected and the per-character slots therefore do not
    /// exist yet. Exactly one slot is account scoped, which is why
    /// <see cref="AccountSettingsEntryState.AccountHotkeySlot"/> is a single value rather than a
    /// list -- the legacy server took the same one slot off the front of its hotkey array.
    ///
    /// A null slot means "never stored", a distinction the domain deliberately keeps. Choosing what
    /// to put on the wire in that case is a wire concern and is therefore decided here, which is
    /// also where the legacy server decided it.
    /// </remarks>
    public static byte[] EncodeAccountHotkeys(AccountSettingsEntryState accountSettings)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(accountSettings.HotkeyKeyType);
        writer.Write(sizeof(ushort));
        writer.Write(accountSettings.AccountHotkeySlot ?? DefaultAccountHotkeySlot);
        return stream.ToArray();
    }

    public static byte[] EncodeDungeonPermissions(IReadOnlyList<CharacterDungeonProgressState> entries)
    {
        if (entries.Count > ushort.MaxValue) throw CountError("dungeon", "u16");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)entries.Count)); foreach (var entry in entries) { writer.Write(checked((ushort)entry.DungeonId)); writer.Write(entry.State); }
        return stream.ToArray();
    }

    public static byte[] EncodeCharacterOption(byte[]? option)
    {
        if (option is not null) return [.. option];
        return new byte[4];
    }

    public static byte[] EncodeGrowthWeapon(IReadOnlyList<byte> values)
    {
        if (values.Count > byte.MaxValue) throw CountError("growth weapon", "u8");
        return [checked((byte)values.Count), .. values];
    }

    public static byte[] EncodeTitleBook(CharacterTitleBookCategoryState category)
    {
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

    public static byte[] EncodeAchievements(IReadOnlyList<CharacterAchievementProgressState> entries)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(entries.Count);
        foreach (var e in entries) { writer.Write(e.AchievementId); writer.Write(e.P1); writer.Write(e.P2); writer.Write(e.P3); writer.Write(e.P4); }
        return stream.ToArray();
    }

    public static byte[] EncodeSkillPointSlots(IReadOnlyList<CharacterSkillPointSlotState> entries)
    {
        if (entries.Count > byte.MaxValue) throw CountError("skill point slot", "u8");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(checked((byte)entries.Count));
        foreach (var entry in entries) { writer.Write(entry.SlotIndex); writer.Write(checked((ushort)entry.Value)); } return stream.ToArray();
    }

    public static byte[] EncodeItemValues(IReadOnlyList<InventoryItemValueState> entries)
    {
        if (entries.Count > byte.MaxValue) throw CountError("item value", "u8");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(checked((byte)entries.Count));
        foreach (var entry in entries.OrderBy(value => value.SortOrder)) { writer.Write(entry.ItemId); writer.Write(entry.Value); } return stream.ToArray();
    }

    public static byte[] EncodeCompatibilityEntries(IReadOnlyList<AccountCompatibilityEntryState> entries)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(entries.Count);
        foreach (var e in entries) { writer.Write(e.EntryId); writer.Write(e.SentinelOrValue); writer.Write(e.Flag); } return stream.ToArray();
    }

    public static byte[] EncodeCollectionBox(CharacterCollectionBoxProgressState box)
    {
        var protocol = box.Protocol ?? throw Missing($"CollectionBoxes[{box.BoxIndex}].Protocol");
        if (box.Slots.Count > byte.MaxValue) throw CountError("collection box", "u8");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(box.BoxIndex); writer.Write((byte)1); writer.Write(protocol.RemainingSeconds); writer.Write(protocol.StatusFlags);
        writer.Write(checked((byte)box.Slots.Count)); foreach (var slot in box.Slots.OrderBy(value => value.SlotIndex)) writer.Write(checked((uint)slot.ItemId));
        return stream.ToArray();
    }

    public static byte[] EncodeRentals(
        CharacterRentalEntryState rentals,
        ushort luckyStar,
        DateTimeOffset preparedAt)
    {
        var items = rentals.Items.Where(value => value.ExpireTime > preparedAt.ToUnixTimeSeconds()).ToArray();
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((uint)luckyStar); writer.Write(checked((uint)items.Length));
        foreach (var item in items) { writer.Write(item.InventoryTemplateId != 0 ? item.InventoryTemplateId : item.ItemId); writer.Write(item.ExpireTime); }
        return stream.ToArray();
    }

    public static byte[] EncodeEquipmentLocks(
        IReadOnlyList<InventoryEquipmentLockState> characterLocks,
        IReadOnlyList<InventoryEquipmentLockState> accountLocks)
    {
        var locks = characterLocks.Concat(accountLocks).OrderBy(value => value.EquipmentLockId).ToArray();
        if (locks.Length > ushort.MaxValue) throw CountError("equipment lock", "u16");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(checked((ushort)locks.Length));
        foreach (var value in locks)
        {
            writer.Write((byte)value.ContainerType); writer.Write(checked((ushort)value.SlotIndex)); writer.Write(value.State);
            if (value.RemainingSeconds != 0) writer.Write(value.RemainingSeconds);
        }
        return stream.ToArray();
    }

    public static byte[] EncodeExpert(CharacterExpertJobEntryState expert)
    {
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

    public static byte[] EncodeFatigueAcceleration(ushort value, byte state)
    {
        using var stream = new MemoryStream(3); using var writer = new BinaryWriter(stream);
        writer.Write(value); writer.Write(state); return stream.ToArray();
    }

    public static byte[] EncodeChampionBreak(CharacterProgressionEntryState state)
    {
        using var stream = new MemoryStream(9); using var writer = new BinaryWriter(stream);
        writer.Write(state.ChampionBreakKey); writer.Write(state.ChampionBreakMode); writer.Write(state.ChampionBreakValue); return stream.ToArray();
    }

    public static byte[] EncodePvpMissions(IReadOnlyList<CharacterPvpMissionEntryState> entries)
    {
        if (entries.Count > byte.MaxValue) throw CountError("PvP mission", "u8");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(checked((byte)entries.Count));
        foreach (var entry in entries) { writer.Write(entry.MissionId); writer.Write(entry.Progress); } return stream.ToArray();
    }

    public static byte[] EncodeDailySchedule(CharacterScheduleCompatibilityState e)
    {
        using var stream = new MemoryStream(16); using var writer = new BinaryWriter(stream); writer.Write(e.Value0); writer.Write(e.Value1); writer.Write(e.Value2); writer.Write(e.Value3); return stream.ToArray();
    }

    public static byte[] EncodeCera(AccountWalletEntryState wallet)
    {
        using var stream = new MemoryStream(13); using var writer = new BinaryWriter(stream);
        writer.Write((byte)1); writer.Write(wallet.Cera); writer.Write(wallet.TokenCera); writer.Write(wallet.HappyTokenCera); return stream.ToArray();
    }

    public static byte[] EncodePosition(CharacterId characterId, EntrySpawnState spawn)
    {
        using var stream = new MemoryStream(11); using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)characterId.Value)); writer.Write(checked((ushort)spawn.X)); writer.Write(checked((ushort)spawn.Y)); writer.Write(spawn.Direction); writer.Write((ushort)100); return stream.ToArray();
    }

    public static byte[] EncodeKnightShieldDeck(IReadOnlyList<int> deck)
    {
        if (deck.Count != 5) throw new InvalidDataException("Knight shield deck must contain exactly five slots.");
        using var stream = new MemoryStream(20); using var writer = new BinaryWriter(stream); foreach (var item in deck) writer.Write(item); return stream.ToArray();
    }

    public static byte[] EncodeDarkKnightCombos(IReadOnlyList<DarkKnightComboEntryState> combos)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var combo in combos.OrderBy(value => value.PageIndex)) writer.Write(combo.EncodedBody); return stream.ToArray();
    }

    public static byte[] EncodePetWelcome(CharacterId characterId, PetWelcomeEntryState welcome)
    {
        if (welcome.Lines.Count == 0) throw Missing("World.Content.PetWelcome.Lines");
        var index = (int)((uint)(characterId.Value * 397) ^ (uint)welcome.CreatureId) % welcome.Lines.Count;
        var text = Encoding.UTF8.GetBytes(welcome.Lines[index]);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)3); writer.Write(checked((ushort)characterId.Value)); writer.Write((byte)0); WriteDstr(writer, text); return stream.ToArray();
    }

    public static byte[] EncodeMailbox(CharacterMailboxEntryState mailbox)
    {
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

    public static byte[] EncodeCurrentUserState(
        ActiveCharacterState character,
        CharacterPublicEntryState entry,
        IReadOnlyList<InventoryItemState> equipment,
        IReadOnlyList<CharacterPetRuntimeState> pets)
    {
        var identity = entry.Identity ?? throw Missing("CharacterEntry.Public.Identity");
        var c = entry.Condition; var pvp = entry.Pvp; var job = entry.ExpertJob;
        var guild = entry.Guild; var settings = entry.Settings;
        var stats = entry.DerivedStats; var extras = entry.Extras;
        var appearance = equipment.Where(item => item.SlotIndex <= 12 || item.SlotIndex == 23).OrderBy(item => item.SlotIndex).ToArray();
        if (appearance.Length > byte.MaxValue) throw CountError("appearance", "u8");
        var creature = equipment.SingleOrDefault(item => item.SlotIndex == 24 && item.Core.ItemKind == 5);
        var pet = creature is null ? null : pets.SingleOrDefault(value => value.PetKey == creature.Core.Value);
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
        writer.Write(entry.CloneTitleItemId); writer.Write((byte)0); writer.Write(checked((byte)c.CreatureField2)); writer.Write(checked((byte)c.CreatureField3)); writer.Write(checked((byte)c.CreatureField4));
        writer.Write(checked((uint)extras.NameTagItemId)); writer.Write(checked((uint)extras.NameTagExpireTime)); writer.Write(checked((byte)c.Stamina)); writer.Write(checked((uint)c.FatiguePenalty)); writer.Write(c.EventCharacter != 0 ? (byte)1 : (byte)0);
        writer.Write(creature is null ? 0u : checked((uint)creature.Core.ItemId)); WriteDstr(writer, pet?.CreatureText ?? []); writer.Write(pet?.Field04 > 0 ? (byte)1 : (byte)0);
        writer.Write(c.PremiumPcRoom != 0 ? (byte)1 : (byte)0); writer.Write(checked((byte)c.ServerGroupId)); writer.Write(checked((uint)c.BlackCount)); writer.Write(checked((byte)guild.Level)); WriteDstr(writer, []);
        writer.Write(checked((uint)c.ChaosPoint)); writer.Write((byte)1); writer.Write(checked((byte)c.DisguiseKind)); writer.Write(c.Disguised != 0 ? (byte)1 : (byte)0); writer.Write(checked((byte)job.Type)); writer.Write(checked((uint)job.Experience));
        writer.Write((byte)0); writer.Write(0u); writer.Write((ushort)0); writer.Write(c.HardcoreMode != 0 ? (byte)1 : (byte)0); writer.Write(c.HardcoreDead != 0 ? (byte)1 : (byte)0);
        writer.Write(checked((ushort)c.HardcoreDeathCount)); writer.Write(checked((uint)stats.Progress1)); writer.Write(checked((uint)stats.Progress2)); writer.Write(checked((byte)checked((uint)settings.UserStateBits))); writer.Write(checked((uint)c.ChatBanEndTime));
        writer.Write((byte)100); writer.Write(checked((ushort)c.FatigueUpdate)); writer.Write(checked((byte)c.ReturnUserFlag)); writer.Write(checked((ushort)c.ChannelDisplayMode)); writer.Write(checked((byte)c.ChannelType)); writer.Write(checked((ushort)c.MoodValue));
        writer.Write(SkillTreeIndexOnTheWire(extras.SkillTreeIndex)); writer.Write(c.ReturnUser != 0 ? (byte)1 : (byte)0); writer.Write(c.LinkSlotEnabled != 0 ? (byte)1 : (byte)0); writer.Write(checked((byte)c.LinkTypeA)); writer.Write(checked((byte)c.LinkTypeB));
        writer.Write(checked((ushort)c.EmotionIndex)); writer.Write(checked((byte)c.ActionByte)); writer.Write(checked((ushort)c.FatigueDisplayUpdate)); writer.Write(checked((byte)c.CostumeFlag)); writer.Write(checked((byte)c.AuraFlag)); writer.Write(checked((byte)c.PetDisplayFlag)); writer.Write(checked((byte)c.TitleDisplayFlag));
        writer.Write(checked((uint)pvp.StatA)); writer.Write(checked((byte)pvp.WinStreak)); writer.Write(checked((byte)pvp.LoseStreak)); writer.Write(checked((uint)pvp.RankPoint)); writer.Write(checked((byte)c.TrailingByte)); return stream.ToArray();
    }

    public static byte[] EncodeExtendedUserState(
        ActiveCharacterState character,
        CharacterPublicEntryState publicState,
        CharacterSkillsEntryState skills,
        IReadOnlyList<InventoryItemState> equipment,
        DateTimeOffset preparedAt)
    {
        var stats = publicState.DerivedStats; var extras = publicState.Extras;
        var pvp = publicState.Pvp; var guild = publicState.Guild;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)1); writer.Write((ushort)1); writer.Write(checked((ushort)character.Identity.CharacterId.Value)); writer.Write(checked((uint)extras.CharacterExperience));
        writer.Write(83); writer.Write(checked((uint)stats.HpMaximum)); writer.Write(checked((uint)stats.MpMaximum)); writer.Write(checked((short)stats.PhysicalAttack)); writer.Write(checked((short)stats.PhysicalDefense));
        writer.Write(checked((short)stats.MagicalAttack)); writer.Write(checked((short)stats.MagicalDefense)); writer.Write(checked((short)stats.FireResistance)); writer.Write(checked((short)stats.WaterResistance)); writer.Write(checked((short)stats.DarkResistance)); writer.Write(checked((short)stats.LightResistance));
        writer.Write(new byte[34]); writer.Write(checked((uint)stats.InventoryLimit)); writer.Write(checked((ushort)stats.HpRegenSpeed)); writer.Write(checked((ushort)stats.MpRegenSpeed)); writer.Write(checked((uint)stats.MoveSpeed));
        writer.Write(checked((ushort)stats.AttackSpeed)); writer.Write(checked((ushort)stats.CastSpeed)); writer.Write(checked((ushort)stats.HitRecovery)); writer.Write(checked((ushort)stats.JumpPower)); writer.Write(checked((uint)stats.Weight)); writer.Write(checked((byte)stats.CurrentHpMpPercent)); writer.Write(checked((byte)extras.ExtraEquipmentSlotState));
        if (equipment.Count > byte.MaxValue) throw CountError("equipped item", "u8"); writer.Write(checked((byte)equipment.Count));
        foreach (var item in equipment.OrderBy(item => item.SlotIndex)) WriteNoti2Item(writer, item, preparedAt);
        writer.Write(publicState.CloneTitleItemId); writer.Write(checked((uint)extras.NameTagItemId)); writer.Write(checked((uint)extras.NameTagExpireTime)); writer.Write(SkillTreeIndexOnTheWire(extras.SkillTreeIndex));
        for (byte page = 0; page < 2; page++)
        {
            var learned = skills.Pages.SingleOrDefault(p => p.PageIndex == page)?.Skills.Where(s => s.Level > 0).ToArray() ?? [];
            if (learned.Length > byte.MaxValue) throw CountError("subtype1 skill", "u8"); writer.Write(checked((byte)learned.Length));
            foreach (var skill in learned) { writer.Write(checked((ushort)skill.SkillId)); writer.Write(skill.Level); }
        }
        writer.Write(checked((byte)stats.EquippedCreatureLevel)); writer.Write(checked((byte)extras.Dimensions.Count));
        foreach (var d in extras.Dimensions.OrderBy(value => value.SortOrder))
        {
            writer.Write(checked((uint)d.Key)); writer.Write(checked((byte)d.Value1)); writer.Write(checked((byte)d.Value2));
        }
        writer.Write(checked((byte)extras.DimensionFlags.Flag1)); writer.Write(checked((byte)extras.DimensionFlags.Flag2));
        writer.Write(checked((byte)extras.DimensionFlags.Flag3)); writer.Write(checked((byte)extras.DimensionFlags.Flag4));
        var pvpResults = pvp.Results ?? throw Missing("CharacterEntry.Public.Pvp.Results");
        var specialRewards = extras.SpecialRewardQuestIds ?? throw Missing("CharacterEntry.Public.Extras.SpecialRewardQuestIds");
        if (pvpResults.Count > byte.MaxValue) throw CountError("subtype1 PvP result", "u8");
        writer.Write(checked((byte)pvpResults.Count));
        foreach (var pvpResult in pvpResults)
        {
            writer.Write(checked((uint)pvpResult.Value32));
            writer.Write(checked((ushort)pvpResult.Value16A));
            writer.Write(checked((ushort)pvpResult.Value16B));
        }
        writer.Write(checked((byte)extras.ManageLevel)); writer.Write(checked((uint)specialRewards.Count));
        foreach (var questId in specialRewards) writer.Write(checked((uint)questId));
        writer.Write(checked((byte)stats.FlagByte)); writer.Write(checked((uint)guild.PowerWar)); writer.Write(checked((uint)stats.ServerTimestamp)); writer.Write(checked((ushort)stats.QuestShopCount));
        writer.Write(checked((uint)stats.Progress1)); writer.Write(checked((uint)stats.Progress2)); return stream.ToArray();
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
    /// <summary>
    /// The skill-tree index as the client's one-byte field carries it.
    /// </summary>
    /// <remarks>
    /// Storage and wire do not share an encoding. In storage the field is signed: <c>0</c> and
    /// <c>1</c> are the purchased second skill page and which of the two is current, while
    /// <c>-1</c> means the page was never bought. On the wire the byte is <c>0xFF</c> for the
    /// unbought case. The reference server states both halves and maps between them in
    /// <c>Game/Skills/SkillTreeExpansionState.cs</c>.
    ///
    /// Encoding used to be <c>checked((byte)index)</c>, which threw <see cref="OverflowException"/>
    /// on <c>-1</c>. Since -1 is the state of every character that has not bought the page --
    /// including every newly created one -- the entry batch could not be encoded for any of them.
    /// The storage validator has always admitted the full -1..1 range, so the two sides disagreed
    /// about the same field.
    /// </remarks>
    private static byte SkillTreeIndexOnTheWire(int storedIndex) =>
        storedIndex is 0 or 1 ? (byte)storedIndex : (byte)0xFF;

    private static void WriteDstr(BinaryWriter writer, byte[] value) { writer.Write(value.Length); writer.Write(value); }
    public static byte[] EncodeUnsigned32(uint value) { var body = new byte[4]; WriteInt32(body, 0, checked((int)value)); return body; }
    private static void WriteInt32(byte[] target, int offset, int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value);
    private static InvalidDataException CountError(string name, string width) => new($"{name} count cannot be represented as {width}.");
}
