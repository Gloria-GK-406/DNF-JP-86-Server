using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Tests.Networking;

public sealed class SelectCharacterProjectionContractTests
{
    [Fact]
    public async Task ApplicationCaller_DeliversTypedValuesAndExplicitRecipientsWithoutFrames()
    {
        var sender = new RecordingSender();
        var owner = new NetworkConnectionId("owner");
        var target = new NetworkConnectionId("target");
        var direct = new SelectCharacterVisibilityProjection(7, 3);
        var publication = new SelectCharacterTownLeaveProjection(9);

        await ((INetworkMessageSender)sender).SendProjectionAsync(
            owner,
            new SelectCharacterProjectionDelivery(
                [direct],
                [new ExplicitProjectionTarget(target, [publication])]),
            CancellationToken.None);

        Assert.Equal(owner, sender.ConnectionId);
        Assert.Same(direct, Assert.Single(sender.Delivery!.RequiredDirectProjections));
        var addressed = Assert.Single(sender.Delivery.BestEffortTargets);
        Assert.Equal(target, addressed.ConnectionId);
        Assert.Same(publication, Assert.Single(addressed.OrderedProjections));
    }

    private sealed class RecordingSender : INetworkMessageSender
    {
        public NetworkConnectionId ConnectionId { get; private set; }
        public SelectCharacterProjectionDelivery? Delivery { get; private set; }

        public ValueTask SendAsync(NetworkConnectionId connectionId, IServerNetworkMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Projection delivery must not travel through the message send path.");

        public ValueTask SendProjectionAsync(NetworkConnectionId requiredDirectConnectionId,
            SelectCharacterProjectionDelivery delivery, CancellationToken cancellationToken)
        {
            ConnectionId = requiredDirectConnectionId;
            Delivery = delivery;
            return ValueTask.CompletedTask;
        }
    }
}
