using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

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
public sealed record VendingMachineCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    int MachineId, int GroupId, int Slot) : ICommand<VendingMachineResult>, IRequestSettlementCommand, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class VendingMachineCommandHandler(IVendingMachineDomainService vending) : ICommandHandler<VendingMachineCommand, VendingMachineResult>
{
    public ValueTask<VendingMachineResult> HandleAsync(CommandExecutionContext context, VendingMachineCommand command, CancellationToken token) =>
        vending.UseAsync(command.Session, command.MachineId, command.GroupId,
            OpeningWireSlots.FromMain(command.Slot), token);
}
public sealed class VendingMachineHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    SelectedSessionTransitionGate transitions, ICommandBus commands, IEventService notifications, IGameMessageSender sender,
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
                (int)message.MachineId, (int)message.GroupId, message.Slot), token).ConfigureAwait(false);
        }
        catch (OpeningRejectedException ex) when (!Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(ex))
        {
            error = ex.Reason switch { OpeningRejection.NoRoom => 1, OpeningRejection.MissingMaterial => 22, _ => (byte)0 };
            logger.LogInformation("Vending rejected character={Character}: {Reason} {Detail}", session.CharacterId, ex.Reason, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(ex)) { logger.LogError(ex, "Vending transaction failed character={Character}", session.CharacterId); }
        logger.LogInformation("Vending character={Character} machine={Machine} group={Group} slot={Slot} seq={Sequence} committed={Committed} reward={Reward} count={Count} remaining={Remaining}",
            session.CharacterId, message.MachineId, message.GroupId, message.Slot, message.Metadata.Sequence, result is not null,
            result?.Reward.ItemId, result?.Reward.Count, result?.Remaining);
        var response = new VendingMachineResponse(result, error);
        if (result is null) await sender.SendCmdAsync(connection, response, token).ConfigureAwait(false);
        else await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new OpeningCompleted(result.Receipt, [response]), token).ConfigureAwait(false);
    }
}
