using System.Buffers;
using System.Net;
using System.Reflection;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Tests.Runtime;

public sealed class PreparedBatchTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("endpoint")]
    [InlineData("oversize")]
    public async Task A_late_registration_endpoint_or_actual_frame_limit_failure_sends_nothing(string failure)
    {
        var registry = new TouchSocketConnectionRegistry();
        var transport = Session("original"); var context = registry.Add(transport, NetworkEndpointKind.Game);
        var services = new ServiceCollection();
        services.AddServerMessageEncoder<Probe, ProbeEncoder>(NetworkEndpointKind.Game);
        if (failure != "missing")
            services.AddServerMessageEncoder<Other, OtherEncoder>(failure == "endpoint" ? NetworkEndpointKind.Channel : NetworkEndpointKind.Game);
        services.AddSingleton<ServerMessageEncoderRegistry>();
        using var provider = services.BuildServiceProvider();
        var sender = new TouchSocketNetworkMessageSender(registry, provider.GetRequiredService<ServerMessageEncoderRegistry>(),
            NullLogger<TouchSocketNetworkMessageSender>.Instance);
        await Assert.ThrowsAnyAsync<Exception>(async () => await sender.SendBatchAsync(context,
            [new Probe(0), new Other(failure == "oversize" ? 1_048_577 : 1)], default));
        Assert.Empty(((SessionProxy)(object)transport).Frames);
    }

    [Theory]
    [InlineData("pets")]
    [InlineData("skills")]
    [InlineData("extras")]
    [InlineData("avatar")]
    public async Task Actual_game_encoder_late_invalid_pet_skill_extra_or_avatar_sends_no_reset(string invalid)
    {
        // Network tests cannot reference Protocol internals. Resolve the actual registered CLR encoders,
        // rather than copying their byte rules into a stand-in encoder.
        var assembly = typeof(Dfo.Server.Protocol.ProtocolModule).Assembly;
        Type MessageType(string name) => assembly.GetType("Dfo.Server.Protocol.Game.StateSynchronization." + name, true)!;
        IServerNetworkMessage message;
        if (invalid == "pets")
            message = (IServerNetworkMessage)Activator.CreateInstance(MessageType("CreatureListMessage"),
                [Enumerable.Range(0, 256).Select(i => new CharacterPetRuntimeState(i, 50, 0, 0, 0, 0, 1, [], 0)).ToArray()])!;
        else if (invalid == "avatar")
            message = (IServerNetworkMessage)Activator.CreateInstance(MessageType("InventorySnapshotMessage"),
                new object[] { new InventoryContainerState(InventoryContainerType.Avatar, 0,
                    [new InventoryItemState(new(1), 0, StackableItemCore.Create(8, 1000, 1))]),
                    Array.Empty<InventoryRuntimeValueState>(), Array.Empty<InventoryRuntimeValueState>(), (ushort)0, 0, DateTimeOffset.UnixEpoch })!;
        else
        {
            var skills = invalid == "skills"
                ? Enumerable.Range(0, 256).Select(i => new CharacterSkillState(checked((short)i), 1, 1, [])).ToArray()
                : [new CharacterSkillState(0, 1, 1, new byte[256])];
            message = (IServerNetworkMessage)Activator.CreateInstance(MessageType("SkillStateMessage"),
                [new CharacterSkillsEntryState([new(0, skills), new(1, [])], [], new(0, 0), null, null, new(0, 0, 0, 0))])!;
        }
        var encoderType = assembly.GetType("Dfo.Server.Protocol.Game.StateSynchronization.GameNotiStateSynchronizationEncoder`1", true)!
            .MakeGenericType(message.GetType());
        var services = new ServiceCollection();
        services.AddServerMessageEncoder<Probe, ProbeEncoder>(NetworkEndpointKind.Game);
        services.AddSingleton(encoderType); services.AddSingleton(new ServerMessageEncoderRegistration(message.GetType(), encoderType, NetworkEndpointKind.Game));
        services.AddSingleton<ServerMessageEncoderRegistry>();
        using var provider = services.BuildServiceProvider();
        var registry = new TouchSocketConnectionRegistry(); var transport = Session("actual");
        var context = registry.Add(transport, NetworkEndpointKind.Game);
        var sender = new TouchSocketNetworkMessageSender(registry, provider.GetRequiredService<ServerMessageEncoderRegistry>(),
            NullLogger<TouchSocketNetworkMessageSender>.Instance);
        await Assert.ThrowsAnyAsync<Exception>(async () => await sender.SendBatchAsync(context, [new Probe(0), message], default));
        Assert.Empty(((SessionProxy)(object)transport).Frames);
    }

    [Fact]
    public async Task Equal_id_context_replacement_is_refused_and_encoding_time_replacement_keeps_the_old_transport()
    {
        var registry = new TouchSocketConnectionRegistry(); var old = Session("same"); var replacement = Session("same");
        var original = registry.Add(old, NetworkEndpointKind.Game);
        var encoder = new ReplacingEncoder { OnEncode = () => { registry.TryRemove(original.ConnectionId, out _); registry.Add(replacement, NetworkEndpointKind.Game); } };
        var services = new ServiceCollection();
        services.AddServerMessageEncoder<Probe, ReplacingEncoder>(NetworkEndpointKind.Game); services.AddSingleton(encoder);
        services.AddSingleton<ServerMessageEncoderRegistry>();
        using var provider = services.BuildServiceProvider();
        var sender = new TouchSocketNetworkMessageSender(registry, provider.GetRequiredService<ServerMessageEncoderRegistry>(),
            NullLogger<TouchSocketNetworkMessageSender>.Instance);
        await sender.SendBatchAsync(original, [new Probe(1), new Probe(2)], default);
        Assert.Equal(2, ((SessionProxy)(object)old).Frames.Count);
        Assert.Empty(((SessionProxy)(object)replacement).Frames);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sender.SendBatchAsync(original, [new Probe(3)], default));
        Assert.Empty(((SessionProxy)(object)replacement).Frames);
    }

    [Fact]
    public async Task Buffers_are_owned_and_transport_failure_can_stop_a_partially_sent_batch()
    {
        var registry = new TouchSocketConnectionRegistry(); var transport = Session("buffers");
        var context = registry.Add(transport, NetworkEndpointKind.Game);
        var services = new ServiceCollection(); services.AddServerMessageEncoder<Probe, ReusedBufferEncoder>(NetworkEndpointKind.Game);
        services.AddSingleton<ServerMessageEncoderRegistry>(); using var provider = services.BuildServiceProvider();
        var sender = new TouchSocketNetworkMessageSender(registry, provider.GetRequiredService<ServerMessageEncoderRegistry>(),
            NullLogger<TouchSocketNetworkMessageSender>.Instance);
        await sender.SendBatchAsync(context, [new Probe(1), new Probe(2)], default);
        Assert.Equal(new byte[] { 1, 2 }, ((SessionProxy)(object)transport).Frames.Select(x => x[0]).ToArray());
        ((SessionProxy)(object)transport).FailAfter = 3;
        await Assert.ThrowsAsync<IOException>(async () => await sender.SendBatchAsync(context, [new Probe(3), new Probe(4)], default));
        Assert.Equal(3, ((SessionProxy)(object)transport).Frames.Count);
    }
    [Fact]
    public async Task A_late_encoding_failure_sends_no_earlier_reset_frame()
    {
        var registry = new TouchSocketConnectionRegistry();
        var transport = Session("original");
        var context = registry.Add(transport, NetworkEndpointKind.Game);
        using var provider = Provider();
        var sender = new TouchSocketNetworkMessageSender(registry,
            provider.GetRequiredService<ServerMessageEncoderRegistry>(),
            NullLogger<TouchSocketNetworkMessageSender>.Instance);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await sender.SendBatchAsync(context, [new Probe(0), new Probe(-1)], default));
        Assert.Empty(((SessionProxy)(object)transport).Frames);
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddServerMessageEncoder<Probe, ProbeEncoder>(NetworkEndpointKind.Game);
        services.AddSingleton<ServerMessageEncoderRegistry>();
        return services.BuildServiceProvider();
    }

    private sealed record Probe(int Value) : IServerNetworkMessage
    {
        public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
    }
    private sealed record Other(int Length) : IServerNetworkMessage { public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization; }
    private sealed class OtherEncoder : ServerMessageEncoder<Other> { protected override IReadOnlyList<byte[]> EncodeMessage(Other message) => [new byte[message.Length]]; }
    private sealed class ReplacingEncoder : ServerMessageEncoder<Probe>
    {
        public Action? OnEncode { get; set; }
        protected override IReadOnlyList<byte[]> EncodeMessage(Probe message)
        { var action = OnEncode; OnEncode = null; action?.Invoke(); return [[checked((byte)message.Value)]]; }
    }
    private sealed class ReusedBufferEncoder : ServerMessageEncoder<Probe>
    {
        private readonly byte[] buffer = new byte[1];
        protected override IReadOnlyList<byte[]> EncodeMessage(Probe message) { buffer[0] = checked((byte)message.Value); return [buffer]; }
    }
    private sealed class ProbeEncoder : ServerMessageEncoder<Probe>
    {
        protected override IReadOnlyList<byte[]> EncodeMessage(Probe message) => message.Value < 0
            ? throw new InvalidDataException("late encoding") : [[0, 0, 0, checked((byte)message.Value)]];
    }
    public interface ITestSession : ITcpSession, ITcpSessionClient;
    private static ITestSession Session(string id)
    {
        var result = DispatchProxy.Create<ITestSession, SessionProxy>();
        ((SessionProxy)(object)result).Id = id;
        return result;
    }
    public class SessionProxy : DispatchProxy
    {
        public string Id { get; set; } = "";
        public List<byte[]> Frames { get; } = [];
        public int FailAfter { get; set; } = int.MaxValue;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_Id") return Id;
            if (method.Name is "get_LocalEndPoint" or "get_RemoteEndPoint")
                return new IPEndPoint(IPAddress.Loopback, 10011);
            if (method.Name == "SendAsync")
            {
                if (Frames.Count >= FailAfter) throw new IOException("transport gone after a partial projection");
                Frames.Add(((ReadOnlySequence<byte>)args![0]!).ToArray());
                return method.ReturnType == typeof(ValueTask) ? ValueTask.CompletedTask : Task.CompletedTask;
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
