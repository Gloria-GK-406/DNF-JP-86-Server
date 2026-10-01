using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

internal sealed record InventoryFullSnapshotMessage(InventoryContainerState Container,
    CharacterInventoryEntryState Character, AccountInventoryEntryState Account, DateTimeOffset PreparedAt)
    : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
}
internal sealed class InventoryFullSnapshotEncoder : GameNotiMessageEncoder<InventoryFullSnapshotMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(InventoryFullSnapshotMessage message) =>
        OnePayload(0x000D, GameInventorySnapshotCodec.EncodeInventorySnapshot(message.Container,
            message.Character.VirtualResources, message.Account.VirtualResources,
            message.Account.Cargo.SelectionKey, message.Account.Cargo.Value32, message.PreparedAt));
}
internal sealed record InventoryLocksMessage(IReadOnlyList<InventoryEquipmentLockState> CharacterLocks,
    IReadOnlyList<InventoryEquipmentLockState> AccountLocks) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
}
internal sealed class InventoryLocksEncoder : GameNotiMessageEncoder<InventoryLocksMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(InventoryLocksMessage message) =>
        OnePayload(0x00FB, GameInventorySnapshotCodec.EncodeEquipmentLocks(message.CharacterLocks, message.AccountLocks));
}
