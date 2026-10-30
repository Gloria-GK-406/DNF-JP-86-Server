using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

/// <summary>Refreshes the cargo window after either a direct or a cash-shop capacity upgrade.</summary>
internal sealed record CargoCapacityUpgradeMessage(InventoryContainerType ContainerType) : IGameCmdMessage
{
    internal ushort WireType => ContainerType switch
    {
        InventoryContainerType.PersonalCargo => 0x0198,
        InventoryContainerType.AccountCargo => 0x0132,
        _ => throw new InvalidDataException("Only cargo has a capacity-upgrade acknowledgement."),
    };
    public ServerMessageType MessageType => (ServerMessageType)(0x00010000 | WireType);
}
internal sealed class CargoCapacityUpgradeEncoder : GameCmdMessageEncoder<CargoCapacityUpgradeMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CargoCapacityUpgradeMessage message) =>
        OnePayload(message.WireType, new byte[] { 1 });
}

internal sealed record InventoryFullSnapshotMessage(InventoryContainerState Container,
    CharacterInventoryEntryState Character, AccountInventoryEntryState Account, DateTimeOffset PreparedAt)
    : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
    internal ushort Type => 0x000D;
}
internal sealed class InventoryFullSnapshotEncoder : GameNotiMessageEncoder<InventoryFullSnapshotMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(InventoryFullSnapshotMessage message) =>
        OnePayload(message.Type, GameInventorySnapshotCodec.EncodeInventorySnapshot(message.Container,
            message.Character.VirtualResources, message.Account.VirtualResources,
            message.Account.Cargo.SelectionKey, message.Account.Cargo.Value32, message.PreparedAt));
}
internal sealed record InventoryLocksMessage(IReadOnlyList<InventoryEquipmentLockState> CharacterLocks,
    IReadOnlyList<InventoryEquipmentLockState> AccountLocks) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
    internal ushort Type => 0x00FB;
}
internal sealed class InventoryLocksEncoder : GameNotiMessageEncoder<InventoryLocksMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(InventoryLocksMessage message) =>
        OnePayload(message.Type, GameInventorySnapshotCodec.EncodeEquipmentLocks(message.CharacterLocks, message.AccountLocks));
}
