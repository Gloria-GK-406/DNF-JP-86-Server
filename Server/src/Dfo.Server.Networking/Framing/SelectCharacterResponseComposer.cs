using Dfo.Server.Data.Game;

namespace Dfo.Server.Networking.Framing;

internal static class SelectCharacterResponseComposer
{
    public static IReadOnlyList<byte[]> Encode(SelectCharacterResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var frames = new List<byte[]>(capacity: 59);
        foreach (var descriptor in SelectCharacterStaticSequence.Descriptors)
        {
            if (SelectCharacterStaticSequence.IsApplicable(descriptor, result))
                frames.Add(EncodeFrame(descriptor, result));

            if (descriptor.Row == 11 && SelectCharacterStaticSequence.HasDarkKnightCombo(result))
            {
                frames.Add(GameServerFrameEncoder.Encode(
                    0,
                    0x01C0,
                    SelectCharacterStaticPayloadCodec.EncodeNotification(result, 0x01C0)));
            }
        }
        return frames;
    }

    private static byte[] EncodeFrame(
        SelectCharacterStaticPacketDescriptor descriptor,
        SelectCharacterResult result)
    {
        byte[] body;
        if (descriptor.Row == 1)
            body = SelectCharacterStaticPayloadCodec.EncodeAcknowledgement(result);
        else if (descriptor.InventoryContainer is { } inventoryContainer)
            body = SelectCharacterStaticPayloadCodec.EncodeInventory(result, inventoryContainer);
        else if (descriptor.Row == 41)
            body = SelectCharacterStaticPayloadCodec.EncodePremiumService(result);
        else
            body = SelectCharacterStaticPayloadCodec.EncodeNotification(
                result, descriptor.Type, descriptor.Occurrence);
        return GameServerFrameEncoder.Encode(descriptor.Command, descriptor.Type, body);
    }
}
