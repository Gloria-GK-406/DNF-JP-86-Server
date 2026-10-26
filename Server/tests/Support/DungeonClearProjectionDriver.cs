using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
namespace Dfo.Server.Protocol.Game.DungeonClear;

// The old component fixtures freeze recipients themselves. Real CMD tests exercise the current
// party resolver; this driver shares only the production clear projection algorithm.
internal sealed class DungeonClearProjectionDriver(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry registry, IGameCommandOutbox outbox, IQuestQueryDomainService quests,
    DungeonPartyRecipients? partyRecipients = null)
{
    public ValueTask<int> DeliverAsync(SelectedSessionIdentity requester, DungeonClearChange change, CancellationToken token) =>
        DungeonClearCommandProjection.DeliverAsync(sessions, runs,
            partyRecipients is null ? registry.Find(change.RunId) : partyRecipients.Find(requester, change.RunId),
            quests, outbox, requester, change, token);
}
