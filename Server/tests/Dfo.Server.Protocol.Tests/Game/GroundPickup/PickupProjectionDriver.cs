using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Testing;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Protocol.Tests.Game.GroundPickup;
// Algorithm-only drivers preserve existing assertions; they do not establish production request scopes.
internal sealed class PickupProjectionDriver(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
    DungeonRunState run, DungeonRunDeliveryRegistry registry, IGameMessageSender sender, TimeProvider clock)
{
    private readonly GameGetItemCommandHandler command = ProtocolAlgorithmFixture.Create<GameGetItemCommandHandler>(
        sessions, runs, registry, new Dfo.Server.Protocol.Tests.Support.DungeonProjectionParty(run),
        new PickupOutbox(sender), clock);
    internal ValueTask<bool> DeliverAsync(SelectedSessionIdentity session, GroundPickupResult result, CancellationToken ct) =>
        command.DeliverPickupAsync(session, result, ct);
}
internal sealed class PickupOutbox(IGameMessageSender sender):IGameCommandOutbox
{
    public ValueTask RegisterExplicitAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,GameExplicitMessageDelivery batch,CancellationToken ct)=>sender.SendExplicitAsync(id,batch,ct);
    public ValueTask RegisterCmdAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,IGameCmdMessage message,CancellationToken ct)=>sender.SendCmdAsync(id,message,ct);
    public ValueTask RegisterNotiAsync(Dfo.Server.Data.Networking.NetworkConnectionId id,IGameNotiMessage message,CancellationToken ct)=>sender.SendNotiAsync(id,message,ct);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext connection,IReadOnlyList<IGameServerMessage> messages,CancellationToken ct)=>sender.SendExplicitAsync(connection.ConnectionId,new(messages,[]),ct);
}
internal sealed class PickupClearDriver(IDungeonClearDomainService clear,DungeonClearProjectionDriver projection)
{
    internal ValueTask<DungeonClearChange?> EvaluateAsync(SelectedSessionIdentity session,GroundPickupResult result)=>GameGetItemCommandHandler.EvaluateClearAsync(clear,session,result);
    internal ValueTask<int> ProjectAsync(SelectedSessionIdentity session,DungeonClearChange change,CancellationToken ct)=>projection.DeliverAsync(session,change,ct);
}
internal sealed record GroundPickupDomainCommand(AccountId AccountId,CharacterId CharacterId,ushort GroundDropNumber,DungeonRunId ExpectedRunId=default,long? ExpectedRoomVersion=null):ICommand<GroundPickupResult>;
internal sealed class GroundPickupDomainCommandHandler(IServiceProvider services):ICommandHandler<GroundPickupDomainCommand,GroundPickupResult>
{
    public ValueTask<GroundPickupResult> HandleAsync(CommandExecutionContext c,GroundPickupDomainCommand input,CancellationToken ct)=>
        ActivatorUtilities.CreateInstance<Dfo.Server.Protocol.Tests.Support.GroundPickupAlgorithmDriver>(services).PickUpAsync(new(input.AccountId,input.CharacterId,input.GroundDropNumber,input.ExpectedRunId,input.ExpectedRoomVersion),ct);
}
