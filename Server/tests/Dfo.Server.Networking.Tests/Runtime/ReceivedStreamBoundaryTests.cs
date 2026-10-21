using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Tests.Runtime;

/// <summary>
/// The receive callback is a stream boundary (plan, transitional rule 3): even if TouchSocket ran it
/// inside a job, the dispatch it starts is off the executor, and the job's token is back afterwards.
/// </summary>
public sealed class ReceivedStreamBoundaryTests
{
    [Fact]
    public async Task A_game_receive_callback_called_inside_a_job_dispatches_off_the_executor()
    {
        var connections = new TouchSocketConnectionRegistry();
        var session = DispatchProxy.Create<ISession, Session>();
        connections.Add(session, NetworkEndpointKind.Game);
        var dispatcher = new RecordingDispatcher();
        var services = new ServiceCollection().AddSingleton<GameDecoder>().BuildServiceProvider();
        var plugin = new GameNetworkPlugin(connections, [], dispatcher,
            new GameMessageDecoderRegistry([new GameMessageDecoderRegistration(1, 0x0003, typeof(GameDecoder))], services),
            NullLogger<GameNetworkPlugin>.Instance);
        var frame = new GameClientFrameRequestInfo();
        var header = new byte[GameClientFrameRequestInfo.HeaderLength];
        header[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(1), 0x0003);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(3), (uint)header.Length);
        Assert.True(frame.OnParsingHeader(header));
        Assert.True(frame.OnParsingBody([]));

        var (before, after) = await InJobAsync(() => plugin.OnTcpReceived(session, Received(frame)));

        Assert.True(before);
        Assert.Equal([false], dispatcher.OnExecutor);
        Assert.True(after);
    }

    [Fact]
    public async Task A_channel_receive_callback_called_inside_a_job_dispatches_off_the_executor()
    {
        var connections = new TouchSocketConnectionRegistry();
        var session = DispatchProxy.Create<ISession, Session>();
        connections.Add(session, NetworkEndpointKind.Channel);
        var dispatcher = new RecordingDispatcher();
        var services = new ServiceCollection().AddSingleton<ChannelDecoder>().BuildServiceProvider();
        var plugin = new ChannelNetworkPlugin(connections, [], dispatcher,
            new ChannelMessageDecoderRegistry([new ChannelMessageDecoderRegistration(0x0B, typeof(ChannelDecoder))], services),
            NullLogger<ChannelNetworkPlugin>.Instance);
        var frame = new ChannelFrameRequestInfo();
        var header = new byte[ChannelFrameRequestInfo.HeaderLength];
        header[0] = 0x7C;
        header[1] = 0x0B;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), (uint)header.Length);
        header[10] = 1;
        Assert.True(frame.OnParsingHeader(header));
        Assert.True(frame.OnParsingBody([]));

        var (before, after) = await InJobAsync(() => plugin.OnTcpReceived(session, Received(frame)));

        Assert.True(before);
        Assert.Equal([false], dispatcher.OnExecutor);
        Assert.True(after);
    }

    private static ReceivedDataEventArgs Received(IRequestInfo frame) => new(ReadOnlyMemory<byte>.Empty, frame);

    private static async Task<(bool Before, bool After)> InJobAsync(Func<Task> callback)
    {
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        return await executor.RunAsync(new GameplayWork<(bool, bool)>(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            "test:received-boundary",
            async _ =>
            {
                var before = GameplayJob.IsOnExecutor;
                await callback();
                return (before, GameplayJob.IsOnExecutor);
            }), CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class RecordingDispatcher : INetworkMessageDispatcher
    {
        public List<bool> OnExecutor { get; } = [];

        public ValueTask DispatchAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken cancellationToken)
        {
            OnExecutor.Add(GameplayJob.IsOnExecutor);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record Probe : IClientNetworkMessage
    {
        public ClientMessageType MessageType => ClientMessageType.GameExit;
    }

    private sealed class GameDecoder : IGameMessageDecoder
    {
        public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => new Probe();
    }

    private sealed class ChannelDecoder : IChannelMessageDecoder
    {
        public IClientNetworkMessage? Decode(ChannelMessageMetadata metadata, ReadOnlySpan<byte> body) => new Probe();
    }

    public interface ISession : ITcpSession, ITcpSessionClient;

    public class Session : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "get_Id" => "boundary",
            "get_LocalEndPoint" or "get_RemoteEndPoint" => new IPEndPoint(IPAddress.Loopback, 10011),
            _ => throw new NotSupportedException(method.Name),
        };
    }
}
