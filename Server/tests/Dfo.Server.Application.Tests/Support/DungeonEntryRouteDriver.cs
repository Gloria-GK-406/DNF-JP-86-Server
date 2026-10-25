using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Testing;
namespace Dfo.Server.Application.Tests.Support;
// Directly exercises the production mode-routing algorithm for pre-existing domain fixtures.
// It intentionally does not stand in for decoder/dispatcher/outbox/scope wire oracles.
internal static class DungeonEntryRouteDriver
{
    internal static async ValueTask<bool> WholeBodyAsync(IServiceProvider services, SelectedSessionIdentity session, int dungeon = 11001, CancellationToken token = default)
    {
        var intents = services.GetRequiredService<IDungeonEntryIntentDomainService>();
        intents.RestoreIfAbsent(session, DungeonEntryIntent.Directed(dungeon));
        services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(session.ConnectionId, out var user);
        var dispatch = new NetworkDispatchContext(user!.Connection);
        var input = new SelectDungeonRequest(GameMessageMetadata.Empty,dungeon,0,0,0);
        var request = new CommandRequestContext.ProtocolNonEffect(dispatch,input,new ProtocolFrameMetadata.Game(input.Metadata),()=>true);
        var context = new CommandExecutionContext(Guid.NewGuid(),typeof(GameSelectDungeonCommand),typeof(GameSelectDungeonCommandHandler),CommandExecutionKind.Direct) { Request=request };
        var handler = ActivatorUtilities.CreateInstance<GameSelectDungeonCommandHandler>(services);
        var result = await handler.HandleAsync(context,new(dispatch,input),token);
        await request.DrainOperationsAsync(); request.Close();
        return result;
    }

    internal static ValueTask<DungeonEntryAttempt> EnterAsync(IServiceProvider services, DungeonEntryContext entry, CancellationToken token = default) =>
        new(TestGameplay.RunAsync(services, async () => {
            var scope=services.GetRequiredService<DomainTransactionScope>();
            if(scope.Current is not null) return await new Handler(services).Enter(entry,token);
            await using var tx=scope.Begin(); var result=await new Handler(services).Enter(entry,token); await tx.CommitAsync(token); return result;
        }));
    private sealed class Handler(IServiceProvider p) : GameSelectDungeonCommandHandler(p.GetService<ISelectedSessionDomainService>()!, p.GetService<ISelectedCharacterCurrentViewDomainService>()!, p.GetService<IOnlineCharacterRuntimeDomainService>()!, p.GetService<ITownAreaDomainService>()!, p.GetService<IPartyService>()!, new Dfo.Server.Protocol.Game.Party.PartyProjectionStreamRegistry(), p.GetService<ICharacterStateProjectionDomainService>()!, p.GetService<ICharacterDomainService>()!, p.GetService<ITownMovementDomainService>()!, p.GetService<IDungeonRunProjectionDomainService>()!, p.GetService<ICommandProjections>()!, new NullOutbox(), p.GetService<ICommandWorkUnits>()!, p.GetService<ICommandConnectionClosures>()!, p.GetService<IOnlineUserQueryDomainService>()!, Microsoft.Extensions.Logging.Abstractions.NullLogger<GameSelectDungeonCommandHandler>.Instance, p.GetService<IBossTowerDomainService>()!, p.GetService<IIllusionTowerDomainService>()!, p.GetService<IMarriageHousingQueryDomainService>()!, TimeProvider.System, p.GetService<IDungeonVisitDomainService>()!, p.GetService<IDungeonRunDomainService>()!, p.GetService<IDungeonEntryIntentDomainService>()!, p.GetService<ICharacterQuestLogDomainService>()!, p.GetService<ICharacterFatigueDomainService>()!, p.GetService<IInventorySlotProjectionDomainService>()!, p.GetService<IOutboundOrdering>()!, p.GetService<IDungeonEntryDecisionDomainService>()!, p.GetService<IHellDungeonEntryDomainService>()!, p.GetService<ICycleDungeonEntryDomainService>()!, p.GetService<IMirrorDungeonEntryDomainService>()!, p.GetService<IDimensionDungeonDomainService>()!, p.GetService<IDespairTowerDomainService>()!, p.GetService<ITournamentDungeonDomainService>()!, p.GetService<IAltarDungeonDomainService>()!, p.GetService<ISaoTowerDomainService>()!, p.GetService<ITauKingdomDomainService>()!, p.GetService<IHerasDungeonDomainService>()!, p.GetService<ICoupleDungeonDomainService>()!, p.GetService<ISouthernDaleDomainService>()!, p.GetService<ISealedTreasureDomainService>()!, p.GetService<IGoldSilverHornDomainService>()!)
    { internal ValueTask<DungeonEntryAttempt> Enter(DungeonEntryContext entry,CancellationToken token)=>RouteEntryAsync(entry,token); }
    private sealed class NullOutbox : IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId c,IGameCmdMessage m,CancellationToken t)=>throw new NotSupportedException();
        public ValueTask RegisterNotiAsync(NetworkConnectionId c,IGameNotiMessage m,CancellationToken t)=>throw new NotSupportedException();
        public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken t)=>throw new NotSupportedException();
        public ValueTask RegisterExplicitAsync(NetworkConnectionId c,GameExplicitMessageDelivery m,CancellationToken t)=>throw new NotSupportedException();
    }
}
