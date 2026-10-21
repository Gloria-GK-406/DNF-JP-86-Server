using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.FinishLoading;

/// <summary>
/// The gate at the end of the loading bar.
/// </summary>
/// <remarks>
/// Unlike the dungeon-entry frames, this pair has a live observation behind it. On 2026-08-18 the
/// client reached the dungeon's loading screen, sent this request, received nothing, and stopped
/// at the last sliver of the bar until it timed out and reported a failed connection. That is the
/// evidence that the reply is required, rather than merely registered for.
/// </remarks>
public sealed class FinishLoadingCodecTests
{
    /// <remarks>
    /// The observed body: eight bytes, all zero. Both u32 fields are read and neither is
    /// interpreted -- their boundaries are closed but nothing yet distinguishes their meanings.
    /// </remarks>
    [Fact]
    public void The_observed_request_decodes_as_two_zero_words()
    {
        byte[] body = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        var request = (FinishLoadingRequest)new FinishLoadingDecoder()
            .Decode(GameMessageMetadata.Empty, body)!;

        Assert.Equal(0u, request.First);
        Assert.Equal(0u, request.Second);
    }

    [Fact]
    public void Both_words_are_read_little_endian_and_kept_apart()
    {
        byte[] body = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];

        var request = (FinishLoadingRequest)new FinishLoadingDecoder()
            .Decode(GameMessageMetadata.Empty, body)!;

        Assert.Equal(0x04030201u, request.First);
        Assert.Equal(0x08070605u, request.Second);
    }

    [Fact]
    public void A_short_body_is_refused()
    {
        Assert.Throws<InvalidDataException>(() =>
            new FinishLoadingDecoder().Decode(GameMessageMetadata.Empty, new byte[7]));
    }

    /// <remarks>
    /// One byte. The reference server's common acknowledgement writes <c>01</c> for success and
    /// <c>00</c> followed by an error code for failure, so a success carries nothing else.
    /// </remarks>
    [Fact]
    public void The_acknowledgement_is_a_single_success_byte()
    {
        Assert.Equal([0x01], FinishLoadingPayloadCodec.EncodeResponse());
    }

    /// <remarks>
    /// Five zero bytes, as the reference builder writes them: an int32 and a byte, both zero.
    /// This is the frame that actually opens the gate; the acknowledgement alone does not.
    /// </remarks>
    [Fact]
    public void The_notification_that_opens_the_gate_is_five_zero_bytes()
    {
        Assert.Equal(
            [0x00, 0x00, 0x00, 0x00, 0x00],
            FinishLoadingPayloadCodec.EncodeLoadingComplete());
    }

    /// <summary>A21 answers with the notification alone, not with a CMD in front of it.</summary>
    /// <remarks>
    /// This sent two frames: a one-byte CMD acknowledgement and then NOTI 0x001E. The A21 reference
    /// sends only the notification, both times its session finishes a load -- at the dungeon's
    /// first room and again after MOVE_MAP. Verified at the wire level on 2026-08-26: the client's
    /// FINISH_LOADING draws one frame back and the session continues.
    ///
    /// The acknowledgement's encoder and its codec test stay: the older client did receive one, and
    /// the shape is worth keeping recorded. What changed is only whether it goes out here.
    /// </remarks>
    [Fact]
    public async Task The_handler_answers_with_the_notification_alone()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sender = new RecordingSender();
            var handler = new FinishLoadingHandler(
                sender,
                NullLogger<FinishLoadingHandler>.Instance,
                new RecordingProjection(accept: true));
            var context = new NetworkDispatchContext(
                new NetworkConnectionContext(new NetworkConnectionId("game-1"), NetworkEndpointKind.Game, 20011));

            await handler.HandleAsync(
                context,
                new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0),
                CancellationToken.None);

            var sent = Assert.Single(sender.Sent);
            Assert.IsType<LoadingCompleteNotification>(sent, exactMatch: false);
        });
    }

    [Fact]
    public async Task The_handler_submits_loading_to_the_projection_before_releasing_the_client()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sender = new RecordingSender();
            var projection = new RecordingProjection(accept: true);
            var handler = new FinishLoadingHandler(
                sender,
                NullLogger<FinishLoadingHandler>.Instance,
                projection);
            var connection = new NetworkConnectionId("game-current");

            await handler.HandleAsync(
                new NetworkDispatchContext(new NetworkConnectionContext(
                    connection, NetworkEndpointKind.Game, 20011)),
                new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0),
                CancellationToken.None);

            Assert.Equal(connection, Assert.Single(projection.Connections));
            Assert.IsType<LoadingCompleteNotification>(Assert.Single(sender.Sent));
        });
    }

    [Fact]
    public async Task A_stale_loading_connection_is_not_released()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sender = new RecordingSender();
            var projection = new RecordingProjection(accept: false);
            var handler = new FinishLoadingHandler(
                sender,
                NullLogger<FinishLoadingHandler>.Instance,
                projection);

            await handler.HandleAsync(
                new NetworkDispatchContext(new NetworkConnectionContext(
                    new NetworkConnectionId("game-stale"), NetworkEndpointKind.Game, 20011)),
                new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0),
                CancellationToken.None);

            Assert.Empty(sender.Sent);
        });
    }

    private sealed class RecordingProjection(bool accept) : IDungeonLoadingProjectionDelivery
    {
        public List<NetworkConnectionId> Connections { get; } = [];

        public ValueTask<bool> CompleteLoadingAsync(
            NetworkConnectionId connectionId,
            Func<CancellationToken, ValueTask> releaseClient,
            CancellationToken cancellationToken)
        {
            Connections.Add(connectionId);
            return CompleteAsync(releaseClient, cancellationToken);
        }

        private async ValueTask<bool> CompleteAsync(
            Func<CancellationToken, ValueTask> releaseClient,
            CancellationToken cancellationToken)
        {
            if (!accept)
                return false;
            await releaseClient(cancellationToken);
            return true;
        }
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }
}
