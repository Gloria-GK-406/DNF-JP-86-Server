namespace Dfo.Server.Protocol.Game.DisjointItem;

internal sealed class DisjointItemResponseEncoder : GameCmdMessageEncoder<DisjointItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DisjointItemResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(DisjointItemResponse message)
    {
        if (message.TargetSlot is not { } targetSlot)
            return [0, message.ErrorCode];
        var materials = message.Materials ?? [];
        if (materials.Count > byte.MaxValue)
            throw new InvalidDataException("DISJOINT_ITEM cannot encode more than 255 material entries.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(checked((short)targetSlot.Index));
        writer.Write((byte)targetSlot.Space);
        writer.Write((byte)materials.Count);
        foreach (var material in materials)
        {
            writer.Write(checked((short)material.Slot.Index));
            writer.Write(material.ItemId);
            writer.Write(material.Count);
        }
        return stream.ToArray();
    }
}
