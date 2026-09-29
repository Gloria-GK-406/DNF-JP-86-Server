namespace Dfo.Server.Protocol.Game.ReinforceItem;

internal sealed class ReinforceItemResponseEncoder : GameCmdMessageEncoder<ReinforceItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ReinforceItemResponse message) =>
        OnePayload(GameProtocol.ReinforceItemType, EncodeBody(message));

    internal static byte[] EncodeBody(ReinforceItemResponse message)
    {
        if (!message.Success)
            return [0, message.ErrorCode];
        if (message.Method != 0)
            throw new InvalidDataException("Ordinary reinforcement ACK must use method zero.");
        var rewards = message.DestroyRewards ?? [];
        if (message.Outcome != Dfo.Server.Data.Inventory.EquipmentReinforcementOutcome.Destroyed
            && rewards.Count != 0 || rewards.Count > byte.MaxValue)
            throw new InvalidDataException("UPGRADE_ITEM destroy reward count is invalid.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(message.Method);
        writer.Write(message.MaterialSlot);
        writer.Write(message.MaterialRemaining);
        writer.Write(message.OptionalTicketSlot);
        writer.Write((byte)0);
        writer.Write(message.OldLevel);
        writer.Write((byte)message.Outcome);
        writer.Write(message.NewLevel);
        writer.Write((byte)0);
        writer.Write(message.TargetSlot);
        writer.Write(message.OptionalTicketSlot);
        if (message.Outcome == Dfo.Server.Data.Inventory.EquipmentReinforcementOutcome.Destroyed)
        {
            writer.Write((byte)rewards.Count);
            foreach (var reward in rewards)
            {
                writer.Write(checked((short)reward.Slot.Index));
                writer.Write(reward.ItemId);
                writer.Write(reward.Count);
            }
        }
        return stream.ToArray();
    }
}
