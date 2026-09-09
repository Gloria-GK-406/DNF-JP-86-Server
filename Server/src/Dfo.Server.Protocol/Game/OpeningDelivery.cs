using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

public sealed record OpeningCompleted(OpeningInventoryReceipt Receipt,
    IReadOnlyList<IGameCmdMessage> Responses) : INotification<OpeningCompleted>;

public sealed class OpeningCompletedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<OpeningCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        OpeningCompleted notification, CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session) return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        // Native box acknowledgements decrement the two local piles. Absolute inventory deltas must follow them.
        var messages = new List<IServerNetworkMessage>(notification.Responses);
        messages.AddRange(GameInventoryDelivery.Notifications(receipt.Updates));
        foreach (var slot in receipt.ClearedMainSlots)
            messages.Add(GameInventoryDelivery.ClearedSlotNotification(InventoryContainerType.Main, slot, receipt.PreparedAt));
        if (!receipt.ChangedPremiums.IsDefaultOrEmpty)
            foreach (var premium in receipt.ChangedPremiums)
                messages.Add(GameInstanceDelivery.Premium(premium, receipt.PreparedAt));
        if (receipt.CreaturesChanged) messages.Add(GameInstanceDelivery.Creatures(receipt.Pets));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}

/// <summary>The same obtained-items popup is used by several opening protocols.</summary>
public sealed record OpeningRewardPopup(int SourceSlot, ImmutableArray<OpeningRewardDisplay> Rewards) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePackageOpening;
}
internal sealed class OpeningRewardPopupEncoder : GameCmdMessageEncoder<OpeningRewardPopup>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(OpeningRewardPopup message) =>
        OnePayload(0x00A0, OpeningPayloads.Popup(message.SourceSlot, message.Rewards));
}

internal static class OpeningPayloads
{
    public static byte Error(OpeningRejection rejection) => rejection switch
    {
        OpeningRejection.NoRoom => 0x7E,
        OpeningRejection.MissingMaterial => 0x16,
        _ => 2,
    };

    public static byte[] Popup(int sourceSlot, ImmutableArray<OpeningRewardDisplay> rewards)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1); writer.Write(checked((short)sourceSlot));
        writer.Write(0); writer.Write(0);
        writer.Write(checked((ushort)rewards.Length));
        foreach (var reward in rewards) { writer.Write(reward.ItemId); writer.Write(reward.Count); }
        return stream.ToArray();
    }

    public static byte[] Box(MagicBoxOpeningInput input, MagicBoxOpeningResult result, bool batch)
    {
        if (result.Receipt is null) return [0, Error(result.Rejection)];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1); writer.Write(input.ListType); writer.Write(result.Bonus.IsEmpty ? (byte)0 : (byte)1);
        if (batch) writer.Write(checked((ushort)input.Count));
        writer.Write(checked((short)input.SourceSlot)); writer.Write(checked((short)input.MaterialSlot));
        if (batch)
        {
            Rows(writer, result.Primary);
            writer.Write((ushort)0);
            Rows(writer, result.Bonus);
        }
        else Rows(writer, result.Primary.AddRange(result.Bonus));
        return stream.ToArray();
    }

    private static void Rows(BinaryWriter writer, ImmutableArray<OpeningRewardDisplay> rewards)
    {
        writer.Write(checked((ushort)rewards.Length));
        foreach (var reward in rewards)
        {
            writer.Write(checked((short)reward.Slot)); writer.Write(reward.ItemId); writer.Write(reward.Count);
            writer.Write(reward.Durability); writer.Write(reward.Ability); writer.Write(reward.Expiration);
            writer.Write(new byte[23]);
        }
    }
}
