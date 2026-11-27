using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildWarehouse;

public sealed record GuildWarehouseTransferRequest(GameMessageMetadata Metadata, bool IsDeposit,
    byte Container, int BagSlot, int WarehouseSlot, int ItemId, int Count, bool IsValid) : IClientNetworkMessage
{
    public ClientMessageType MessageType =>
        IsDeposit ? ClientMessageType.GameGuildWarehouseDeposit : ClientMessageType.GameGuildWarehouseWithdraw;
}

internal sealed class GuildWarehouseDepositDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.Length == 13
        ? new GuildWarehouseTransferRequest(metadata, true, body[0],
            BinaryPrimitives.ReadUInt16LittleEndian(body[1..]), BinaryPrimitives.ReadUInt16LittleEndian(body[11..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]), BinaryPrimitives.ReadInt32LittleEndian(body[7..]), true)
        : new GuildWarehouseTransferRequest(metadata, true, 0, 0, 0, 0, 0, false);
}

internal sealed class GuildWarehouseWithdrawDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.Length == 11
        ? new GuildWarehouseTransferRequest(metadata, false, body[10], 0,
            BinaryPrimitives.ReadUInt16LittleEndian(body), BinaryPrimitives.ReadInt32LittleEndian(body[2..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[6..]), true)
        : new GuildWarehouseTransferRequest(metadata, false, 0, 0, 0, 0, 0, false);
}

internal sealed record GuildWarehouseTransferReply(bool IsDeposit, GuildWarehouseFailure Failure,
    GuildWarehouseTransfer? Transfer) : IGameCmdMessage
{
    public ServerMessageType MessageType =>
        IsDeposit ? ServerMessageType.GameGuildWarehouseDepositResult : ServerMessageType.GameGuildWarehouseWithdrawResult;
}

internal sealed class GuildWarehouseTransferEncoder : GameCmdMessageEncoder<GuildWarehouseTransferReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildWarehouseTransferReply message)
    {
        if (message.Transfer is not { } transfer)
            return OnePayload(message, GameCmdEnvelope.Refuse(ErrorCode(message.Failure, message.IsDeposit)));

        var slot = A21InventoryLayout.ToGameSlot(transfer.BagSlot);
        Span<byte> payload = stackalloc byte[9];
        if (message.IsDeposit)
        {
            payload[0] = (byte)slot.Space;
            BinaryPrimitives.WriteUInt16LittleEndian(payload[1..], checked((ushort)slot.Index));
            BinaryPrimitives.WriteInt32LittleEndian(payload[3..], transfer.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(payload[7..], checked((ushort)transfer.WarehouseSlot));
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload, checked((ushort)transfer.WarehouseSlot));
            payload[2] = (byte)slot.Space;
            BinaryPrimitives.WriteUInt16LittleEndian(payload[3..], checked((ushort)slot.Index));
            BinaryPrimitives.WriteInt32LittleEndian(payload[5..], transfer.Count);
        }
        return OnePayload(message, GameCmdEnvelope.Accept(payload));
    }

    private static byte ErrorCode(GuildWarehouseFailure failure, bool isDeposit) => failure switch
    {
        GuildWarehouseFailure.AccessDenied => 0x24,
        GuildWarehouseFailure.InvalidSlot => 0xC5,
        GuildWarehouseFailure.InvalidCount when isDeposit => 0xC7,
        GuildWarehouseFailure.Untransferable when isDeposit => 0xC6,
        GuildWarehouseFailure.NoRoom when isDeposit => 0xCA,
        _ => 0xCD,
    };
}

public sealed record GameGuildWarehouseTransferCommand(NetworkDispatchContext Dispatch, GuildWarehouseTransferRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildWarehouseTransferCommandHandler(ISelectedSessionDomainService sessions,
    IGuildWarehouseDomainService warehouse, ICommandWorkUnits units, IGameCommandOutbox outbox,
    ICommandProjections projections) : ICommandHandler<GameGuildWarehouseTransferCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuildWarehouseTransferCommand command,
        CancellationToken ct)
    {
        var request = command.Request;
        if (GameRequestFreshness.IsStale(request.Metadata))
            return false;

        var connection = command.Dispatch.Connection;
        GuildWarehouseTransferResult result;
        if (!request.IsValid || request.Container != (byte)InventoryContainerType.Main
            || request.ItemId <= 0 || request.Count < 0)
            result = new(GuildWarehouseFailure.InvalidSlot);
        else if (!sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            result = new(GuildWarehouseFailure.AccessDenied);
        else
        {
            BusinessSlot source = default;
            if (request.IsDeposit)
            {
                try { source = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.BagSlot)); }
                catch (InventorySlotMappingException)
                {
                    await outbox.RegisterCmdAsync(connection.ConnectionId,
                        new GuildWarehouseTransferReply(true, GuildWarehouseFailure.InvalidSlot, null), ct).ConfigureAwait(false);
                    return false;
                }
            }

            var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
            result = await units.WriteAsync(context, new("guild-warehouse-transfer", [recipient]), (_, token) =>
                request.IsDeposit
                    ? warehouse.DepositAsync(actor, source, request.WarehouseSlot, request.ItemId, request.Count, token)
                    : warehouse.WithdrawAsync(actor, request.WarehouseSlot, request.ItemId, request.Count, token),
                ct).ConfigureAwait(false);
        }

        await outbox.RegisterCmdAsync(connection.ConnectionId,
            new GuildWarehouseTransferReply(request.IsDeposit, result.Failure, result.Transfer), ct).ConfigureAwait(false);
        if (result.Transfer is not { } transfer)
            return false;

        var snapshot = new GuildWarehouseCapacityChanged(transfer.Warehouse);
        await outbox.RegisterNotiAsync(connection.ConnectionId, snapshot, ct).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters(transfer.OtherMembers), snapshot, ct)
            .ConfigureAwait(false);
        return true;
    }
}

public sealed class GuildWarehouseTransferHandler(ICommandBus commands)
    : ClientMessageHandler<GuildWarehouseTransferRequest>, IProtocolCommandMessageHandler<GameGuildWarehouseTransferCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildWarehouseTransferRequest message,
        CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildWarehouseTransferCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildWarehouseTransferRegistration
{
    internal static void Add(IServiceCollection services)
    {
        const ClientMessageType deposit = ClientMessageType.GameGuildWarehouseDeposit;
        const ClientMessageType withdraw = ClientMessageType.GameGuildWarehouseWithdraw;
        services.AddGameCmdMessageDecoder<GuildWarehouseDepositDecoder>(deposit);
        services.AddGameCmdMessageDecoder<GuildWarehouseWithdrawDecoder>(withdraw);
        services.AddGameCmdMessageEncoder<GuildWarehouseTransferReply, GuildWarehouseTransferEncoder>();
        services.AddProtocolCommandHandler<GameGuildWarehouseTransferCommand, bool, GameGuildWarehouseTransferCommandHandler>(
            new ProtocolCommandMessageBinding(deposit, typeof(GuildWarehouseTransferRequest)),
            new ProtocolCommandMessageBinding(withdraw, typeof(GuildWarehouseTransferRequest)));
        services.RegisterProtocolMessageHandler<GuildWarehouseTransferHandler, GuildWarehouseTransferRequest, GameGuildWarehouseTransferCommand>(deposit);
        services.RegisterProtocolMessageHandler<GuildWarehouseTransferHandler, GuildWarehouseTransferRequest, GameGuildWarehouseTransferCommand>(withdraw);
    }
}
