using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StoryPause;

/// <summary>
/// CMD 0x00BF <c>DUNGEON_EVENT_STORY_PAUSE</c>: the client pausing, or resuming from, a scripted
/// moment inside a dungeon.
/// </summary>
/// <remarks>
/// The static census marks this request-only because the client registers no receive handler for
/// the same type. That is true and beside the point: the reference server answers on a different
/// type, NOTI 0x00AA, and the pair is what lets a scripted moment proceed. A census that reads
/// only same-type registrations cannot see an answer that arrives under another number.
///
/// Observed live on 2026-08-18, twice in one dungeon run, with bodies <c>0001</c> and <c>0101</c>
/// -- one pause and one resume of the same request type. The tutorial's potion NPC could not be
/// interacted with in that session, which is the shape an unanswered pause takes: the script stops
/// where it asked to stop and never restarts.
/// </remarks>
public sealed record StoryPauseRequest(
    GameMessageMetadata Metadata,
    byte PauseFlag,
    byte RequestType) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameStoryPause;
}

/// <summary>NOTI 0x00AA: the pause the server acknowledges, addressed to the character that asked.</summary>
internal sealed record StoryPauseNotification(
    ushort WireUserId,
    byte PauseFlag,
    byte RequestType) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameStoryPauseState;
}

internal static class StoryPausePayloadCodec
{
    public const int MinimumBodyLength = 2;

    /// <summary>
    /// <c>u16 user, u8 pause, u8 requestType</c>: the request's own two bytes, returned under the
    /// character that sent them.
    /// </summary>
    /// <remarks>
    /// Echoed rather than decided. The reference server passes both bytes straight back, so the
    /// server is confirming the client's transition rather than choosing one, and inventing a
    /// value here would answer a question the client did not ask.
    /// </remarks>
    public static byte[] EncodeState(ushort wireUserId, byte pauseFlag, byte requestType)
    {
        using var stream = new MemoryStream(4);
        using var writer = new BinaryWriter(stream);
        writer.Write(wireUserId);
        writer.Write(pauseFlag);
        writer.Write(requestType);
        return stream.ToArray();
    }
}

internal sealed class StoryPauseEncoder : GameNotiMessageEncoder<StoryPauseNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(StoryPauseNotification message) =>
        OnePayload(
            GameProtocol.StoryPauseNotificationType,
            StoryPausePayloadCodec.EncodeState(
                message.WireUserId, message.PauseFlag, message.RequestType));
}

internal sealed class StoryPauseDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < StoryPausePayloadCodec.MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"DUNGEON_EVENT_STORY_PAUSE requires at least two body bytes; "
                    + $"received '{body.Length}'.");
        }

        return new StoryPauseRequest(metadata, body[0], body[1]);
    }
}

/// <summary>Confirms a scripted pause so the script can carry on.</summary>
public sealed class StoryPauseHandler(
    ISelectedSessionDirectory selectedSessions,
    IGameMessageSender sender,
    ILogger<StoryPauseHandler> logger) : ClientMessageHandler<StoryPauseRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        StoryPauseRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Story pause {ConnectionId}: no published session, ignoring pause={Pause} type={Type}",
                context.Connection.ConnectionId,
                message.PauseFlag,
                message.RequestType);
            return;
        }

        logger.LogInformation(
            "Story pause {ConnectionId} user={WireUserId}: pause={Pause} type={Type}",
            context.Connection.ConnectionId,
            session.WireUserId,
            message.PauseFlag,
            message.RequestType);

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new StoryPauseNotification(session.WireUserId, message.PauseFlag, message.RequestType)],
                []),
            cancellationToken).ConfigureAwait(false);
    }
}
