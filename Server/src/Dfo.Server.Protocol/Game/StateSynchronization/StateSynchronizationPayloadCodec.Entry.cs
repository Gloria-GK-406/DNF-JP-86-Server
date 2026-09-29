using Dfo.Server.Protocol.Game.Mailbox;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal static partial class StateSynchronizationPayloadCodec
{
    public static byte[] EncodePremiumService(
        AccountEntitlementEntryState entitlements,
        IReadOnlyList<CharacterPremiumUsageSlotState> usageSlots, DateTimeOffset preparedAt)
    {
        var data = new byte[74];
        foreach (var premium in entitlements.Premiums)
        {
            var slot = premium.PremiumType - 580;
            if (slot is < 0 or >= 8 || premium.ExpiresAtUnixSeconds <= preparedAt.ToUnixTimeSeconds()) continue;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6 + slot * 9, 4),
                checked((uint)premium.ExpiresAtUnixSeconds));
        }
        foreach (var usage in usageSlots)
        {
            if (usage.SlotIndex >= 8 || usage.UsedCount is < 0 or > byte.MaxValue)
                throw new InvalidDataException("Premium usage slot is outside the observed 8-slot layout.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6 + usage.SlotIndex * 9, 4)) != 0)
                data[10 + usage.SlotIndex * 9] = checked((byte)usage.UsedCount);
        }
        using var stream = new MemoryStream(77);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write((ushort)1);
        writer.Write(data);
        return stream.ToArray();
    }

    public static byte[] EncodeCreatures(IReadOnlyList<CharacterPetRuntimeState> pets) => GameInstanceDelivery.EncodeCreatures(pets);

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
        var core = item.Core ?? throw Missing($"SupportEquipment.Core[slot {item.SlotIndex}]");
        if (item.SlotIndex is < 0 or > byte.MaxValue)
            throw new InvalidDataException("NOTI-2 equipment slot must fit u8.");
        if (core.ItemKind == avatarKind && item.AvatarDetail is null)
            throw Missing($"SupportEquipment.AvatarDetail[slot {item.SlotIndex}]");
        if (core.ItemKind != avatarKind && item.AvatarDetail is not null)
            throw new InvalidDataException("Non-avatar support equipment carries avatar detail.");

        writer.Write(checked((byte)item.SlotIndex));
        writer.Write(core.ItemId);
        writer.Write(unchecked((uint)(core.ItemKind == avatarKind
            ? AvatarLifetimeOnTheWire.RemainingSeconds(item.AvatarDetail!.ExpireDate, preparedAt)
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

        if (IsCostumeBarSlot(item.SlotIndex))
        {
            if (core.ItemKind == avatarKind)
            {
                if (item.AvatarDetail!.JewelSocket.Length != StoredJewelBytes)
                    throw new InvalidDataException("Support avatar jewel socket must contain exactly 30 bytes.");
                AvatarJewelSocketOnTheWire.WriteJewelRun(writer, item.AvatarDetail.JewelSocket);
            }
            else
            {
                writer.Write(0);
            }
            writer.Write(4);
            writer.Write(item.AvatarDetail?.Color1 ?? 0);
            writer.Write(item.AvatarDetail?.Color2 ?? 0);
        }

        if (item.SlotIndex == Noti2CreatureSlot)
        {
            if (core.ItemKind == creatureKind && item.Noti2CreatureMarker is null)
                throw Missing($"SupportEquipment.Noti2CreatureMarker[slot {item.SlotIndex}]");
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
        WriteNoti2EntryTail(writer, item.SlotIndex);
    }

    /// <summary>What A21 appends to every equipped entry, and the older client appended to none.</summary>
    /// <remarks>
    /// Seventeen bytes for an ordinary worn slot and nine for a costume-bar or creature one. Read
    /// off a working A21 session: its three worn pieces are sixty bytes each, and the sixty
    /// decompose only with this on the end. The <c>8</c> is written because that is what the
    /// session carried; nothing here knows what it counts.
    /// </remarks>
    private static void WriteNoti2EntryTail(BinaryWriter writer, int slotIndex)
    {
        if (!IsCostumeBarSlot(slotIndex) && slotIndex != Noti2CreatureSlot)
        {
            writer.Write(0u);
            writer.Write(Noti2EntryTailConstant);
        }

        writer.Write(new byte[9]);
    }

    private const uint Noti2EntryTailConstant = 8;

    /// <summary>
    /// The slots the costume bar draws, which carry an avatar block and take the short tail.
    /// </summary>
    /// <remarks>
    /// Zero through eleven. A21 added the aurora-illusion slot at eleven, so the older client's
    /// boundary of ten is one short here.
    /// </remarks>
    private static bool IsCostumeBarSlot(int slotIndex) => slotIndex is >= 0 and <= 11;

    /// <summary>How long a stored jewel socket is, which is not how much of it goes on the wire.</summary>
    /// <remarks>
    /// Thirty: five slots of six bytes, and the shape the store guarantees
    /// (<c>CHECK(length(avatar_jewel_socket) = 30)</c>). The two writers below check an incoming
    /// socket against it and then hand the whole thing to
    /// <see cref="AvatarJewelSocketOnTheWire.WriteJewelRun"/>, which decides how much of it A21
    /// actually takes. Keeping the two figures apart is the point: they were the same number until
    /// the wire moved to eighteen, and writing thirty in both places is how they came to disagree.
    /// </remarks>
    private const int StoredJewelBytes = 30;

    /// <summary>The slot whose entry carries the creature marker.</summary>
    /// <remarks>
    /// Upstream A21 identifies 25; an A21 move capture remains pending. Legacy stored
    /// positions are normalized before this entry projection runs.
    /// </remarks>
    private const int Noti2CreatureSlot = 25;

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

    /// <summary>The 99-entry key binding table a character receives when it has stored none.</summary>
    /// <remarks>
    /// The table is positional: the index is the action and the value is the key bound to it, with
    /// <c>0x0086</c> meaning unbound. A character that has never saved its keys still has to be told
    /// what they are, and a slot count of zero is not the same statement -- it leaves the client
    /// without the table the rest of the town session indexes. This mirrors the account-scoped
    /// substitution above for the same reason.
    ///
    /// Transcribed from the entry batch of a working A21 session (see the trace archived under
    /// <c>protocol/patterns/data/traces/</c>). That session sends 0x01C7 exactly once, at entry,
    /// carrying these 99 values. Whether they are the stock defaults or that character's own
    /// bindings is not established -- what the capture fixes is the table's length and layout.
    /// </remarks>
    private static readonly ushort[] DefaultCharacterHotkeyTable =
    [
        0x0002, 0x0000, 0x0003, 0x0001, 0x004E, 0x0039, 0x0050, 0x000D, 0x004C, 0x0043, 0x0041, 0x003F,
        0x0045, 0x0052, 0x004B, 0x0055, 0x0044, 0x0056, 0x0054, 0x0051, 0x0037, 0x0049, 0x003A, 0x003C,
        0x003D, 0x003E, 0x0047, 0x004D, 0x003B, 0x0048, 0x004A, 0x004F, 0x002E, 0x002F, 0x0030, 0x0031,
        0x0032, 0x0033, 0x0086, 0x001C, 0x0053, 0x0057, 0x0086, 0x006C, 0x0086, 0x0086, 0x0086, 0x0086,
        0x001A, 0x0046, 0x0068, 0x0069, 0x0042, 0x001B, 0x006A, 0x0086, 0x0086, 0x0086, 0x0038, 0x0086,
        0x0086, 0x0086, 0x0086, 0x0086, 0x0086, 0x0086, 0x0007, 0x0086, 0x0086, 0x0086, 0x0086, 0x0086,
        0x0086, 0x0086, 0x0059, 0x0086, 0x0086, 0x0086, 0x0086, 0x0058, 0x0086, 0x005A, 0x005B, 0x005C,
        0x0086, 0x0086, 0x0086, 0x0086, 0x001F, 0x0086, 0x0086, 0x0086, 0x0086, 0x0086, 0x0086, 0x0015,
        0x0086, 0x0018, 0x0086
    ];

    public static byte[] EncodeHotkeys(
        AccountSettingsEntryState accountSettings,
        CharacterSettingsEntryState characterSettings)
    {
        var slots = characterSettings.HotkeySlots.Count > 0
            ? characterSettings.HotkeySlots
            : DefaultCharacterHotkeyTable;
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
        writer.Write(checked((ushort)entries.Count)); foreach (var entry in entries) { writer.Write(checked((uint)entry.DungeonId)); writer.Write(entry.State); }
        return stream.ToArray();
    }

    public static byte[] EncodeCharacterOption(byte[]? option)
    {
        if (option is not null) return [.. option];
        return new byte[4];
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

    public static byte[] EncodeCera(AccountWalletEntryState wallet) =>
        GameInstanceDelivery.EncodeWallet(new(wallet.Cera, wallet.TokenCera, wallet.HappyTokenCera));

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
        var summaries = new List<(CharacterMailboxMessageEntryState Message, CharacterMailboxAttachmentEntryState? Attachment, int Gold, long? GoldId, bool SeedOnly)>();
        foreach (var message in mailbox.Messages)
        {
            foreach (var attachment in message.Attachments.Where(value => value.ItemTemplateId > 0 && value.ItemCount > 0))
                summaries.Add((message, attachment, 0, null, false));
            foreach (var gold in message.GoldAttachments)
                summaries.Add((message, null, gold.Amount, gold.AttachmentId, false));
            // Old projection callers have no independent gold keys; preserve only their historical shape.
            if (message.GoldAttachments.Count == 0 && message.Gold > 0)
                summaries.Add((message, null, message.Gold, null, false));
            if (message.Attachments.Count == 0 && message.Gold == 0)
                summaries.Add((message, null, 0, null, true));
        }
        if (summaries.Count > byte.MaxValue) throw CountError("mailbox summary", "u8");

        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)summaries.Count)); writer.Write((byte)0);
        foreach (var summary in summaries)
            WriteMailboxSummary(writer, summary.Message, summary.Attachment, summary.Gold, summary.GoldId, summary.SeedOnly);
        writer.Write(checked((ushort)mailbox.NotLoadedCount));
        writer.Write(checked((ushort)mailbox.Messages.Count));
        foreach (var message in mailbox.Messages)
        {
            writer.Write(checked((int)message.MessageId)); writer.Write(MailboxSenderId(message)); WriteMailboxString(writer, MailboxSenderName(message), 30);
            var display = message.Title.Length == 0 || message.Title.SequenceEqual(message.Body)
                ? message.Body
                : [.. message.Title, (byte)'\n', .. message.Body];
            WriteMailboxString(writer, display, 512); writer.Write(Math.Max(0, message.CreatedAtUnixSeconds)); writer.Write((ushort)message.LetterState);
            writer.Write((byte)0); // A21 detail terminal byte @01199613; semantics pending.
        }
        return stream.ToArray();
    }

    private static void WriteMailboxSummary(
        BinaryWriter writer,
        CharacterMailboxMessageEntryState message,
        CharacterMailboxAttachmentEntryState? attachment,
        int gold,
        long? goldId,
        bool seedOnly)
    {
        var hasItem = attachment is { ItemTemplateId: > 0, ItemCount: > 0 };
        var core = hasItem ? DecodeMailboxCore(attachment!) : null;
        var messageId = checked((int)message.MessageId);
        uint claimWireId = hasItem ? MailboxIds.Attachment(attachment!.AttachmentId)
            : goldId is { } id ? MailboxIds.Attachment(id) : checked((uint)messageId);
        writer.Write(claimWireId);
        writer.Write(MailboxSenderId(message));
        WriteMailboxString(writer, MailboxSenderName(message), 30);
        writer.Write(gold);
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
        writer.Write(4); writer.Write(0);
        writer.Write(8); writer.Write(0L);
        writer.Write((byte)0); // A21 @011991FA after the two unconditional length blocks.
        if (avatar) WriteMailboxAvatarDetail(writer, attachment!);
        writer.Write(Math.Max(0, message.RemainingSeconds));
        writer.Write(messageId); // Same parent key for all attachments and the body record.
        writer.Write((byte)0); // A21 terminal byte @0119952B; semantics pending.
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
            if (socket.Length != StoredJewelBytes) throw new InvalidDataException("Mailbox avatar jewel socket must contain exactly 30 bytes.");
            AvatarJewelSocketOnTheWire.WriteJewelRun(writer, socket); writer.Write(4);
            writer.Write(avatar.GetProperty("Color1").GetUInt16()); writer.Write(avatar.GetProperty("Color2").GetUInt16());
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException($"Mailbox avatar detail {attachment.AttachmentId} is malformed.", error);
        }
    }

    private static int MailboxSenderId(CharacterMailboxMessageEntryState message) => message.MailType != 0 ? 0 : message.SenderCharacterId;
    private static byte[] MailboxSenderName(CharacterMailboxMessageEntryState message) => message.MailType != 0 || message.SenderCharacterId is 0 or 2000000000 ? [] : message.SenderName;
    private static void WriteMailboxString(BinaryWriter writer, byte[] value, int maximumBytes)
    {
        var bytes = value.Length < maximumBytes ? value : value[..(maximumBytes - 1)];
        WriteDstr(writer, bytes);
    }

    /// <summary>The zero block A21 carries between the subtype's version and the character id.</summary>
    /// <remarks>
    /// Measured, not derived: the known-good session's subtype-zero body has thirty-eight zero
    /// bytes there and the older client had none. Nothing here knows what they are for -- only
    /// that everything after them sits thirty-eight bytes further along, which is the part that
    /// matters to a reader.
    /// </remarks>
    private const int EntrySubtypeZeroReservedBlock = 38;

    /// <summary>Where the character id sits in a subtype-0 USERINFO body.</summary>
    /// <remarks>
    /// <c>subtype(1) + version(2) + reserved(38)</c>. Published because the PvP lobby codec reads
    /// the same body and had the A12 offset (3) baked in; two copies of this number drift the day
    /// the reserved block changes again, and the failure is a lobby that rejects every identity.
    /// </remarks>
    internal const int CurrentUserStateCharacterIdOffset = 3 + EntrySubtypeZeroReservedBlock;

    /// <summary>Whether what subtype zero draws depends on what is worn in this slot.</summary>
    /// <remarks>
    /// The union of the two things this frame draws from what is worn, and deliberately a union
    /// rather than a third filter: the appearance run is
    /// <see cref="DrawnAppearance.DrawsFrom"/>'s answer -- asked rather than restated, because the
    /// character-selection roster draws from the same run -- and the creature triple takes
    /// <c>24</c>. Anyone asking "would this frame come out differently if that slot changed" has to
    /// ask about both, and asking here is what keeps a fourth slot filter from appearing in this
    /// repository -- there are already three that disagree with each other, and reconciling them
    /// changes bytes.
    /// <para>
    /// It answers about a worn slot. A slot number from the bag or the avatar page means something
    /// else entirely, and the caller is the one that knows which list it holds.
    /// </para>
    /// </remarks>
    internal static bool CurrentUserStateDrawsFrom(int wornSlotIndex) =>
        DrawnAppearance.DrawsFrom(wornSlotIndex) || wornSlotIndex == Noti2CreatureSlot;

    public static byte[] EncodeCurrentUserState(
        ActiveCharacterState character,
        CharacterPublicEntryState entry,
        IReadOnlyList<InventoryItemState> equipment,
        IReadOnlyList<CharacterPetRuntimeState> pets)
    {
        var identity = entry.Identity ?? throw Missing("CharacterEntry.Public.Identity");
        // PvP standing, guild, settings and derived stats used to be read here for the tail this
        // frame no longer writes field by field. They are still loaded on the character -- other
        // frames use them -- but subtype zero does not mention them on A21.
        var c = entry.Condition; var job = entry.ExpertJob; var extras = entry.Extras;
        // Worked out where the roster screen works it out, so the two pictures of one character
        // cannot disagree. This used to be a filter and eight literal writes inline here; the
        // writes below are the same eight fields, now read off the part rather than recomputed.
        var appearance = DrawnAppearance.Of(equipment);
        if (appearance.Count > byte.MaxValue) throw CountError("appearance", "u8");
        var creature = equipment.SingleOrDefault(item => item.SlotIndex == Noti2CreatureSlot && item.Core.ItemKind == 5);
        var pet = creature is null ? null : pets.SingleOrDefault(value => value.PetKey == creature.Core.Value);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)0); writer.Write((ushort)1);
        // A21 puts thirty-eight zero bytes between the version and the character id. The older
        // client had none, and every field after this point is where it is because of them.
        writer.Write(new byte[EntrySubtypeZeroReservedBlock]);
        writer.Write(checked((ushort)character.Identity.CharacterId.Value)); WriteDstr(writer, character.Identity.Name);
        writer.Write(character.Identity.Job); writer.Write(character.Identity.GrowType); writer.Write(character.Progression.Level);
        writer.Write(identity.PvpGrade); writer.Write(identity.PvpRatingGrade); writer.Write(identity.UserState); writer.Write(checked((byte)appearance.Count));
        foreach (var part in appearance)
        {
            writer.Write(part.Slot); writer.Write(part.DisplayItemId); writer.Write(part.ExpansionLength); writer.Write(part.ExpansionData);
            writer.Write(part.State);
            writer.Write(part.LinkItemId); writer.Write(part.EnchantValue); writer.Write(part.Flag20);
        }
        writer.Write(entry.CloneTitleItemId); writer.Write((byte)0); writer.Write(checked((byte)c.CreatureField2)); writer.Write(checked((byte)c.CreatureField3)); writer.Write(checked((byte)c.CreatureField4));
        writer.Write(checked((uint)extras.NameTagItemId)); writer.Write(checked((uint)extras.NameTagExpireTime)); writer.Write(checked((byte)c.Stamina)); writer.Write(checked((uint)c.FatiguePenalty)); writer.Write(c.EventCharacter != 0 ? (byte)1 : (byte)0);
        WriteCreatureTriple(writer, creature, pet);
        writer.Write(BuildSubtypeZeroTail(job, entry.HasBlackDiamond));
        return stream.ToArray();
    }

    /// <summary>The twenty-six dimension keys A21 always sends, with the value each defaults to.</summary>
    /// <remarks>
    /// A21's dimension list is not "whatever the character has stored" -- it is this fixed roll of
    /// twenty-six keys, every time, with a stored entry overriding a key's pair when one exists.
    /// The reference session's subtype one declares twenty-six and carries 26x6 bytes of them, and
    /// the A21 server that produced it holds the same table
    /// (`UserInfoSubtype1Builder.A21DimensionKeys` / `A21DimensionDefaultVal2`).
    ///
    /// This server sent its stored list instead, which on a new character is empty -- so the frame
    /// declared zero where A21 declares twenty-six, and everything after it landed 156 bytes early.
    /// </remarks>
    private static readonly (long Key, byte DefaultValue2)[] SubtypeOneDimensions =
    [
        (11006, 3), (11007, 3), (3054, 3), (3056, 3), (3057, 1), (122, 9), (4000, 1), (3706, 3),
        (4108, 1), (4109, 1), (4110, 1), (4111, 1), (4103, 3), (4114, 3), (4115, 3), (4116, 3),
        (4117, 3), (4118, 3), (4130, 3), (3900, 3), (4124, 1), (4125, 1), (4126, 1), (4127, 1),
        (4128, 1), (4123, 3),
    ];

    /// <summary>What A21 writes between the dimension roll and the manage-level pair.</summary>
    private static readonly byte[] SubtypeOneAfterDimensionPrefix =
        [0x02, 0x00, 0x05, 0x00, 0x6F, 0x00, 0x00, 0x00, 0x00];

    /// <summary>And what it writes after the manage point, to the end of the frame.</summary>
    private static readonly byte[] SubtypeOneAfterDimensionRest =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    /// <summary>Subtype one from the dimension roll to the end of the frame.</summary>
    /// <remarks>
    /// The two constant runs replace what this server wrote as four dimension flags, a PvP result
    /// list, a special-reward quest list and six trailing stat fields. Those are the older client's
    /// tail; A21's own server emits these bytes instead, and the capture agrees with it. Only the
    /// manage level and manage point sit inside as values, at the offsets the upstream patches
    /// them into -- what the rest of the twenty-five constant bytes stand for is not established.
    /// </remarks>
    private static void WriteSubtypeOneDimensionTail(
        BinaryWriter writer,
        CharacterEntryExtrasState extras)
    {
        // Grouped rather than ToDictionary: character_dimensions is keyed on (character_id,
        // sort_order), so dim_key is not unique and a duplicate would throw ArgumentException --
        // the wrong exception type for this layer, and one that would abort the whole batch where
        // the previous sequential write was merely odd. First by sort order wins, which is the
        // order the previous code would have written them in.
        //
        // A stored dimension whose key is not one of the twenty-six is dropped. That is what A21
        // does, and there is no wire position for it, but it is a silent loss rather than an error.
        var stored = extras.Dimensions
            .OrderBy(value => value.SortOrder)
            .GroupBy(value => value.Key)
            .ToDictionary(group => group.Key, group => group.First());
        writer.Write(checked((byte)SubtypeOneDimensions.Length));
        foreach (var (key, defaultValue2) in SubtypeOneDimensions)
        {
            var (value1, value2) = stored.TryGetValue(key, out var saved)
                ? (checked((byte)saved.Value1), checked((byte)saved.Value2))
                : ((byte)0, defaultValue2);
            writer.Write(checked((uint)key));
            writer.Write(value1);
            writer.Write(value2);
        }

        writer.Write(SubtypeOneAfterDimensionPrefix);
        writer.Write(checked((byte)extras.ManageLevel));
        // A manage point, which this server does not model. Zero is what the reference carries, so
        // it is not a placeholder standing in for an unknown -- it is the observed value.
        writer.Write(0u);
        writer.Write(SubtypeOneAfterDimensionRest);
    }

    /// <summary>Everything subtype zero writes after the creature triple: a fixed 64-byte block.</summary>
    /// <remarks>
    /// This used to be forty-odd named fields -- guild level, chaos point, disguise, hardcore
    /// counters, fatigue, channel, emotion, PvP standing -- laid out as the older client wanted
    /// them, and it came to 81 bytes. A21 puts 64 there.
    ///
    /// The bytes are not a guess. The reference session's subtype zero ends with exactly this block,
    /// and the A21 server that produced that capture writes it as a constant with three patch
    /// points (`UserInfoSubtype0Builder.A21AfterAliveNoGuild`): the expert-job type at +23, its
    /// experience at +24, and what it calls a channel id at +59. Comparing that constant against
    /// the captured 64 bytes leaves exactly one difference -- offset 59, holding 2, which is that
    /// third patch point at its default. Everything else matches byte for byte.
    ///
    /// The block remains transcribed, except offset 1 now carries the ClientStatic black-diamond
    /// name marker. The remaining unknown bytes are not established here, and the fields this replaced are not gone from the domain -- they simply
    /// have no known home on A21's wire. Writing them at the older client's offsets was not a
    /// partial answer but a wrong one: every field after the first sat at an offset A21 does not
    /// use, and the block being 17 bytes shorter means the frame ended in the wrong place too.
    ///
    /// The upstream comment on that constant is worth carrying over: its shape was chosen so the
    /// client would not read a stale progress value as a string length. A field written into the
    /// wrong place here does not merely display wrongly -- it can make the client size a read from
    /// data that is not a length.
    /// </remarks>
    private static readonly byte[] SubtypeZeroFixedTail =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0xCE, 0xDE, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00,
    ];

    private const int SubtypeZeroTailExpertJobTypeOffset = 23;
    private const int SubtypeZeroTailExpertJobExperienceOffset = 24;
    private const int SubtypeZeroTailChannelOffset = 59;

    /// <summary>The channel value the reference carries, and the upstream default.</summary>
    /// <remarks>
    /// Two, on a session whose channel is advertised as 11 -- so this is not the channel number.
    /// The upstream calls it a channel id and substitutes two whenever it holds zero, which is
    /// every session this server has observed. What it selects is unestablished, so it is written
    /// as the value the reference carries rather than derived from a channel field that would only
    /// look related.
    /// </remarks>
    private const ushort SubtypeZeroTailChannel = 2;

    private static byte[] BuildSubtypeZeroTail(CharacterExpertJobStorageRecord job, bool hasBlackDiamond)
    {
        var tail = SubtypeZeroFixedTail.ToArray();
        // A21 read at VA013E2DB0 -> user info +0x2E8; name renderer selects diamond frame 3.
        // Byte 0 is the separate premium PC-room flag and must not be changed here.
        tail[1] = hasBlackDiamond ? (byte)1 : (byte)0;
        tail[SubtypeZeroTailExpertJobTypeOffset] = checked((byte)job.Type);
        // No expert job means no experience. The upstream projects the same way, and specifically
        // refuses to send the older sentinel of -1.
        var experience = job.Type == 0 ? 0u : checked((uint)Math.Max(0L, job.Experience));
        BinaryPrimitives.WriteUInt32LittleEndian(
            tail.AsSpan(SubtypeZeroTailExpertJobExperienceOffset), experience);
        BinaryPrimitives.WriteUInt16LittleEndian(
            tail.AsSpan(SubtypeZeroTailChannelOffset), SubtypeZeroTailChannel);
        return tail;
    }

    /// <summary>The label A21 sends in the name slot when there is no creature: 没有宠物.</summary>
    /// <remarks>
    /// Written as bytes rather than <c>Encoding.UTF8.GetBytes</c> of a literal. What goes on the
    /// wire should not depend on how this source file happens to be saved, and every other
    /// transcribed run in this file is a byte array for the same reason.
    /// </remarks>
    private static readonly byte[] NoCreatureLabel =
        [0xE6, 0xB2, 0xA1, 0xE6, 0x9C, 0x89, 0xE5, 0xAE, 0xA0, 0xE7, 0x89, 0xA9];

    /// <summary>Writes the creature slot, including the shape A21 uses when there is none.</summary>
    /// <remarks>
    /// A21 does not spell "no creature" as zeros. The reference session, whose character has none,
    /// writes <c>FFFFFFFF</c> for the item, the label above for the name, and <c>FF</c> for the
    /// state -- twenty-one bytes where a zero item, an empty string and a zero state would be nine.
    /// That the label is the client's own "no pet" text makes this a display name the server fills
    /// in, not a name the client substitutes when the field is empty.
    ///
    /// A character that does have one is unobserved on A21: the reference never equips a creature.
    /// The populated branch is therefore unchanged from the older client.
    /// </remarks>
    private static void WriteCreatureTriple(
        BinaryWriter writer,
        InventoryItemState? creature,
        CharacterPetRuntimeState? pet)
    {
        if (creature is null)
        {
            writer.Write(uint.MaxValue);
            WriteDstr(writer, NoCreatureLabel);
            writer.Write(byte.MaxValue);
            return;
        }

        writer.Write(checked((uint)creature.Core.ItemId));
        WriteDstr(writer, pet?.CreatureText ?? []);
        writer.Write(pet?.Field04 > 0 ? (byte)1 : (byte)0);
    }

    /// <summary>The zero block A21 carries between subtype one's version and the character id.</summary>
    /// <remarks>
    /// Fifteen bytes, measured the same way as subtype zero's thirty-eight: with them in place the
    /// fields that follow line up with the known-good session -- the character id at eighteen, the
    /// stat-block length at twenty-four, maximum HP at twenty-eight -- and without them nothing
    /// after the version does.
    /// </remarks>
    private const int ExtendedSubtypeReservedBlock = 15;

    /// <summary>The length the derived-stat block declares about itself.</summary>
    /// <remarks>Eighty-three on the older client, eighty-eight here. Read off the wire, not derived.</remarks>
    private const int DerivedStatBlockLength = 88;

    /// <summary>
    /// The one non-zero value in the padding that follows the elemental resistances.
    /// </summary>
    /// <remarks>
    /// The block is thirty-four bytes either way; the older client left all of it zero and A21
    /// puts 5500 in the last sixteen-bit slot. What it means is unknown -- it is written because
    /// that is what the session carried, and a reader deserves to know it is not a length or a
    /// count that was worked out.
    /// </remarks>
    private const ushort UnnamedStatBeforeInventoryLimit = 5500;

    /// <summary>
    /// What A21 carries where the older client carried the current HP/MP percentage.
    /// </summary>
    /// <remarks>
    /// The older client took one byte here. A21 takes a 32-bit hundred followed by a zero byte,
    /// and the character's actual percentage is not in this frame at all on this client. Writing
    /// the stored percentage into the first byte of a four-byte field would put a plausible number
    /// in the wrong place and shift everything after it.
    /// </remarks>
    private const uint FullHpMpPercentOnTheWire = 100;

    /// <summary>
    /// Encodes A21 USERINFO subtype 3, the response to GET_USERINFO mode 3 for another current
    /// character. Its prefix and trailer are not subtype one's: A21 consumes a five-byte reserved
    /// run before the target uid and a 27-byte inspect context before the guild dstr.
    /// </summary>
    public static byte[] EncodeOtherCharacterDetail(
        ActiveCharacterState character,
        CharacterPublicEntryState publicState,
        CharacterSkillsEntryState skills,
        IReadOnlyList<InventoryItemState> equipment,
        DateTimeOffset preparedAt)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(publicState);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(equipment);

        const int inspectReservedPrefixLength = 5;
        const int inspectContextLength = 27;
        const byte inspectGuildMarker = 0x6F;
        var stats = publicState.DerivedStats;
        var extras = publicState.Extras;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)3);
        writer.Write((ushort)1);
        writer.Write(checked((ushort)extras.ManageLevel));
        writer.Write(new byte[inspectReservedPrefixLength]);
        writer.Write(checked((ushort)character.Identity.CharacterId.Value));
        writer.Write(checked((uint)extras.CharacterExperience));
        writer.Write(DerivedStatBlockLength);
        WriteOtherCharacterDetailCombatStats(writer, stats);
        writer.Write(checked((byte)extras.ExtraEquipmentSlotState));

        if (equipment.Count > byte.MaxValue)
            throw CountError("inspected equipped item", "u8");
        writer.Write(checked((byte)equipment.Count));
        foreach (var item in equipment.OrderBy(value => value.SlotIndex))
            WriteNoti2EquippedItem(writer, item, preparedAt);

        writer.Write(publicState.CloneTitleItemId);
        writer.Write(checked((uint)extras.NameTagItemId));
        writer.Write(checked((uint)extras.NameTagExpireTime));
        writer.Write(SkillTreeIndexOnTheWire(extras.SkillTreeIndex));
        WriteOtherCharacterDetailSkillPage(writer, skills, 0);
        WriteOtherCharacterDetailSkillPage(writer, skills, 1);
        writer.Write(checked((byte)stats.EquippedCreatureLevel));

        writer.Write(new byte[inspectContextLength]);
        // Guild identity is not modelled yet. The empty dstr is the A21-compatible no-guild
        // shape; level remains projected from the target's current guild standing.
        WriteDstr(writer, []);
        writer.Write(checked((byte)publicState.Guild.Level));
        writer.Write((byte)0);
        writer.Write(inspectGuildMarker);
        writer.Write(checked((uint)extras.SpecialRewardQuestIds.Count));
        foreach (var questId in extras.SpecialRewardQuestIds)
            writer.Write(checked((uint)questId));
        writer.Write((byte)0);
        return stream.ToArray();
    }

    private static void WriteOtherCharacterDetailCombatStats(
        BinaryWriter writer,
        CharacterDerivedStatsStorageRecord stats)
    {
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
        writer.Write(new byte[32]);
        writer.Write(UnnamedStatBeforeInventoryLimit);
        writer.Write(checked((uint)stats.InventoryLimit));
        writer.Write(checked((ushort)stats.HpRegenSpeed));
        writer.Write(checked((ushort)stats.MpRegenSpeed));
        writer.Write(checked((uint)stats.MoveSpeed));
        writer.Write(checked((ushort)stats.AttackSpeed));
        writer.Write(checked((ushort)stats.CastSpeed));
        writer.Write(checked((ushort)stats.HitRecovery));
        writer.Write(checked((ushort)stats.JumpPower));
        writer.Write(checked((uint)stats.Weight));
        writer.Write(FullHpMpPercentOnTheWire);
        writer.Write((byte)0);
        writer.Write((byte)0);
    }

    private static void WriteOtherCharacterDetailSkillPage(
        BinaryWriter writer,
        CharacterSkillsEntryState skills,
        byte pageIndex)
    {
        var learned = skills.Pages
            .SingleOrDefault(page => page.PageIndex == pageIndex)?.Skills
            .Where(skill => skill.Level > 0)
            .OrderBy(skill => skill.Slot)
            .ToArray() ?? [];
        if (learned.Length > byte.MaxValue)
            throw CountError("inspected skill page", "u8");
        writer.Write(checked((byte)learned.Length));
        foreach (var skill in learned)
        {
            writer.Write(checked((ushort)skill.SkillId));
            writer.Write(skill.Level);
        }
    }

    public static byte[] EncodeExtendedUserState(
        ActiveCharacterState character,
        CharacterPublicEntryState publicState,
        CharacterSkillsEntryState skills,
        IReadOnlyList<InventoryItemState> equipment,
        DateTimeOffset preparedAt,
        ushort? routingWireUserId = null)
    {
        var stats = publicState.DerivedStats; var extras = publicState.Extras;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)1); writer.Write((ushort)1);
        // A21's dungeon projection binds a remote participant's routing user id at body +3. The
        // receiver's own USERINFO keeps this word zero. The character id remains independently at
        // +18; replacing it with the routing id makes the client create the wrong avatar identity.
        writer.Write(routingWireUserId ?? (ushort)0);
        writer.Write(new byte[ExtendedSubtypeReservedBlock - sizeof(ushort)]);
        writer.Write(checked((ushort)character.Identity.CharacterId.Value)); writer.Write(checked((uint)extras.CharacterExperience));
        writer.Write(DerivedStatBlockLength); writer.Write(checked((uint)stats.HpMaximum)); writer.Write(checked((uint)stats.MpMaximum)); writer.Write(checked((short)stats.PhysicalAttack)); writer.Write(checked((short)stats.PhysicalDefense));
        writer.Write(checked((short)stats.MagicalAttack)); writer.Write(checked((short)stats.MagicalDefense)); writer.Write(checked((short)stats.FireResistance)); writer.Write(checked((short)stats.WaterResistance)); writer.Write(checked((short)stats.DarkResistance)); writer.Write(checked((short)stats.LightResistance));
        writer.Write(new byte[32]); writer.Write(UnnamedStatBeforeInventoryLimit); writer.Write(checked((uint)stats.InventoryLimit)); writer.Write(checked((ushort)stats.HpRegenSpeed)); writer.Write(checked((ushort)stats.MpRegenSpeed)); writer.Write(checked((uint)stats.MoveSpeed));
        writer.Write(checked((ushort)stats.AttackSpeed)); writer.Write(checked((ushort)stats.CastSpeed)); writer.Write(checked((ushort)stats.HitRecovery)); writer.Write(checked((ushort)stats.JumpPower)); writer.Write(checked((uint)stats.Weight));
        writer.Write(FullHpMpPercentOnTheWire); writer.Write((byte)0);
        writer.Write(checked((byte)extras.ExtraEquipmentSlotState)); writer.Write((byte)0);
        if (equipment.Count > byte.MaxValue) throw CountError("equipped item", "u8"); writer.Write(checked((byte)equipment.Count));
        foreach (var item in equipment.OrderBy(item => item.SlotIndex)) WriteNoti2EquippedItem(writer, item, preparedAt);
        writer.Write(publicState.CloneTitleItemId); writer.Write(checked((uint)extras.NameTagItemId)); writer.Write(checked((uint)extras.NameTagExpireTime)); writer.Write(SkillTreeIndexOnTheWire(extras.SkillTreeIndex));
        var page0 = LearnedForSubtypeOne(skills, 0);
        var page1 = LearnedForSubtypeOne(skills, 1);
        WriteSubtypeOneSkillPage(writer, page0);
        WriteSubtypeOneSkillPage(writer, IsCopiedSkillPage(page0, page1) ? [] : page1);
        writer.Write(checked((byte)stats.EquippedCreatureLevel));
        WriteSubtypeOneDimensionTail(writer, extras);
        return stream.ToArray();
    }

    private static CharacterSkillState[] LearnedForSubtypeOne(
        CharacterSkillsEntryState skills, byte pageIndex) =>
        skills.Pages.SingleOrDefault(page => page.PageIndex == pageIndex)?.Skills
            .Where(skill => skill.Level > 0).OrderBy(skill => skill.Slot).ToArray() ?? [];

    private static void WriteSubtypeOneSkillPage(
        BinaryWriter writer, IReadOnlyList<CharacterSkillState> learned)
    {
        if (learned.Count > byte.MaxValue) throw CountError("subtype1 skill", "u8");
        writer.Write(checked((byte)learned.Count));
        foreach (var skill in learned)
        {
            writer.Write(checked((ushort)skill.SkillId)); writer.Write(skill.Level);
        }
    }

    /// <summary>Whether the second tree holds nothing the first does not.</summary>
    /// <remarks>
    /// A character owns two skill trees from creation and the second is a copy of the first until
    /// it is bought, so this is the ordinary case rather than an edge one. This frame reports the
    /// copy as empty: a working A21 session sends the six skills for page zero and a count of zero
    /// for page one, while the same session's skill frame sends both pages in full. The two frames
    /// disagreeing is the client's rule, not a mistake in one of them -- this one carries what
    /// differs, and the skill frame carries what is held.
    /// </remarks>
    private static bool IsCopiedSkillPage(
        IReadOnlyList<CharacterSkillState> page0, IReadOnlyList<CharacterSkillState> page1)
    {
        if (page1.Count == 0) return true;
        if (page0.Count != page1.Count) return false;
        for (var index = 0; index < page0.Count; index++)
        {
            if (page0[index].SkillId != page1[index].SkillId
                || page0[index].Level != page1[index].Level)
                return false;
        }

        return true;
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
