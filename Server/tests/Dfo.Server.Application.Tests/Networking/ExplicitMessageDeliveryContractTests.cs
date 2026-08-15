using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Tests.Networking;

public sealed class ExplicitMessageDeliveryContractTests
{
    [Fact]
    public async Task ApplicationCaller_DeliversTypedValuesAndExplicitRecipientsWithoutFrames()
    {
        var sender = new RecordingSender();
        var owner = new NetworkConnectionId("owner");
        var target = new NetworkConnectionId("target");
        var direct = new TestMessage(3);
        var publication = new TestMessage(9);

        await ((INetworkMessageSender)sender).SendExplicitAsync(
            owner,
            new ExplicitMessageDelivery(
                [direct],
                [new ExplicitMessageTarget(target, [publication])]),
            CancellationToken.None);

        Assert.Equal(owner, sender.ConnectionId);
        Assert.Same(direct, Assert.Single(sender.Delivery!.RequiredDirectMessages));
        var addressed = Assert.Single(sender.Delivery.BestEffortTargets);
        Assert.Equal(target, addressed.ConnectionId);
        Assert.Same(publication, Assert.Single(addressed.OrderedMessages));
    }

    private sealed class RecordingSender : INetworkMessageSender
    {
        public NetworkConnectionId ConnectionId { get; private set; }
        public ExplicitMessageDelivery? Delivery { get; private set; }

        public ValueTask SendAsync(NetworkConnectionId connectionId, IServerNetworkMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "An explicit delivery plan must use the explicit-recipient path.");

        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId,
            ExplicitMessageDelivery delivery, CancellationToken cancellationToken)
        {
            ConnectionId = requiredDirectConnectionId;
            Delivery = delivery;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record TestMessage(byte Value) : IServerNetworkMessage
    {
        public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
    }
}
