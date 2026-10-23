using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Networking.Tests.Outbound;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// An explicit delivery as the outbox registers it (spec §5 table): the direct messages as one Required
/// batch for their connection, then one BestEffort batch per other target in plan order, the owner left
/// out. What a failed connection then costs is the sender's business (OutboundSenderTests).
/// </summary>
/// <remarks>
/// Moved from TouchSocketNetworkMessageSenderExplicitDeliveryTests when the direct sender was deleted
/// (TASK-23); the method names are kept so the rewrite can be followed (F-87).
/// </remarks>
public sealed class OutboxExplicitDeliveryTests
{
    [Fact]
    public async Task ExplicitDelivery_PreservesRequiredAndPerTargetOrderAndExcludesOwner()
    {
        var connections = new FixedConnections();
        var owner = connections.Add("owner");
        connections.Add("a");
        connections.Add("b");
        var delivery = new ExplicitMessageDelivery(
            [P(1), P(2)],
            [
                new ExplicitMessageTarget(owner.ConnectionId, [P(99)]),
                new ExplicitMessageTarget(Id("a"), [P(3), P(4)]),
                new ExplicitMessageTarget(Id("b"), [P(5)]),
            ]);
        await using var host = new OutboxRegistrationHost(connections, new ByteEncoder());

        await host.RunAsync(() => host.Sender.SendExplicitAsync(owner.ConnectionId, delivery, CancellationToken.None).AsTask());

        Assert.Equal(["owner:1+2 Required", "a:3+4 BestEffort", "b:5 BestEffort"], host.Entries.Select(Describe));
    }

    [Fact]
    public async Task ExplicitDelivery_RequiredDirectFailurePropagatesAndStopsDelivery()
    {
        var connections = new FixedConnections();
        var owner = connections.Add("owner");
        connections.Add("a");
        var delivery = new ExplicitMessageDelivery(
            [P(1), P(2), P(3)],
            [new ExplicitMessageTarget(Id("a"), [P(4)])]);
        await using var host = new OutboxRegistrationHost(connections, new ByteEncoder(refusing: 2));
        InvalidOperationException? error = null;

        await host.RunAsync(async () => error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.Sender.SendExplicitAsync(owner.ConnectionId, delivery, CancellationToken.None)));

        Assert.Equal("direct", error!.Message);
        Assert.Empty(host.Entries);
    }

    [Fact]
    public async Task ExplicitDelivery_BestEffortFailureStopsOnlyThatTarget()
    {
        var connections = new FixedConnections();
        var owner = connections.Add("owner");
        connections.Add("a");
        connections.Add("b");
        var delivery = new ExplicitMessageDelivery(
            [P(1)],
            [
                new ExplicitMessageTarget(Id("a"), [P(2), P(3), P(4)]),
                new ExplicitMessageTarget(Id("disconnected"), [P(5)]),
                new ExplicitMessageTarget(Id("b"), [P(6), P(7)]),
            ]);
        await using var host = new OutboxRegistrationHost(connections, new ByteEncoder(refusing: 3));

        await host.RunAsync(() => host.Sender.SendExplicitAsync(owner.ConnectionId, delivery, CancellationToken.None).AsTask());

        Assert.Equal(["owner:1 Required", "b:6+7 BestEffort"], host.Entries.Select(Describe));
    }

    private static string Describe(OutboundEntry entry) =>
        $"{entry.Connection.ConnectionId.Value}:{string.Join("+", entry.Messages.Select(message => ((TestMessage)message).Value))} {entry.Delivery}";

    private static NetworkConnectionId Id(string value) => new(value);

    private static TestMessage P(byte value) => new(value);

    private sealed record TestMessage(byte Value) : IServerNetworkMessage
    {
        public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
    }

    /// <summary>One byte per message; the refused value throws, as a message without a valid encoding does.</summary>
    private sealed class ByteEncoder(int refusing = -1) : IOutboundMessageEncoder
    {
        public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message) =>
            ((TestMessage)message).Value == refusing
                ? throw new InvalidOperationException("direct")
                : [((TestMessage)message).Value];

        public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages) =>
            [.. messages.SelectMany(message => EncodeMessage(connection, message))];
    }
}
