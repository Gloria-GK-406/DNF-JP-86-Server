using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Protocol.Game.GroundPickup;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Protocol.Tests.Support;
// Domain fixture only: the exact private whole pickup algorithm, without production request lifecycle.
internal sealed class GroundPickupAlgorithmDriver(IDungeonRunDomainService runs,IGroundPickupDomainService floor,
    ICharacterContainerDomainService containers,ICharacterDomainService characters,IQuestProgressDomainService progress,
    IItemGrantDomainService grants,IEpicBookDomainService? epicBook=null)
    : GameGetItemCommandHandler(null!,runs,null!,null!,null!,floor,containers,characters,progress,grants,epicBook!,null!,null!,null!,null!,null!,new NoProjectionOutbox(),null!,TimeProvider.System,null!,NullLogger<GameGetItemCommandHandler>.Instance);

internal sealed class NoProjectionOutbox:Dfo.Server.Protocol.Game.IGameCommandOutbox
{
    public ValueTask RegisterExplicitAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.GameExplicitMessageDelivery d,CancellationToken ct)=>throw new InvalidOperationException("Algorithm driver cannot project.");
    public ValueTask RegisterCmdAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.IGameCmdMessage m,CancellationToken ct)=>throw new InvalidOperationException("Algorithm driver cannot project.");
    public ValueTask RegisterNotiAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.IGameNotiMessage m,CancellationToken ct)=>throw new InvalidOperationException("Algorithm driver cannot project.");
    public ValueTask RegisterBatchAsync(Dfo.Server.Data.Networking.NetworkConnectionContext c,IReadOnlyList<Dfo.Server.Protocol.Game.IGameServerMessage> m,CancellationToken ct)=>throw new InvalidOperationException("Algorithm driver cannot project.");
}
