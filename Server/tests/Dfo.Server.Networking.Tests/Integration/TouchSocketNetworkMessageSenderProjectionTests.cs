using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Game;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Networking.Runtime;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class TouchSocketNetworkMessageSenderProjectionTests
{
    [Fact]
    public async Task TouchSocketNetworkMessageSender_PreservesRequiredAndPerTargetOrderAndExcludesOwner()
    {
        var owner = Id("owner");
        var targetA = Id("a");
        var targetB = Id("b");
        var delivery = new SelectCharacterProjectionDelivery(
            [P(1), P(2)],
            [
                new ExplicitProjectionTarget(owner, [P(99)]),
                new ExplicitProjectionTarget(targetA, [P(3), P(4)]),
                new ExplicitProjectionTarget(targetB, [P(5)]),
            ]);
        var sent = new List<string>();

        await TouchSocketNetworkMessageSender.DeliverProjectionAsync(
            owner,
            delivery,
            new TestComposer(),
            (connectionId, frame, _) =>
            {
                sent.Add($"{connectionId.Value}:{frame.Span[0]}");
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
        var delivery = new SelectCharacterProjectionDelivery(
            [P(1), P(2), P(3)],
            [new ExplicitProjectionTarget(Id("a"), [P(4)])]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await TouchSocketNetworkMessageSender.DeliverProjectionAsync(
                owner,
                delivery,
                new TestComposer(),
                (connectionId, frame, _) =>
                {
                    attempts.Add($"{connectionId.Value}:{frame.Span[0]}");
                    return frame.Span[0] == 2
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
        var delivery = new SelectCharacterProjectionDelivery(
            [P(1)],
            [
                new ExplicitProjectionTarget(targetA, [P(2), P(3), P(4)]),
                new ExplicitProjectionTarget(Id("disconnected"), [P(5)]),
                new ExplicitProjectionTarget(Id("b"), [P(6), P(7)]),
            ]);

        await TouchSocketNetworkMessageSender.DeliverProjectionAsync(
            owner,
            delivery,
            new TestComposer(),
            (connectionId, frame, _) =>
            {
                attempts.Add($"{connectionId.Value}:{frame.Span[0]}");
                if ((connectionId == targetA && frame.Span[0] == 3) ||
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
    private static TestProjection P(byte value) => new(value);
    private sealed record TestProjection(byte Value) : ISelectCharacterSupplementalProjection;
    private sealed class TestComposer : ISelectCharacterProjectionComposer
    {
        public IReadOnlyList<byte[]> Compose(IReadOnlyList<ISelectCharacterSupplementalProjection> projections) =>
            projections.Cast<TestProjection>().Select(value => new[] { value.Value }).ToArray();
    }
}
