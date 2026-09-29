using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.VendingMachine;

public sealed record VendingMachineRequest(GameMessageMetadata Metadata, uint MachineId, uint GroupId, short Slot) : IClientNetworkMessage
{
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
        writer.Write(checked((short)result.SourceSlot)); writer.Write(result.Remaining);
        writer.Write(result.Reward.ItemId); writer.Write(result.Reward.Count);
        writer.Write(-1); writer.Write(0); // No bonus pool in the captured ordinary machine.
        var slots = result.Receipt.Rewards.Select(r => r.Slot).ToHashSet();
        var entries = result.Receipt.Updates.SelectMany(update => update.Items
            .Where(item => slots.Contains(item.SlotIndex) && item.Core.ItemId == result.Reward.ItemId)
            .Select(item => (update.ContainerType, Item: item))).ToArray();
        if (entries.Length != slots.Count || entries.Any(e => e.ContainerType != InventoryContainerType.Main))
            throw new InvalidDataException("Vending receipt does not match the native reward list.");
        writer.Write(checked((ushort)entries.Length));
        foreach (var entry in entries)
        {
            writer.Write((byte)entry.ContainerType);
            GameInventoryItemEntry.Write(writer, entry.Item, result.Receipt.PreparedAt, entry.ContainerType);
        }
        return stream.ToArray();
    }
}
public sealed record VendingMachineCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    int MachineId, int GroupId, int Slot, ushort Sequence) : ICommand<VendingMachineResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class VendingMachineCommandHandler(IVendingMachineDomainService vending) : ICommandHandler<VendingMachineCommand, VendingMachineResult>
{
    public ValueTask<VendingMachineResult> HandleAsync(CommandExecutionContext context, VendingMachineCommand command, CancellationToken token) =>
        vending.UseAsync(command.Session, command.MachineId, command.GroupId, command.Slot, command.Sequence, token);
}
public sealed class VendingMachineHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    SelectedSessionTransitionGate transitions, ICommandBus commands, INotificationBus notifications, IGameMessageSender sender,
    ILogger<VendingMachineHandler> logger) : ClientMessageHandler<VendingMachineRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, VendingMachineRequest message, CancellationToken token)
    {
        var connection = context.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null) return;
        await using var lease = await transitions.AcquireAsync(session.AccountId, UserAccessMode.Exclusive, token).ConfigureAwait(false);
        if (sessions.FindByConnection(connection) != session || !users.TryGetByConnection(connection, out var user) || user is null) return;
        VendingMachineResult? result = null;
        byte error = 0;
        try
        {
            if (message.MachineId > int.MaxValue || message.GroupId > int.MaxValue || message.Slot < 3)
                throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Invalid vending input.");
            result = await commands.SendAsync(new VendingMachineCommand(user.OnlineUserId, session,
                (int)message.MachineId, (int)message.GroupId, message.Slot, message.Metadata.Sequence), token).ConfigureAwait(false);
        }
        catch (OpeningRejectedException ex)
        {
            error = ex.Reason switch { OpeningRejection.NoRoom => 1, OpeningRejection.MissingMaterial => 22, _ => (byte)0 };
            logger.LogInformation("Vending rejected character={Character}: {Reason} {Detail}", session.CharacterId, ex.Reason, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Vending transaction failed character={Character}", session.CharacterId); }
        logger.LogInformation("Vending character={Character} machine={Machine} group={Group} slot={Slot} seq={Sequence} committed={Committed} reward={Reward} count={Count} remaining={Remaining}",
            session.CharacterId, message.MachineId, message.GroupId, message.Slot, message.Metadata.Sequence, result is not null,
            result?.Reward.ItemId, result?.Reward.Count, result?.Remaining);
        var response = new VendingMachineResponse(result, error);
        if (result is null) await sender.SendCmdAsync(connection, response, token).ConfigureAwait(false);
        else await notifications.PublishAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new OpeningCompleted(result.Receipt, [response]), token).ConfigureAwait(false);
    }
}
