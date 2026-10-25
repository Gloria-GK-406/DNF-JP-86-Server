using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Dispatching;
namespace Dfo.Server.Protocol.Game.VendingMachine;

public sealed record VendingMachineRequest(GameMessageMetadata Metadata, uint MachineId, uint GroupId, short Slot) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => ClientMessageType.GameVendingMachine;
}
internal sealed class VendingMachineDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 10) throw new InvalidDataException("Vending request must contain exactly 10 bytes.");
        return new VendingMachineRequest(metadata, BinaryPrimitives.ReadUInt32LittleEndian(body),
            BinaryPrimitives.ReadUInt32LittleEndian(body[4..]), BinaryPrimitives.ReadInt16LittleEndian(body[8..]));
    }
}
public sealed record VendingMachineResponse(VendingMachineResult? Result, byte Error = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameVendingMachine;
}
internal sealed class VendingMachineEncoder : GameCmdMessageEncoder<VendingMachineResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(VendingMachineResponse message) => OnePayload(0x00D7, Encode(message));
    internal static byte[] Encode(VendingMachineResponse message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Result is null ? (byte)0 : (byte)1);
        if (message.Result is not { } result) { writer.Write(message.Error); return stream.ToArray(); }
        writer.Write(checked((short)OpeningWireSlots.RewardIndex(result.SourceSlot))); writer.Write(result.Remaining);
        writer.Write(result.Reward.ItemId); writer.Write(result.Reward.Count);
        writer.Write(-1); writer.Write(0); // No bonus pool in the captured ordinary machine.
        var slots = result.Receipt.Rewards.Where(r => r.Slot is not null).Select(r => r.Slot!.Value).ToHashSet();
        var entries = result.Receipt.Updates.Where(update => slots.Contains(update.Slot)
            && update.Core.ItemId == result.Reward.ItemId)
            .Select(update => (Wire: OpeningWireSlots.Project(update.Slot, new InventorySlotMappingRuleService()),
                Item: new InventoryItemState(update.Identity,
                    OpeningWireSlots.RewardIndex(update.Slot), update.Core.ToCore()))).ToArray();
        if (entries.Length != slots.Count || entries.Any(e => e.Wire.Space != InventoryContainerType.Main))
            throw new InvalidDataException("Vending receipt does not match the native reward list.");
        writer.Write(checked((ushort)entries.Length));
        foreach (var entry in entries)
        {
            writer.Write((byte)entry.Wire.Space);
            GameInventoryItemEntry.Write(writer, entry.Item, result.Receipt.PreparedAt, entry.Wire.Space);
        }
        return stream.ToArray();
    }
}
public sealed class VendingMachineHandler(ICommandBus commands) : ClientMessageHandler<VendingMachineRequest>, IProtocolCommandMessageHandler<GameVendingMachineCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, VendingMachineRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameVendingMachineCommand(context, message), ct).ConfigureAwait(false);
}
