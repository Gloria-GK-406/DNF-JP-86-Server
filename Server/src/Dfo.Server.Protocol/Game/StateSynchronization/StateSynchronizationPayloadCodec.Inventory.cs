using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal static partial class StateSynchronizationPayloadCodec
{
    /// <param name="townId">
    /// The town this entry places the character in -- the same value NOTI 0x0017 carries. It is a
    /// lookup key on the client, not a display field: the byte is handed to a keyed town manager
    /// and the object that comes back is dereferenced without a null check, so a town the content
    /// does not declare terminates the client. See protocol/game/client-to-server/0x0004.md.
    /// </param>
    public static byte[] EncodeEntryAccepted(
        ActiveCharacterState character,
        AccountEntryState account,
        CharacterQuestEntryState quests,
        byte townId,
        DateTimeOffset preparedAt)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(quests);
        var characterId = checked((ushort)character.Identity.CharacterId.Value);
        // A backstop, not the guarantee. Zero is the value the old defect produced and the one the
        // content can never declare; the towns that do exist are not a contiguous range, so only
        // EntryPlacementRuleService's lookup can tell a real town from a plausible number, and it
        // already refuses to resolve a spawn in a town the archive does not define.
        if (townId == 0)
        {
            throw new InvalidDataException(
                "Entry town cannot be zero: the client resolves this byte to a town object and "
                + "dereferences the result unchecked.");
        }

        // Keep expiry history for domain reconciliation, but never start a client
        // contract countdown at zero. The entry list contains active services only.
        var now = preparedAt.ToUnixTimeSeconds();
        var activePremiums = account.Entitlements.Premiums
            .Where(premium => premium.ExpiresAtUnixSeconds > now && premium.PremiumType is not (>= 580 and <= 587)).ToArray();
        if (activePremiums.Length > byte.MaxValue)
            throw new InvalidDataException("Premium count cannot be represented as u8.");

        using var stream = new MemoryStream(320);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(0); // observed account-registration compatibility field
        writer.Write(checked((int)character.CreatedAt.ToUnixTimeSeconds()));
        writer.Write(characterId);
        writer.Write(character.Fatigue.Used); // A21 displays Total - Used (2026-09-20 verified).
        writer.Write(character.Fatigue.Total);
        writer.Write(checked((ushort)Math.Min(ushort.MaxValue, character.Fatigue.ConsumedToday)));
        writer.Write(checked((byte)activePremiums.Length));
        foreach (var premium in activePremiums.OrderBy(value => value.PremiumType))
        {
            // The observed ACK layout carries only the low byte. Legacy loading
            // projected the stored integer to byte before this body was built.
            writer.Write(unchecked((byte)premium.PremiumType));
            writer.Write(premium.ExpiresAtUnixSeconds - now);
        }
        writer.Write(account.Wallet.Cera);

        var slots = quests.ActiveQuests.ToDictionary(value => value.Slot);
        for (short slot = 0; slot < 30; slot++)
        {
            if (slots.TryGetValue(slot, out var quest))
            {
                writer.Write(checked((ushort)quest.QuestId));
                writer.Write(checked((uint)quest.TriggerValue));
            }
            else
            {
                writer.Write(ushort.MaxValue);
                writer.Write(0u);
            }
        }
        for (var index = 0; index < 4; index++)
            writer.Write(index < quests.NotificationQuestIds.Count
                ? quests.NotificationQuestIds[index]
                : 0);

        // Offset 218. The reference server fills this from the character's stored town while calling
        // the field AckCharSlotIndex; the name is its own misnomer and the value is what matters.
        writer.Write(townId);
        writer.Write((byte)0);
        if (character.Progression.Level <= 1)
            writer.Write((byte)0);
        else
        {
            writer.Write((byte)1);
            writer.Write((byte)0x4E);
        }
        writer.Write((ushort)0); // observed fatigue-battery empty state
        writer.Write((ushort)0); // observed grown-up-buff empty state
        writer.Write((byte)0); // observed no trade punishment
        writer.Write((ushort)0); // observed 86JP compatibility field
        writer.Write(new byte[8]);
        writer.Write((byte)0); // observed tutorial-skippable state
        writer.Write((ushort)0);
        writer.Write(new byte[22]);
        return stream.ToArray();
    }

    public static byte[] EncodeInventorySnapshot(InventoryContainerState container,
        IReadOnlyList<InventoryRuntimeValueState> characterVirtualResources,
        IReadOnlyList<InventoryRuntimeValueState> accountVirtualResources,
        ushort accountSelectionKey, int accountCargoGold, DateTimeOffset preparedAt) =>
        GameInventorySnapshotCodec.EncodeInventorySnapshot(container, characterVirtualResources,
            accountVirtualResources, accountSelectionKey, accountCargoGold, preparedAt);

    private static InvalidDataException Missing(string name) =>
        new($"Required inventory synchronization value '{name}' is unavailable.");
}
