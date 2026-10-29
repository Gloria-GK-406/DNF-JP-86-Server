using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Protocol.Game.GroundPickup;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Application.Tests.Support;
// Domain fixture only: the exact private whole pickup algorithm, without production request lifecycle.
internal sealed class GroundPickupAlgorithmDriver(IDungeonRunDomainService runs,
    ICharacterContainerDomainService containers,ICharacterDomainService characters,IQuestProgressDomainService progress,
    IItemGrantDomainService grants,IEpicBookDomainService? epicBook=null)
{
    private readonly GameGetItemCommandHandler command = ProtocolAlgorithmFixture.Create<GameGetItemCommandHandler>(
        runs, new Dfo.Server.Application.World.GroundPickupDomainService(runs, characters,
            epicBook ?? ProtocolAlgorithmFixture.Default<IEpicBookDomainService>(), containers, grants, progress),
        new NoProjectionOutbox());
    internal ValueTask<GroundPickupResult> PickUpAsync(GroundPickupInput input, CancellationToken ct) =>
        command.PickUpAsync(input, ct);
}

internal sealed class NoProjectionOutbox:Dfo.Server.Protocol.Game.IGameCommandOutbox
{
    public ValueTask RegisterExplicitAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.GameExplicitMessageDelivery d,CancellationToken ct)=>throw new InvalidOperationException("Algorithm driver cannot project.");
    public ValueTask RegisterCmdAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.IGameCmdMessage m,CancellationToken ct)=>throw new InvalidOperationException("Algorithm driver cannot project.");
    public ValueTask RegisterNotiAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,Dfo.Server.Protocol.Game.IGameNotiMessage m,CancellationToken ct)=>throw new InvalidOperationException("Algorithm driver cannot project.");
    public ValueTask RegisterBatchAsync(Dfo.Server.Data.Networking.NetworkConnectionContext c,IReadOnlyList<Dfo.Server.Protocol.Game.IGameServerMessage> m,CancellationToken ct)=>throw new InvalidOperationException("Algorithm driver cannot project.");
}
