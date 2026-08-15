using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Networking.Runtime;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class TouchSocketNetworkMessageSenderExplicitDeliveryTests
{
    [Fact]
    public async Task TouchSocketNetworkMessageSender_PreservesRequiredAndPerTargetOrderAndExcludesOwner()
    {
        var owner = Id("owner");
        var targetA = Id("a");
        var targetB = Id("b");
        var delivery = new ExplicitMessageDelivery(
            [P(1), P(2)],
            [
                new ExplicitMessageTarget(owner, [P(99)]),
                new ExplicitMessageTarget(targetA, [P(3), P(4)]),
                new ExplicitMessageTarget(targetB, [P(5)]),
            ]);
        var sent = new List<string>();

        await TouchSocketNetworkMessageSender.DeliverExplicitAsync(
            owner,
            delivery,
            (connectionId, message, _) =>
            {
                sent.Add($"{connectionId.Value}:{((TestMessage)message).Value}");
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(["owner:1", "owner:2", "a:3", "a:4", "b:5"], sent);
    }

    [Fact]
    public async Task TouchSocketNetworkMessageSender_RequiredDirectFailurePropagatesAndStopsDelivery()
    {
        var owner = Id("owner");
        var attempts = new List<string>();
        var delivery = new ExplicitMessageDelivery(
            [P(1), P(2), P(3)],
            [new ExplicitMessageTarget(Id("a"), [P(4)])]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await TouchSocketNetworkMessageSender.DeliverExplicitAsync(
                owner,
                delivery,
                (connectionId, message, _) =>
                {
                    var value = ((TestMessage)message).Value;
                    attempts.Add($"{connectionId.Value}:{value}");
                    return value == 2
                        ? ValueTask.FromException(new InvalidOperationException("direct"))
                        : ValueTask.CompletedTask;
                },
                CancellationToken.None));

        Assert.Equal("direct", error.Message);
        Assert.Equal(["owner:1", "owner:2"], attempts);
    }

    [Fact]
    public async Task TouchSocketNetworkMessageSender_BestEffortFailureStopsOnlyThatTarget()
    {
        var owner = Id("owner");
        var targetA = Id("a");
        var attempts = new List<string>();
        var delivery = new ExplicitMessageDelivery(
            [P(1)],
            [
                new ExplicitMessageTarget(targetA, [P(2), P(3), P(4)]),
                new ExplicitMessageTarget(Id("disconnected"), [P(5)]),
                new ExplicitMessageTarget(Id("b"), [P(6), P(7)]),
            ]);

        await TouchSocketNetworkMessageSender.DeliverExplicitAsync(
            owner,
            delivery,
            (connectionId, message, _) =>
            {
                var value = ((TestMessage)message).Value;
                attempts.Add($"{connectionId.Value}:{value}");
                if ((connectionId == targetA && value == 3) ||
                    connectionId.Value == "disconnected")
                {
                    return ValueTask.FromException(new InvalidOperationException("gone"));
                }

                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(["owner:1", "a:2", "a:3", "disconnected:5", "b:6", "b:7"], attempts);
    }

    private static NetworkConnectionId Id(string value) => new(value);
    private static TestMessage P(byte value) => new(value);
    private sealed record TestMessage(byte Value) : IServerNetworkMessage
    {
        public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
    }
}
