using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.FinishLoading;

/// <summary>
/// CMD 0x0025 <c>FINISH_LOADING</c>: the client reporting that a map has finished loading.
/// </summary>
/// <remarks>
/// The gate at the end of the loading bar. Observed live on 2026-08-18: answering SELECT_DUNGEON
/// with the run's three frames got the client into the dungeon's loading screen, where it sent
/// this and stopped at the last sliver of the bar because nothing answered. The bar completes when
/// the reply arrives, so this is what turns "the client accepted the layout" into "the character
/// is standing in the room".
///
/// Not specific to dungeons. The reference server handles it on its town handler, and the client
/// loads a map whenever it changes one, so this belongs to map loading in general rather than to
/// dungeon entry.
///
/// The body is closed at eight bytes and both u32 fields were zero in the observed frame; neither
/// has evidenced meaning, so they are read and not interpreted.
/// </remarks>
public sealed record FinishLoadingRequest(
    GameMessageMetadata Metadata,
    uint First,
    uint Second) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameFinishLoading;
}

/// <summary>The CMD acknowledgement for the same type.</summary>
public sealed record FinishLoadingResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameFinishLoading;
}

/// <summary>NOTI 0x001E: loading is over, the map may be entered.</summary>
internal sealed record LoadingCompleteNotification : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLoadingComplete;
}

internal static class FinishLoadingPayloadCodec
{
    public const int BodyLength = 8;

    /// <summary>
    /// A one-byte success. The reference server's common acknowledgement is <c>01</c> for success
    /// and <c>00</c> followed by a code for failure, so the single byte is the whole answer.
    /// </summary>
    public static byte[] EncodeResponse() => [0x01];

    /// <summary>Five bytes, all zero, exactly as the reference builder writes them.</summary>
    public static byte[] EncodeLoadingComplete() => [0x00, 0x00, 0x00, 0x00, 0x00];
}

internal sealed class FinishLoadingDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < FinishLoadingPayloadCodec.BodyLength)
        {
            throw new InvalidDataException(
                $"FINISH_LOADING requires {FinishLoadingPayloadCodec.BodyLength} body bytes; "
                    + $"received '{body.Length}'.");
        }

        return new FinishLoadingRequest(
            metadata,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body[4..]));
    }
}

internal sealed class FinishLoadingResponseEncoder : GameCmdMessageEncoder<FinishLoadingResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(FinishLoadingResponse message) =>
        OnePayload(GameProtocol.FinishLoadingType, FinishLoadingPayloadCodec.EncodeResponse());
}

internal sealed class LoadingCompleteEncoder : GameNotiMessageEncoder<LoadingCompleteNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        LoadingCompleteNotification message) =>
        OnePayload(
            GameProtocol.LoadingCompleteNotificationType,
            FinishLoadingPayloadCodec.EncodeLoadingComplete());
}

/// <summary>
/// Releases the loading screen.
/// </summary>
/// <remarks>
/// Both frames, in this order: the acknowledgement for the request, then the notification that
/// actually opens the gate. The reference server sends the same pair and follows it with an
/// experience-progress push, which is a growth-capsule concern and is not part of releasing the
/// screen.
/// </remarks>
public sealed class FinishLoadingHandler(
    IGameMessageSender sender,
    ILogger<FinishLoadingHandler> logger,
    IDungeonLoadingProjectionDelivery entryProjection,
    IllusionTowerProjectionDelivery? illusionTower = null, BossTowerProjectionDelivery? bossTower = null)
    : ClientMessageHandler<FinishLoadingRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        FinishLoadingRequest message,
        CancellationToken cancellationToken)
    {
        if (bossTower is not null && await bossTower.CompleteLoadingAsync(context.Connection.ConnectionId, message.Metadata, cancellationToken).ConfigureAwait(false)) return;

        if (illusionTower is not null
            && await illusionTower.CompleteLoadingAsync(
                context.Connection.ConnectionId, message.Metadata, cancellationToken).ConfigureAwait(false))
            return;

        if (!await entryProjection.CompleteLoadingAsync(
                context.Connection.ConnectionId,
                token => sender.SendExplicitAsync(
                    context.Connection.ConnectionId,
                    new GameExplicitMessageDelivery(
                        [new LoadingCompleteNotification()],
                        []),
                    token),
                cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning(
                "Finish loading {ConnectionId}: stale or missing selected-session generation",
                context.Connection.ConnectionId);
            return;
        }

        logger.LogInformation(
            "Finish loading {ConnectionId}: releasing the current map ({First},{Second})",
            context.Connection.ConnectionId,
            message.First,
            message.Second);

        // A21 answers with only NOTI 0x001E. The shared coordinator delivered it while the exact
        // selected-session generation was still leased; no CMD acknowledgement precedes it.
    }
}
