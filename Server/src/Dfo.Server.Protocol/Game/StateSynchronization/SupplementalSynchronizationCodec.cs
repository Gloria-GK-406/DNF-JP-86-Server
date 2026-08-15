using System.Text;
using Dfo.Server.Data.Game;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal sealed record PvpSynchronizationBatch(
    IReadOnlyList<byte[]> DirectFrames,
    IReadOnlyList<byte[]> PublicationFrames);

/// <summary>Pure framing for X-01 through X-18. Callers own applicability, recipients and mutations.</summary>
internal static class SupplementalSynchronizationCodec
{
    public static byte[] EncodeVisibility(ushort userId, byte userStateBits)
    {
        using var stream = new MemoryStream(3);
        using var writer = new BinaryWriter(stream);
        writer.Write(userId);
        writer.Write(userStateBits);
        return Frame(0, 0x0165, stream.ToArray());
    }

    public static byte[] EncodeCloneTitle(uint itemId)
    {
        using var stream = new MemoryStream(5);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(itemId);
        return Frame(1, 0x0239, stream.ToArray());
    }

    public static byte[] EncodeRoster(CharacterRosterEntryState value)
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

    public static byte[] EncodeHonor(uint level, uint experience)
    {
        using var stream = new MemoryStream(8);
        using var writer = new BinaryWriter(stream);
        writer.Write(level);
        writer.Write(experience);
        return Frame(0, 0x0289, stream.ToArray());
    }

    public static byte[] EncodeGrowthCapsule(
        byte level,
        uint totalExperience,
        ushort page0Sp,
        ushort page1Sp,
        ushort page0Tp,
        ushort page1Tp,
        uint pvpVictoryPoints,
        uint growthCapsuleExperience,
        uint honorLevel,
        uint honorExperience)
    {
        using var stream = new MemoryStream(95);
        using var writer = new BinaryWriter(stream);
        writer.Write(level);
        writer.Write(totalExperience);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(page0Sp);
        writer.Write(page1Sp);
        writer.Write(page0Tp);
        writer.Write(page1Tp);
        writer.Write(pvpVictoryPoints);
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
        writer.Write(growthCapsuleExperience);
        writer.Write(honorLevel);
        writer.Write(honorExperience);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        return Frame(0, 0x0025, stream.ToArray());
    }

    public static IReadOnlyList<byte[]> EncodeRejection(
        byte errorCode,
        string message,
        byte noticeMode)
    {
        using var notice = new MemoryStream();
        using var writer = new BinaryWriter(notice);
        writer.Write(noticeMode);
        WriteDstr(writer, Encoding.UTF8.GetBytes(message ?? string.Empty));
        return
        [
            Frame(1, 0x0004, [0, errorCode]),
            Frame(0, 0x01F7, notice.ToArray()),
        ];
    }

    public static PvpSynchronizationBatch EncodePvpLobby(PvpLobbySynchronizationState value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var identities = value.OrderedIdentities ?? throw Missing("Pvp.OrderedIdentities");
        var rooms = value.OrderedRooms ?? throw Missing("Pvp.OrderedRooms");
        var newcomer = value.Newcomer ?? throw Missing("Pvp.Newcomer");
        var roster = EncodePvpIdentityRoster([.. identities, newcomer]);
        var roomSnapshot = EncodePvpRoomSnapshot(rooms);
        var publication = EncodeSinglePvpIdentity(newcomer);
        return new PvpSynchronizationBatch([roster, roomSnapshot], [publication]);
    }

    public static byte[] EncodePvpIdentity(PvpParticipantEntryState value) =>
        EncodeSinglePvpIdentity(value);

    public static byte[] EncodeTrailingEquipmentLocks(
        Dfo.Server.Data.Inventory.CharacterInventoryEntryState characterInventory,
        Dfo.Server.Data.Inventory.AccountInventoryEntryState accountInventory)
    {
        ArgumentNullException.ThrowIfNull(characterInventory);
        ArgumentNullException.ThrowIfNull(accountInventory);
        return Frame(0, 0x00FB, StateSynchronizationPayloadCodec.EncodeEquipmentLocks(
            characterInventory.EquipmentLocks,
            accountInventory.Cargo.EquipmentLocks));
    }

    public static IReadOnlyList<byte[]> EncodeAcceptedQuests(
        IReadOnlyList<AcceptedQuestProgressState>? quests)
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

    public static IReadOnlyList<byte[]> EncodePetRevival(PetRevivalState? value)
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

    public static IReadOnlyList<byte[]> EncodeDungeonRejoin(DungeonRejoinSynchronizationState? value)
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

    private static byte[] EncodePvpIdentityRoster(IReadOnlyList<PvpParticipantEntryState> identities)
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
            var body = EncodePvpSubtype0(identity);
            writer.Write(body, 3, body.Length - 3);
        }
        return Frame(0, 0x0002, stream.ToArray());
    }

    private static byte[] EncodeSinglePvpIdentity(PvpParticipantEntryState identity)
    {
        ValidatePvpIdentity(identity, null);
        return Frame(0, 0x0002, EncodePvpSubtype0(identity));
    }

    private static ushort ValidatePvpIdentity(
        PvpParticipantEntryState? identity,
        HashSet<ushort>? seen)
    {
        if (identity?.Character?.Identity is null)
            throw Missing("Pvp.Identity");
        var userId = checked((ushort)identity.Character.Identity.CharacterId.Value);
        if (userId == 0)
            throw Missing("Pvp.Identity.CharacterId");
        var body = EncodePvpSubtype0(identity);
        if (body.Length < 5 || body[0] != 0 || BitConverter.ToUInt16(body, 1) != 1 ||
            BitConverter.ToUInt16(body, 3) != userId)
            throw new InvalidDataException("PvP identity is not a matching subtype-0 USERINFO body.");
        if (seen is not null && !seen.Add(userId))
            throw new InvalidDataException("PvP identity order contains a duplicate user id.");
        return userId;
    }

    private static byte[] EncodePvpSubtype0(PvpParticipantEntryState participant) =>
        StateSynchronizationPayloadCodec.EncodeCurrentUserState(
            participant.Character,
            participant.Public,
            participant.Equipment,
            participant.Pets);

    private static byte[] EncodePvpRoomSnapshot(IReadOnlyList<PvpRoomSnapshotState> rooms)
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

    private static void WriteAppearance(BinaryWriter writer, CharacterRosterAppearanceState value)
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
        new($"Required supplemental synchronization value '{name}' is unavailable.");
}
