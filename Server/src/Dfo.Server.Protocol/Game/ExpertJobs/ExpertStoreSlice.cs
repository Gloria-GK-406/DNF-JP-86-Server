using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ExpertJobs;

internal sealed record ExpertStoreRequest(GameMessageMetadata Metadata, ClientMessageType MessageType,
    bool Malformed, ExpertStorePlacement? Placement = null, ushort OwnerUserId = 0) : IClientNetworkMessage;

internal sealed class ExpertStoreDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var type = (ClientMessageType)((metadata.Selector << 16) | metadata.Type);
        return type switch
        {
            ClientMessageType.GameCreateExpertStore => DecodeOpen(metadata, type, body),
            ClientMessageType.GameEnterExpertStore when body.Length == 2 =>
                new ExpertStoreRequest(metadata, type, false, OwnerUserId: BinaryPrimitives.ReadUInt16LittleEndian(body)),
            ClientMessageType.GameCloseExpertStore when body.IsEmpty => new ExpertStoreRequest(metadata, type, false),
            _ => new ExpertStoreRequest(metadata, type, true),
        };
    }

    private static ExpertStoreRequest DecodeOpen(GameMessageMetadata metadata, ClientMessageType type, ReadOnlySpan<byte> body)
    {
        var placement = OpenPlacement(body);
        return new(metadata, type, placement is null, placement);
    }

    private static ExpertStorePlacement? OpenPlacement(ReadOnlySpan<byte> body)
    {
        if (body.Length < 15) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(body[1..]);
        if (length is < 0 or > byte.MaxValue || body.Length != 15 + length) return null;
        var name = ImmutableArray.CreateRange(body.Slice(5, length).ToArray());
        var tail = body[(5 + length)..];
        return new((ExpertStoreKind)body[0], name, BinaryPrimitives.ReadInt32LittleEndian(tail),
            BinaryPrimitives.ReadInt16LittleEndian(tail[4..]), BinaryPrimitives.ReadInt16LittleEndian(tail[6..]),
            BinaryPrimitives.ReadInt16LittleEndian(tail[8..]));
    }
}

internal sealed record ExpertStoreReply(ServerMessageType MessageType, byte Error = 0, ExpertStoreView? View = null) : IGameCmdMessage;

internal sealed class ExpertStoreReplyEncoder : GameCmdMessageEncoder<ExpertStoreReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ExpertStoreReply message)
    {
        if (message.Error != 0) return OnePayload(message, new byte[] { 0, message.Error });
        if (message.View is not { } view) return OnePayload(message, new byte[] { 1 });
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write((byte)view.Store.Kind);
        if (view.Store.Kind == ExpertStoreKind.DisjointMachine)
        {
            writer.Write(checked((byte)view.Grade));
            writer.Write(view.Store.Cost);
        }
        else writer.Write(view.Store.Owner.WireUserId);
        writer.Write(view.Endurance);
        return OnePayload(message, payload.ToArray());
    }
}

internal sealed record ExpertStoreOpenedMessage(ExpertStoreView View) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameExpertStoreOpened;
}

internal sealed class ExpertStoreOpenedEncoder : GameNotiMessageEncoder<ExpertStoreOpenedMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ExpertStoreOpenedMessage message)
    {
        var view = message.View;
        var store = view.Store;
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)store.Kind);
        writer.Write(store.Owner.WireUserId);
        writer.Write(store.Placement.Name.Length);
        writer.Write(store.Placement.Name.AsSpan());
        writer.Write(store.Area.TownId);
        writer.Write(store.Area.AreaId);
        writer.Write(store.Placement.X);
        writer.Write(store.Placement.Y);
        writer.Write(store.Cost);
        writer.Write((byte)0); // Replace the standing avatar with the store.
        if (store.Kind == ExpertStoreKind.EnchantShop)
        {
            writer.Write(checked((byte)view.CardQualifications.Count));
            foreach (var qualification in view.CardQualifications) writer.Write(qualification);
        }
        return OnePayload(message, payload.ToArray());
    }
}

internal sealed record GameExpertStoreCommand(NetworkDispatchContext Dispatch, ExpertStoreRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameExpertStoreCommandHandler(ISelectedSessionDomainService sessions,
    IExpertStoreDomainService stores, IExpertStoreGameplayDomainService gameplay,
    Dfo.Server.Abstractions.World.ITownAreaDomainService townAreas, ICommandProjections projections, IGameCommandOutbox outbox)
    : ICommandHandler<GameExpertStoreCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameExpertStoreCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection.ConnectionId;
        var request = command.Input;
        var reply = (ServerMessageType)request.MessageType;
        if (request.Malformed)
            return await RefuseAsync(connection, reply, ExpertStoreRejection.InvalidRequest, ct).ConfigureAwait(false);
        if (!sessions.TryGetInWorld(connection, out var actor))
            return await RefuseAsync(connection, reply, ExpertStoreRejection.InvalidState, ct).ConfigureAwait(false);

        if (request.MessageType == ClientMessageType.GameCloseExpertStore)
        {
            if (!stores.HasOpenStore(actor) && stores.Leave(actor)) return true;
            if (stores.CloseOwned(actor) is { } closure)
                await ExpertStoreProjection.ClosedAsync(sessions, townAreas, projections, closure, includeOwner: true, ct).ConfigureAwait(false);
            else
                await outbox.RegisterNotiAsync(connection, new ExpertStoreClosedMessage(actor.WireUserId), ct).ConfigureAwait(false);
            return true;
        }

        var isOpen = request.MessageType == ClientMessageType.GameCreateExpertStore;
        var result = isOpen && request.Placement is { } placement
            ? await gameplay.OpenAsync(actor, placement, ct).ConfigureAwait(false)
            : await gameplay.EnterAsync(actor, request.OwnerUserId, ct).ConfigureAwait(false);
        if (result.View is not { } view)
            return await RefuseAsync(connection, reply, result.Rejection, ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection, new ExpertStoreReply(reply, View: isOpen ? null : view), ct).ConfigureAwait(false);
        if (isOpen)
            await ExpertStoreProjection.OpenedAsync(sessions, townAreas, projections, view, ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> RefuseAsync(NetworkConnectionId connection, ServerMessageType reply,
        ExpertStoreRejection rejection, CancellationToken ct)
    {
        var error = rejection switch
        {
            ExpertStoreRejection.InvalidRequest => (byte)10,
            ExpertStoreRejection.UnavailablePoint => (byte)190,
            ExpertStoreRejection.RestrictedCommercialZone => (byte)82,
            _ => (byte)19,
        };
        await outbox.RegisterCmdAsync(connection, new ExpertStoreReply(reply, error), ct).ConfigureAwait(false);
        return false;
    }
}

internal sealed class ExpertStoreHandler(ICommandBus commands)
    : ClientMessageHandler<ExpertStoreRequest>, IProtocolCommandMessageHandler<GameExpertStoreCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ExpertStoreRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameExpertStoreCommand(context, message), ct).ConfigureAwait(false);
}

internal static class ExpertStoreRegistration
{
    internal static void AddExpertStoreSlice(this IServiceCollection services)
    {
        ClientMessageType[] types =
        [
            ClientMessageType.GameCreateExpertStore,
            ClientMessageType.GameEnterExpertStore,
            ClientMessageType.GameCloseExpertStore,
        ];
        services.AddProtocolCommandHandler<GameExpertStoreCommand, bool, GameExpertStoreCommandHandler>(
            [.. types.Select(type => new ProtocolCommandMessageBinding(type, typeof(ExpertStoreRequest)))]);
        foreach (var type in types)
        {
            services.AddGameCmdMessageDecoder<ExpertStoreDecoder>(type);
            services.RegisterProtocolMessageHandler<ExpertStoreHandler, ExpertStoreRequest, GameExpertStoreCommand>(type);
        }
        services.AddGameCmdMessageEncoder<ExpertStoreReply, ExpertStoreReplyEncoder>();
        services.AddGameNotiMessageEncoder<ExpertStoreOpenedMessage, ExpertStoreOpenedEncoder>();
        services.AddNotificationHandler<ExpertStoreArrival, ExpertStoreArrivalHandler>();
        services.AddNotificationHandler<ExpertStoreOpened, ExpertStoreOpenedHandler>();
        services.AddNotificationHandler<ExpertStoreClosed, ExpertStoreClosedHandler>();
    }
}
