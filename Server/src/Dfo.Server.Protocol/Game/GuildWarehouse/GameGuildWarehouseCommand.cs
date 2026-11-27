using System.Buffers.Binary;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildWarehouse;

public sealed record GuildWarehouseRequest(GameMessageMetadata Metadata, bool IsEmpty) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGuildWarehouse;
}

internal sealed class GuildWarehouseDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildWarehouseRequest(metadata, body.IsEmpty);
}

internal sealed record GuildWarehouseOpened(GuildWarehouseView? View) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildWarehouseOpened;
}

internal sealed record GuildWarehouseCapacityChanged(GuildWarehouseView View) :
    IGameNotiMessage, INotification<GuildWarehouseCapacityChanged>
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildWarehouseSnapshot;
}

internal static class GuildWarehouseSnapshotCodec
{
    internal const int ItemRowSize = 101;

    internal static byte[] Write(GuildWarehouseView view)
    {
        var body = new byte[6 + view.Items.Count * ItemRowSize];
        BinaryPrimitives.WriteUInt32LittleEndian(body, checked((uint)view.Capacity));
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), checked((ushort)view.Items.Count));
        for (var index = 0; index < view.Items.Count; index++)
        {
            var entry = view.Items[index];
            var item = entry.Item;
            var row = body.AsSpan(6 + index * ItemRowSize, ItemRowSize);
            BinaryPrimitives.WriteInt16LittleEndian(row, checked((short)entry.Slot));
            BinaryPrimitives.WriteInt32LittleEndian(row[2..], item.ItemId);
            BinaryPrimitives.WriteInt32LittleEndian(row[6..], item.IsPile ? item.Count : item.InstanceValue);
            row[10] = item.Attribute;
            BinaryPrimitives.WriteUInt16LittleEndian(row[11..], item.Durability);
            row[13] = item.SealFlag;
            BinaryPrimitives.WriteInt32LittleEndian(row[14..], item.EnchantCardId);
            row[18] = item.EnchantUpgradeCount;
            row[19] = item.AmplifyType;
            BinaryPrimitives.WriteUInt16LittleEndian(row[20..], item.AmplifyValue);
        }
        return body;
    }
}

internal sealed class GuildWarehouseOpenedEncoder : GameCmdMessageEncoder<GuildWarehouseOpened>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildWarehouseOpened message) =>
        OnePayload(message, message.View is { } view
            ? GameCmdEnvelope.Accept(GuildWarehouseSnapshotCodec.Write(view))
            : GameCmdEnvelope.Refuse(0x24));
}

internal sealed class GuildWarehouseCapacityEncoder : GameNotiMessageEncoder<GuildWarehouseCapacityChanged>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildWarehouseCapacityChanged message) =>
        OnePayload(message, GuildWarehouseSnapshotCodec.Write(message.View));
}

internal sealed class GuildWarehouseCapacityHandler : INotificationHandler<GuildWarehouseCapacityChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildWarehouseCapacityChanged notification, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([notification]);
}

public sealed record GameGuildWarehouseCommand(NetworkDispatchContext Dispatch, GuildWarehouseRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildWarehouseCommandHandler(
    ISelectedSessionDomainService sessions, IGuildWarehouseDomainService warehouse,
    ICommandWorkUnits units, IGameCommandOutbox outbox) : ICommandHandler<GameGuildWarehouseCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildWarehouseCommand command, CancellationToken ct)
    {
        var request = command.Request;
        if (GameRequestFreshness.IsStale(request.Metadata))
            return false;

        var connection = command.Dispatch.Connection;
        var reply = request.IsEmpty && sessions.TryGetInWorld(connection.ConnectionId, out var actor)
            ? await units.ReadOnlyAsync(context,
                new("guild-warehouse-open", [new SelectedSessionRecipient(actor, connection, request.Metadata)]),
                async token => new GuildWarehouseOpened(await warehouse.OpenAsync(actor, token).ConfigureAwait(false)),
                ct).ConfigureAwait(false)
            : new GuildWarehouseOpened(null);

        await outbox.RegisterCmdAsync(connection.ConnectionId, reply, ct).ConfigureAwait(false);
        return reply.View is not null;
    }
}

public sealed class GuildWarehouseRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildWarehouseRequest>, IProtocolCommandMessageHandler<GameGuildWarehouseCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildWarehouseRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildWarehouseCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildWarehouseRegistration
{
    internal static void AddGuildWarehouseSlice(IServiceCollection services)
    {
        const ClientMessageType type = ClientMessageType.GameGuildWarehouse;
        services.AddGameCmdMessageDecoder<GuildWarehouseDecoder>(type);
        services.AddGameCmdMessageEncoder<GuildWarehouseOpened, GuildWarehouseOpenedEncoder>();
        services.AddGameNotiMessageEncoder<GuildWarehouseCapacityChanged, GuildWarehouseCapacityEncoder>();
        services.AddNotificationHandler<GuildWarehouseCapacityChanged, GuildWarehouseCapacityHandler>();
        services.AddProtocolCommandHandler<GameGuildWarehouseCommand, bool, GameGuildWarehouseCommandHandler>(
            new ProtocolCommandMessageBinding(type, typeof(GuildWarehouseRequest)));
        services.RegisterProtocolMessageHandler<GuildWarehouseRequestHandler, GuildWarehouseRequest, GameGuildWarehouseCommand>(type);
        GuildWarehouseTransferRegistration.Add(services);
        GuildWarehouseMaintenanceRegistration.Add(services);
    }
}
