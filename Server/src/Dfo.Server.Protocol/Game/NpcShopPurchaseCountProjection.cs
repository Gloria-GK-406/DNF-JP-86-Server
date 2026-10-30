using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

/// <summary>A21's absolute shop counters, shared by a count query and a committed purchase.</summary>
public sealed record ShopPurchaseCountResponse(NpcShopPurchaseCountsResult Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameShopPurchaseCount;
}

internal sealed class ShopPurchaseCountEncoder : GameCmdMessageEncoder<ShopPurchaseCountResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ShopPurchaseCountResponse message)
    {
        if (message.Result.Rejection != NpcShopRejection.None)
            return OnePayload(message, new byte[] { 0, 0 });
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(message.Result.Counts.Count);
        foreach (var count in message.Result.Counts)
        {
            writer.Write(count.ItemId);
            writer.Write(count.Count);
        }
        writer.Write(0); // The second counted list remains empty; its gameplay meaning is unknown.
        return OnePayload(message, stream.ToArray());
    }
}
