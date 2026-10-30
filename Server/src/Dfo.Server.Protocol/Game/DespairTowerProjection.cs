using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

internal sealed record DespairClearRewardNotification(DespairTowerSettlement Settlement) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDespairClearReward;
}
/// <param name="Appearance">The character's already-resolved worn appearance parts.</param>
/// <param name="Equipment">Worn equipment; only the creature in slot 25 is read from it.</param>
internal sealed record DespairApcInfoNotification(byte Floor, ActiveCharacterState Character,
    IReadOnlyList<Dfo.Server.Data.Game.CharacterRosterAppearanceState> Appearance,
    IReadOnlyList<InventoryItemState> Equipment) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDespairApcInfo;
}
internal static class DespairTowerPayloadCodec
{
    public static byte[] Reward(DespairTowerSettlement settlement)
    {
        if (settlement.Rewards.Length > 10) throw new InvalidDataException("Despair reward panel has ten positions.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(settlement.ClearTimeMilliseconds);
        writer.Write(checked((ushort)settlement.Floor));
        writer.Write((byte)10);
        for (var i = 0; i < 10; i++)
        {
            writer.Write(i < settlement.Rewards.Length ? settlement.Rewards[i].ItemId : -1);
            writer.Write(i < settlement.Rewards.Length ? settlement.Rewards[i].Count : 0);
        }
        return stream.ToArray();
    }
    public static byte[] Apc(DespairApcInfoNotification message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Floor);
        writer.Write(message.Character.Identity.Name.Length);
        writer.Write(message.Character.Identity.Name);
        writer.Write(message.Character.Progression.Level);
        writer.Write(message.Character.Identity.Job);
        writer.Write(message.Character.Identity.GrowType);
        writer.Write(0); // empty guild Dstr
        writer.Write(0); // guild id
        var appearances = message.Appearance;
        for (var i = 0; i < 22; i++)
        {
            var wornSlot = i < 10 ? i : i == 10 ? 12 : -1;
            writer.Write(appearances.FirstOrDefault(x => x.Slot == wornSlot)?.DisplayItemId ?? 0);
        }
        var creature = message.Equipment.FirstOrDefault(x => x.SlotIndex == 25 && x.Core.ItemKind == 5);
        byte[] name = []; // inventory projection has no pet nickname; empty Dstr is valid.
        writer.Write(name.Length);
        writer.Write(name);
        writer.Write(checked((uint)(creature?.Core.ItemId ?? 0)));
        return stream.ToArray();
    }
}
internal sealed class DespairClearRewardEncoder : GameNotiMessageEncoder<DespairClearRewardNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DespairClearRewardNotification message) =>
        OnePayload(GameProtocol.DespairClearRewardType, DespairTowerPayloadCodec.Reward(message.Settlement));
}
internal sealed class DespairApcInfoEncoder : GameNotiMessageEncoder<DespairApcInfoNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DespairApcInfoNotification message) =>
        OnePayload(GameProtocol.DespairApcInfoType, DespairTowerPayloadCodec.Apc(message));
}
internal static class DespairTowerProjection
{
    public static IEnumerable<IGameServerMessage> Rewards(DespairTowerSettlement settlement, DateTimeOffset preparedAt,
        Dfo.Server.Abstractions.Inventory.IInventorySlotProjectionDomainService projection)
    {
        foreach (var update in settlement.InventoryUpdates)
        {
            var slot = projection.Project(update.Slot);
            yield return new InventoryDeltaMessage(slot.Space,
                [new InventoryItemState(null, slot.Index, update.Item.ToCore())], preparedAt);
        }
        yield return new DespairClearRewardNotification(settlement);
    }
}
