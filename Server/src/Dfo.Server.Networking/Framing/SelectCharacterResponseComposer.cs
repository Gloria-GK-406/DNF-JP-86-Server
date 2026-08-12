using Dfo.Server.Data.Game;

namespace Dfo.Server.Networking.Framing;

internal static class SelectCharacterResponseComposer
{
    public static IReadOnlyList<byte[]> Encode(SelectCharacterResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // The legacy path sends a long, conditionally extended initialization
        // sequence. Keep packet ordering here, outside the business result.
        // Only the selection ACK has been migrated so far; subsequent domain
        // packet projections are intentionally not represented as empty data.
        return
        [
            GameServerFrameEncoder.Encode(
                GameProtocol.Command,
                GameProtocol.SelectCharacterType,
                EncodeAcknowledgement(result))
        ];
    }

    private static byte[] EncodeAcknowledgement(SelectCharacterResult result)
    {
        var character = result.Character;
        var characterId = character.Identity.CharacterId.Value;
        if (characterId is < 0 or > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"Character ID '{characterId}' cannot be represented by the observed SELECT_CHARACTER ACK.");
        }

        using var stream = new MemoryStream(capacity: 260);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(0); // Account registration time is not present in the migrated account model yet.
        writer.Write(checked((int)character.CreatedAt.ToUnixTimeSeconds()));
        writer.Write(checked((ushort)characterId));
        writer.Write((ushort)0); // Observed leading fatigue field; semantics are not capture-confirmed.
        writer.Write(character.Fatigue.Total);
        writer.Write(character.Fatigue.Used);
        writer.Write((byte)0); // Premium projection belongs to AccountEntryState and is not migrated yet.
        writer.Write(0); // Cera projection belongs to AccountEntryState and is not migrated yet.

        for (var index = 0; index < 30; index++)
        {
            writer.Write(ushort.MaxValue);
            writer.Write(0u);
        }

        for (var index = 0; index < 4; index++)
        {
            writer.Write(0);
        }

        writer.Write(checked((byte)character.Identity.SlotIndex));
        writer.Write((byte)0);
        if (character.Progression.Level <= 1)
        {
            writer.Write((byte)0);
        }
        else
        {
            writer.Write((byte)1);
            writer.Write((byte)0x4E);
        }
        writer.Write((ushort)0); // Fatigue battery.
        writer.Write((ushort)0); // Fatigue grown-up buff.
        writer.Write((byte)0); // Trade punishment flag.
        writer.Write((ushort)0); // Observed 86JP extension.
        writer.Write(new byte[8]);
        writer.Write((byte)0); // Tutorial skippable.
        writer.Write((ushort)0);
        writer.Write(new byte[22]);
        return stream.ToArray();
    }
}
