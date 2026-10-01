using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

public sealed record OpeningCompleted(OpeningInventoryReceipt Receipt,
    IReadOnlyList<IGameCmdMessage> Responses, int? GoldAfter = null,
    IReadOnlyList<IServerNetworkMessage>? FollowingMessages = null,
    IReadOnlyList<InventoryRuntimeValueState>? AccountMaterials = null) : INotification<OpeningCompleted>;

public sealed class OpeningCompletedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<OpeningCompleted>
{
    private static readonly IInventorySlotMappingRuleService SlotMappings = new InventorySlotMappingRuleService();
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        OpeningCompleted notification, CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session) return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        // Native box acknowledgements decrement the two local piles. Absolute inventory deltas must follow them.
        var messages = new List<IServerNetworkMessage>(notification.Responses);
        var updates = receipt.Updates.GroupBy(update => OpeningWireSlots.Project(update.Slot, SlotMappings).Space)
            .Select(group => new CommittedInventoryUpdateState(group.Key,
                group.Select(update => new InventoryItemState(update.Identity,
                    OpeningWireSlots.Project(update.Slot, SlotMappings).Index, update.Core)).ToArray(), receipt.PreparedAt)).ToArray();
        messages.AddRange(GameInventoryDelivery.Notifications(updates));
        foreach (var slot in receipt.Cleared)
        {
            var wire = OpeningWireSlots.Project(slot, SlotMappings);
            messages.Add(GameInventoryDelivery.ClearedSlotNotification(wire.Space, wire.Index, receipt.PreparedAt));
        }
        if (!receipt.ChangedPremiums.IsDefaultOrEmpty)
            foreach (var premium in receipt.ChangedPremiums)
                messages.Add(GameInstanceDelivery.Premium(premium, receipt.PreparedAt));
        if (receipt.CreaturesChanged) messages.Add(GameInstanceDelivery.Creatures(receipt.Pets));
        if (notification.GoldAfter is { } gold) messages.Add(GameInventoryDelivery.CarriedGoldNotification(gold, receipt.PreparedAt));
        if (notification.AccountMaterials is { } resources)
            messages.AddRange(AccountMaterialNotifications.Compose(resources, receipt.PreparedAt));
        if (notification.FollowingMessages is { } following) messages.AddRange(following);
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
            writer.Write(checked((short)OpeningWireSlots.RewardIndex(reward.Slot))); writer.Write(reward.ItemId); writer.Write(reward.Count);
            writer.Write(reward.Durability); writer.Write(reward.Ability); writer.Write(reward.Expiration);
            writer.Write(new byte[23]);
        }
    }
}

internal static class OpeningWireSlots
{
    private static readonly IInventorySlotMappingRuleService Mappings = new InventorySlotMappingRuleService();

    public static BusinessSlot FromMain(int slot)
    {
        try
        {
            var business = Mappings.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, slot));
            if (BusinessInventoryOwnership.StoreOf(business.Space) != BusinessInventoryStoreKind.Item)
                throw new OpeningRejectedException(OpeningRejection.InvalidRequest,
                    $"Opening source slot {slot} is outside the item store.");
            return business;
        }
        catch (InventorySlotMappingException)
        { throw new OpeningRejectedException(OpeningRejection.InvalidRequest, $"Invalid opening source slot {slot}."); }
    }

    public static BusinessMagicBoxOpeningInput Business(MagicBoxOpeningInput input) =>
        new(input.ListType, FromMain(input.SourceSlot), input.ExpectedItemId,
            input.MaterialSlot < 0 ? null : FromMain(input.MaterialSlot), input.ExpectedMaterialId, input.Count);

    public static BusinessPackageOpeningInput Business(PackageOpeningInput input) =>
        new(FromMain(input.SourceSlot), input.SelectedItems);

    public static BusinessAvatarPackageOpeningInput Business(AvatarPackageOpeningInput input) =>
        new(FromMain(input.SourceSlot), input.Choices);

    public static GameSlot Project(BusinessSlot slot, IInventorySlotMappingRuleService mappings) =>
        slot.Space == BusinessInventorySpace.VirtualResource
            ? new GameSlot(InventoryContainerType.Main, slot.Index)
            : mappings.ToGameSlot(slot);

    public static int RewardIndex(BusinessSlot? slot) => slot is { } address
        ? Project(address, Mappings).Index : -1;
}
