using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.SecretShop;

public sealed record SecretShopBuyRequest(GameMessageMetadata Metadata, int ItemId, int Count) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameSecretShopBuy;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Quests;
}

internal sealed record SecretShopWindowReport(bool Open) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSecretShopWindow;
}

internal sealed record SecretShopBuyResponse(SecretShopPurchaseResult Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSecretShopBuy;
}

internal sealed record SecretShopItemsNotification(IReadOnlyList<DungeonSecretShopItem> Items) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSecretShopItems;
}

internal sealed class SecretShopBuyDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 8)
            throw new InvalidDataException("A secret shop purchase requires eight body bytes.");
        return new SecretShopBuyRequest(metadata,
            BinaryPrimitives.ReadInt32LittleEndian(body), BinaryPrimitives.ReadInt32LittleEndian(body[4..]));
    }
}

internal sealed class SecretShopWindowDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 1 || body[0] > 1)
            throw new InvalidDataException("A secret shop window report requires one flag byte.");
        return new SecretShopWindowReport(body[0] == 1);
    }
}

internal sealed class SecretShopItemsEncoder : GameNotiMessageEncoder<SecretShopItemsNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SecretShopItemsNotification message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(SecretShopItemsNotification message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Items.Count);
        foreach (var item in message.Items)
        {
            var itemCurrency = item.CostItemId != 0;
            writer.Write(item.ItemId);
            writer.Write(itemCurrency ? (byte)1 : (byte)0);
            writer.Write(itemCurrency ? 0 : item.UnitPrice);
            writer.Write(item.RemainingCount);
            writer.Write(item.CostItemId);
            writer.Write(itemCurrency ? item.UnitPrice : 0);
            writer.Write((byte)0);
        }
        return stream.ToArray();
    }
}

internal sealed class SecretShopBuyEncoder : GameCmdMessageEncoder<SecretShopBuyResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SecretShopBuyResponse message) =>
        OnePayload(message, EncodeBody(message.Result));

    internal static byte[] EncodeBody(SecretShopPurchaseResult result)
    {
        if (result.Receipt is not { } receipt)
        {
            var error = result.Rejection switch
            {
                NpcShopRejection.InsufficientGold => (byte)0x0A,
                NpcShopRejection.ItemNotSold => (byte)0x11,
                _ => (byte)0x04,
            };
            return [0, error];
        }

        var grant = receipt.GrantedSlots[0];
        var wire = A21InventoryLayout.ToGameSlot(grant.Slot);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(receipt.Gold);
        writer.Write(checked((ushort)wire.Index));
        writer.Write(receipt.ItemId);
        writer.Write(grant.Core.IsPile ? grant.Core.Count : grant.Core.InstanceValue);
        writer.Write(grant.Core.Attribute);
        writer.Write(grant.Core.Durability);
        writer.Write(result.CostItemId > 0 ? result.CostItemId : -1);
        writer.Write(result.CostItemRemaining);
        writer.Write(result.RemainingStock);
        return stream.ToArray();
    }
}
