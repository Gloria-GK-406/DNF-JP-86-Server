using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game;

/// <summary>A server-to-client message in one of the two Game protocol families.</summary>
public interface IGameServerMessage : IServerNetworkMessage;

/// <summary>A Game CMD message, dispatched by the client's command registry.</summary>
public interface IGameCmdMessage : IGameServerMessage;

/// <summary>A Game NOTI message, dispatched by the client's notification registry.</summary>
public interface IGameNotiMessage : IGameServerMessage;

internal enum GameMessageClass : byte
{
    Noti = 0x00,
    Cmd = 0x01,
}

internal readonly record struct GameMessagePayload(
    ushort Type,
    ReadOnlyMemory<byte> Body,
    uint Checksum = 0,
    uint Reserved = 0);

internal abstract class GameMessageEncoder<TMessage>(GameMessageClass messageClass)
    : ServerMessageEncoder<TMessage>
    where TMessage : class, IGameServerMessage
{
    protected sealed override IReadOnlyList<byte[]> EncodeMessage(TMessage message) =>
        [.. EncodePayloads(message).Select(payload => GameServerFrameEncoder.Encode(
            (byte)messageClass,
            payload.Type,
            payload.Body.Span,
            payload.Checksum,
            payload.Reserved))];

    protected abstract IReadOnlyList<GameMessagePayload> EncodePayloads(TMessage message);

    protected static IReadOnlyList<GameMessagePayload> OnePayload(
        ushort type,
        ReadOnlyMemory<byte> body) => [new(type, body)];
}

internal abstract class GameCmdMessageEncoder<TMessage>()
    : GameMessageEncoder<TMessage>(GameMessageClass.Cmd)
    where TMessage : class, IGameCmdMessage;

internal abstract class GameNotiMessageEncoder<TMessage>()
    : GameMessageEncoder<TMessage>(GameMessageClass.Noti)
    where TMessage : class, IGameNotiMessage;

/// <summary>An explicitly addressed ordered batch of strongly classified Game messages.</summary>
public sealed record GameExplicitMessageTarget(
    NetworkConnectionId ConnectionId,
    IReadOnlyList<IGameServerMessage> OrderedMessages);

/// <summary>
/// A Game delivery plan whose individual messages carry their own non-overridable CMD/NOTI class.
/// </summary>
public sealed record GameExplicitMessageDelivery(
    IReadOnlyList<IGameServerMessage> RequiredDirectMessages,
    IReadOnlyList<GameExplicitMessageTarget> BestEffortTargets);

public interface IGameMessageSender
{
    ValueTask SendBatchAsync(NetworkConnectionContext expectedConnection,
        IReadOnlyList<IGameServerMessage> messages, CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException("This Game sender does not support prepared typed batches."));

    ValueTask SendCmdAsync(
        NetworkConnectionId connectionId,
        IGameCmdMessage message,
        CancellationToken cancellationToken);

    ValueTask SendNotiAsync(
        NetworkConnectionId connectionId,
        IGameNotiMessage message,
        CancellationToken cancellationToken);

    ValueTask SendExplicitAsync(
        NetworkConnectionId requiredDirectConnectionId,
        GameExplicitMessageDelivery delivery,
        CancellationToken cancellationToken);
}

internal sealed class GameMessageSender(INetworkMessageSender sender) : IGameMessageSender
{
    public ValueTask SendBatchAsync(NetworkConnectionContext expectedConnection,
        IReadOnlyList<IGameServerMessage> messages, CancellationToken cancellationToken) =>
        sender.SendBatchAsync(expectedConnection, [.. messages.Cast<IServerNetworkMessage>()], cancellationToken);

    public ValueTask SendCmdAsync(
        NetworkConnectionId connectionId,
        IGameCmdMessage message,
        CancellationToken cancellationToken) =>
        sender.SendAsync(connectionId, message, cancellationToken);

    public ValueTask SendNotiAsync(
        NetworkConnectionId connectionId,
        IGameNotiMessage message,
        CancellationToken cancellationToken) =>
        sender.SendAsync(connectionId, message, cancellationToken);

    public ValueTask SendExplicitAsync(
        NetworkConnectionId requiredDirectConnectionId,
        GameExplicitMessageDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        return sender.SendExplicitAsync(
            requiredDirectConnectionId,
            delivery.ToNetworkDelivery(),
            cancellationToken);
    }
}

internal static class GameMessageRegistrationExtensions
{
    public static IServiceCollection AddGameMessageSending(this IServiceCollection services)
    {
        services.TryAddSingleton<IGameMessageSender, GameMessageSender>();
        services.TryAddSingleton<IGameCommandOutbox, GameCommandOutbox>();
        // The endpoint layer's own outbound frames, bound here rather than inside any slice: a
        // slice that registered one would be the slice that owned it, and the frames that live
        // here are exactly the ones no single slice may own. See GameInventoryDelivery.
        GameInventoryDelivery.AddEncoders(services);
        return services;
    }

    public static IServiceCollection AddGameCmdMessageEncoder<TMessage, TEncoder>(
        this IServiceCollection services)
        where TMessage : class, IGameCmdMessage
        where TEncoder : GameCmdMessageEncoder<TMessage> =>
        services.AddServerMessageEncoder<TMessage, TEncoder>(NetworkEndpointKind.Game);

    public static IServiceCollection AddGameNotiMessageEncoder<TMessage, TEncoder>(
        this IServiceCollection services)
        where TMessage : class, IGameNotiMessage
        where TEncoder : GameNotiMessageEncoder<TMessage> =>
        services.AddServerMessageEncoder<TMessage, TEncoder>(NetworkEndpointKind.Game);

    public static IServiceCollection AddGameCmdMessageDecoder<TDecoder>(
        this IServiceCollection services,
        ushort type,
        params ClientMessageType[] produces)
        where TDecoder : class, IGameMessageDecoder =>
        services.AddGameMessageDecoder<TDecoder>(
            (byte)GameMessageClass.Cmd,
            type,
            produces);
}

internal static class GameDeliveryConversions
{
    internal static ExplicitMessageDelivery ToNetworkDelivery(this GameExplicitMessageDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        return new([.. delivery.RequiredDirectMessages.Cast<IServerNetworkMessage>()],
            [.. delivery.BestEffortTargets.Select(target => new ExplicitMessageTarget(
                target.ConnectionId, [.. target.OrderedMessages.Cast<IServerNetworkMessage>()]))]);
    }
}
