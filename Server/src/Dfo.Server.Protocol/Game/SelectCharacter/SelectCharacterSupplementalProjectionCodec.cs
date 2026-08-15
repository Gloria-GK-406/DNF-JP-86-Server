using System.Text;
using Dfo.Server.Data.Game;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

internal sealed record SelectCharacterPvpProjectionBatch(
    IReadOnlyList<byte[]> DirectFrames,
    IReadOnlyList<byte[]> PublicationFrames);

/// <summary>Pure framing for X-01 through X-18. Callers own applicability, recipients and mutations.</summary>
internal static class SelectCharacterSupplementalProjectionCodec
{
    public static IReadOnlyList<byte[]> EncodeDarkKnightCombo(
        SelectCharacterDarkKnightComboProjection? value)
    {
        if (value is null)
            return [];
        var body = value.Body ?? throw Missing("DarkKnightCombo.Body");
        return [Frame(0, 0x01C0, body)];
    }

    public static byte[] EncodeVisibility(SelectCharacterVisibilityProjection value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var stream = new MemoryStream(3);
        using var writer = new BinaryWriter(stream);
        writer.Write(value.UserId);
        writer.Write(value.UserStateBits);
        return Frame(0, 0x0165, stream.ToArray());
    }

    public static byte[] EncodeCloneTitle(SelectCharacterCloneTitleProjection value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var stream = new MemoryStream(5);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(value.ItemId);
        return Frame(1, 0x0239, stream.ToArray());
    }

    public static byte[] EncodeRoster(SelectCharacterRosterProjection value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var characters = value.Characters ?? throw Missing("Roster.Characters");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)2);
        writer.Write(value.SlotLimit);
        writer.Write(value.SecondarySlotLimit);
        writer.Write(value.ManageLevel);
        writer.Write(value.AdventurePoint);
        writer.Write(value.Compatibility16);
        writer.Write(value.Compatibility32);
        writer.Write(checked((ushort)characters.Count));
        foreach (var character in characters)
        {
            var appearances = character.Appearances ?? throw Missing($"Roster.Appearance[{character.CharacterId}]");
            writer.Write(character.SlotIndex);
            WriteDstr(writer, character.Name);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(character.Job);
            writer.Write(character.GrowType);
            writer.Write(character.Level);
            writer.Write(character.HonorLevel);
            writer.Write(character.HonorExperience);
            writer.Write(character.HonorReserved);
            writer.Write(checked((byte)appearances.Count));
            foreach (var appearance in appearances)
                WriteAppearance(writer, appearance);
            WriteRosterTail(writer, character.CloneTitleItemId);
        }

        var frame = Frame(0, 0x0002, stream.ToArray());
        frame[7] = 2;
        return frame;
    }

    public static byte[] EncodeHonor(SelectCharacterHonorProjection value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var stream = new MemoryStream(8);
        using var writer = new BinaryWriter(stream);
        writer.Write(value.Level);
        writer.Write(value.Experience);
        return Frame(0, 0x0289, stream.ToArray());
    }

    public static IReadOnlyList<byte[]> EncodeGrowthCapsule(SelectCharacterGrowthCapsuleProjection? value)
    {
        if (value is null)
            return [];
        using var stream = new MemoryStream(95);
        using var writer = new BinaryWriter(stream);
        writer.Write(value.Level);
        writer.Write(value.TotalExperience);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(value.Page0Sp);
        writer.Write(value.Page1Sp);
        writer.Write(value.Page0Tp);
        writer.Write(value.Page1Tp);
        writer.Write(value.PvpVictoryPoints);
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(value.GrowthCapsuleExperience);
        writer.Write(value.HonorLevel);
        writer.Write(value.HonorExperience);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        return [Frame(0, 0x0025, stream.ToArray())];
    }

    public static IReadOnlyList<byte[]> EncodeInventoryUpdates(
        IReadOnlyList<SelectCharacterInventoryUpdateProjection>? values)
    {
        if (values is null || values.Count == 0)
            return [];
        return values.Select(value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(value.Items);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)value.ContainerType);
            writer.Write(checked((ushort)value.Items.Count));
            foreach (var item in value.Items)
                SelectCharacterStaticPayloadCodec.WriteInventoryItem(writer, item, value.PreparedAt);
            return Frame(0, 0x000E, stream.ToArray());
        }).ToArray();
    }

    public static IReadOnlyList<byte[]> EncodeRejection(SelectCharacterAdmissionRejectionProjection value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var notice = new MemoryStream();
        using var writer = new BinaryWriter(notice);
        writer.Write(value.NoticeMode);
        WriteDstr(writer, Encoding.UTF8.GetBytes(value.Message ?? string.Empty));
        return
        [
            Frame(1, 0x0004, [0, value.ErrorCode]),
            Frame(0, 0x01F7, notice.ToArray()),
        ];
    }

    public static SelectCharacterPvpProjectionBatch EncodePvpLobby(SelectCharacterPvpLobbyProjection value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var identities = value.OrderedIdentities ?? throw Missing("Pvp.OrderedIdentities");
        var rooms = value.OrderedRooms ?? throw Missing("Pvp.OrderedRooms");
        var newcomer = value.Newcomer ?? throw Missing("Pvp.Newcomer");
        var roster = EncodePvpIdentityRoster([.. identities, newcomer]);
        var roomSnapshot = EncodePvpRoomSnapshot(rooms);
        var publication = EncodeSinglePvpIdentity(newcomer);
        return new SelectCharacterPvpProjectionBatch([roster, roomSnapshot], [publication]);
    }

    public static byte[] EncodePvpIdentity(SelectCharacterPvpIdentityProjection value) =>
        EncodeSinglePvpIdentity(value);

    public static byte[] EncodeTrailingEquipmentLocks(SelectCharacterResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Frame(0, 0x00FB, SelectCharacterStaticPayloadCodec.EncodeNotification(result, 0x00FB));
    }

    public static IReadOnlyList<byte[]> EncodeAcceptedQuests(
        IReadOnlyList<SelectCharacterAcceptedQuestProjection>? quests)
    {
        if (quests is null)
            return [];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((uint)quests.Count));
        foreach (var quest in quests)
        {
            writer.Write(quest.QuestId);
            writer.Write(quest.TriggerValue);
        }
        return [Frame(0, 0x023F, stream.ToArray())];
    }

    public static IReadOnlyList<byte[]> EncodePetRevival(SelectCharacterPetRevivalProjection? value)
    {
        if (value is null)
            return [];
        using var revival = new MemoryStream(2);
        using (var writer = new BinaryWriter(revival, Encoding.UTF8, leaveOpen: true))
            writer.Write(value.UserId);
        using var state = new MemoryStream(8);
        using (var writer = new BinaryWriter(state, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(value.CreatureKey);
            writer.Write(value.StateValue);
        }
        return [Frame(0, 0x006B, revival.ToArray()), Frame(0, 0x0067, state.ToArray())];
    }

    public static IReadOnlyList<byte[]> EncodeDungeonRejoin(SelectCharacterDungeonRejoinProjection? value)
    {
        if (value is null)
            return [];
        var participants = value.ParticipantUserIds ?? throw Missing("DungeonRejoin.ParticipantUserIds");
        using var offer = new MemoryStream(9);
        using (var writer = new BinaryWriter(offer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(value.PartyId);
            writer.Write(0);
            writer.Write((byte)0);
        }
        var frames = new List<byte[]>(participants.Count + 1) { Frame(0, 0x02A7, offer.ToArray()) };
        foreach (var participant in participants)
        {
            using var body = new MemoryStream(2);
            using (var writer = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
                writer.Write(participant);
            frames.Add(Frame(0, 0x02A8, body.ToArray()));
        }
        return frames;
    }

    public static byte[] EncodeExpertStoreClose(ushort ownerUserId) =>
        Frame(0, 0x021C, BitConverter.GetBytes(ownerUserId));

    public static byte[] EncodeTownLeave(ushort userId) =>
        Frame(0, 0x0006, BitConverter.GetBytes(userId));

    private static byte[] EncodePvpIdentityRoster(IReadOnlyList<SelectCharacterPvpIdentityProjection> identities)
    {
        if (identities.Count > ushort.MaxValue)
            throw new InvalidDataException("PvP identity count cannot be represented as u16.");
        var seen = new HashSet<ushort>();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0);
        writer.Write(checked((ushort)identities.Count));
        foreach (var identity in identities)
        {
            ValidatePvpIdentity(identity, seen);
            var body = SelectCharacterStaticPayloadCodec.EncodePvpSubtype0(identity.Result);
            writer.Write(body, 3, body.Length - 3);
        }
        return Frame(0, 0x0002, stream.ToArray());
    }

    private static byte[] EncodeSinglePvpIdentity(SelectCharacterPvpIdentityProjection identity)
    {
        ValidatePvpIdentity(identity, null);
        return Frame(0, 0x0002, SelectCharacterStaticPayloadCodec.EncodePvpSubtype0(identity.Result));
    }

    private static void ValidatePvpIdentity(SelectCharacterPvpIdentityProjection? identity, HashSet<ushort>? seen)
    {
        if (identity is null || identity.UserId == 0)
            throw Missing("Pvp.Identity");
        var body = SelectCharacterStaticPayloadCodec.EncodePvpSubtype0(
            identity.Result ?? throw Missing("Pvp.Identity.Result"));
        if (body.Length < 5 || body[0] != 0 || BitConverter.ToUInt16(body, 1) != 1 ||
            BitConverter.ToUInt16(body, 3) != identity.UserId)
            throw new InvalidDataException("PvP identity is not a matching subtype-0 USERINFO body.");
        if (seen is not null && !seen.Add(identity.UserId))
            throw new InvalidDataException("PvP identity order contains a duplicate user id.");
    }

    private static byte[] EncodePvpRoomSnapshot(IReadOnlyList<SelectCharacterPvpRoomProjection> rooms)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)rooms.Count));
        foreach (var room in rooms)
        {
            var seats = room.Seats ?? throw Missing($"Pvp.Room[{room.RoomId}].Seats");
            if (seats.Count != 8)
                throw new InvalidDataException("PvP room requires exactly eight ordered seats.");
            writer.Write(room.RoomId);
            writer.Write(room.NameType);
            if (room.NameType == 0)
                WriteDstr(writer, room.Name);
            writer.Write(room.RoomState);
            writer.Write(room.ManagerSeat);
            writer.Write(room.MapIndex);
            writer.Write(room.BattleMode);
            foreach (var seat in seats)
            {
                writer.Write(seat.State);
                writer.Write(seat.UserId);
                writer.Write((byte)0);
            }
            writer.Write(room.HasPassword ? (byte)1 : (byte)0);
            writer.Write(room.MatchingType);
        }
        return Frame(0, 0x0029, stream.ToArray());
    }

    private static void WriteAppearance(BinaryWriter writer, SelectCharacterRosterAppearanceProjection value)
    {
        var expansion = value.ExpansionData ?? throw Missing("Roster.Appearance.ExpansionData");
        if (expansion.Length != 4 || value.ExpansionLength != 4)
            throw new InvalidDataException("Roster appearance expansion must be the loaded four-byte value.");
        writer.Write(value.Slot);
        writer.Write(value.DisplayItemId);
        writer.Write(value.ExpansionLength);
        writer.Write(expansion);
        writer.Write(value.State);
        writer.Write(value.LinkItemId);
        writer.Write(value.EnchantValue);
        writer.Write(value.Flag20);
    }

    private static void WriteRosterTail(BinaryWriter writer, uint cloneTitleItemId)
    {
        writer.Write(cloneTitleItemId);
        writer.Write(new byte[4]);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write(new byte[] { 0, 0, 0, 3, 0, 0, 4, 0, 0, 0, 0 });
    }

    private static void WriteDstr(BinaryWriter writer, byte[]? value)
    {
        value ??= [];
        writer.Write(value.Length);
        writer.Write(value);
    }

    private static byte[] Frame(byte command, ushort type, ReadOnlySpan<byte> body) =>
        GameServerFrameEncoder.Encode(command, type, body);

    private static InvalidDataException Missing(string name) =>
        new($"Required supplemental projection '{name}' is unavailable.");
}
