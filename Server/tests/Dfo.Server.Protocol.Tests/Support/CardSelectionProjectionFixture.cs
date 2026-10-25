using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.Dungeons;
namespace Dfo.Server.Protocol.Tests.Support;
internal sealed class CardSelectionProjectionFixture(ISelectedSessionDomainService sessions,IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveries,IGameMessageSender sender,DungeonPartyRecipients? parties=null)
{
    internal async ValueTask<int> DeliverAsync(SelectedSessionIdentity actor,SelectCardResult result,DateTimeOffset at,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);
        var change=result.Change??throw new ArgumentException("A successful card projection requires its committed change.",nameof(result));
        var delivery=DungeonCardSelectionProjection.CreateDelivery(sessions,runs,
            parties?.Find(actor,change.RunId)??deliveries.Find(change.RunId),actor,result,at,out var count);
        if(delivery is null)return 0;
        await sender.SendExplicitAsync(actor.ConnectionId,delivery,ct);return count;
    }
}
