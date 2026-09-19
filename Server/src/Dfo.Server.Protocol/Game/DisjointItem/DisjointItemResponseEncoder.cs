namespace Dfo.Server.Protocol.Game.DisjointItem;

internal sealed class DisjointItemResponseEncoder : GameCmdMessageEncoder<DisjointItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DisjointItemResponse message) =>
        OnePayload(GameProtocol.DisjointItemType, EncodeBody(message));

    internal static byte[] EncodeBody(DisjointItemResponse message)
    {
        if (message.Receipt is not { } receipt)
            return [0, message.ErrorCode];
        if (receipt.Materials.Count > byte.MaxValue)
            throw new InvalidDataException("DISJOINT_ITEM cannot encode more than 255 material entries.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(receipt.TargetSlotIndex);
        writer.Write((byte)receipt.ItemSpace);
        writer.Write((byte)receipt.Materials.Count);
        foreach (var material in receipt.Materials)
        {
            writer.Write(material.SlotIndex);
            writer.Write(material.ItemId);
            writer.Write(material.Count);
        }
        return stream.ToArray();
    }
}
