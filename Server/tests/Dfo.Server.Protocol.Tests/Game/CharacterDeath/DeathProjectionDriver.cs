using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Protocol.Tests.Game.CharacterDeath;
// Shared projection algorithm used by the owning CMD, not a simulated inbound scope.
internal sealed class DeathProjectionDriver(ISelectedSessionDomainService sessions,IGameMessageSender sender,
    IBossTowerDomainService? boss,IIllusionTowerDomainService? illusion)
    :PartyMutationProtocolCommandHandler(sessions,null,null,null!,null!,null,NullLogger.Instance,bossTowers:boss,
        illusionTowers:illusion,clock:TimeProvider.System,register:sender.SendExplicitAsync)
{
    internal ValueTask<IReadOnlyList<(SelectedSessionIdentity Session,Exception Error)>> BossAsync(BossTowerActionResult action,SelectedSessionIdentity reporter,CancellationToken ct)=>
        BossTowerProjectActionAsync(action,new CharacterLifeStateNotification(reporter.WireUserId),ct,reporter,new CharacterDeathResponse());
    internal ValueTask<IReadOnlyList<(SelectedSessionIdentity Session,Exception Error)>> IllusionAsync(TowerActionResult action,SelectedSessionIdentity reporter,CancellationToken ct)=>
        IllusionTowerProjectActionAsync(action,new CharacterLifeStateNotification(reporter.WireUserId),ct,reporter,new CharacterDeathResponse());
}
