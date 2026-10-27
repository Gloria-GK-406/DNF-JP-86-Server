
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
        var context = DomainCommandFixture.Context(Guid.NewGuid(),typeof(GameSelectDungeonCommand),typeof(GameSelectDungeonCommandHandler)) with { Request=request };
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
    private sealed class Handler(IServiceProvider p)
    {
        private readonly GameSelectDungeonCommandHandler handler = new GameSelectDungeonCommandHandler((p.GetService<ISelectedSessionDomainService>() ?? ProtocolAlgorithmFixture.Default<ISelectedSessionDomainService>()), (p.GetService<ISelectedCharacterCurrentViewDomainService>() ?? ProtocolAlgorithmFixture.Default<ISelectedCharacterCurrentViewDomainService>()), (p.GetService<IOnlineCharacterRuntimeDomainService>() ?? ProtocolAlgorithmFixture.Default<IOnlineCharacterRuntimeDomainService>()), (p.GetService<ITownAreaDomainService>() ?? ProtocolAlgorithmFixture.Default<ITownAreaDomainService>()), (p.GetService<IPartyService>() ?? ProtocolAlgorithmFixture.Default<IPartyService>()), new Dfo.Server.Protocol.Game.Party.PartyProjectionStreamRegistry(), (p.GetService<ICharacterStateProjectionDomainService>() ?? ProtocolAlgorithmFixture.Default<ICharacterStateProjectionDomainService>()), (p.GetService<ICharacterDomainService>() ?? ProtocolAlgorithmFixture.Default<ICharacterDomainService>()), (p.GetService<ITownMovementDomainService>() ?? ProtocolAlgorithmFixture.Default<ITownMovementDomainService>()), (p.GetService<IDungeonRunProjectionDomainService>() ?? ProtocolAlgorithmFixture.Default<IDungeonRunProjectionDomainService>()), (p.GetService<ICommandProjections>() ?? ProtocolAlgorithmFixture.Default<ICommandProjections>()), new NullOutbox(), (p.GetService<ICommandWorkUnits>() ?? ProtocolAlgorithmFixture.Default<ICommandWorkUnits>()), (p.GetService<ICommandConnectionClosures>() ?? ProtocolAlgorithmFixture.Default<ICommandConnectionClosures>()), (p.GetService<IOnlineUserQueryDomainService>() ?? global::Dfo.Server.Testing.ProtocolAlgorithmFixture.QueryFor(p.GetService<ISelectedSessionDomainService>()!)), Microsoft.Extensions.Logging.Abstractions.NullLogger<GameSelectDungeonCommandHandler>.Instance, (p.GetService<IBossTowerDomainService>() ?? ProtocolAlgorithmFixture.Default<IBossTowerDomainService>()), (p.GetService<IIllusionTowerDomainService>() ?? ProtocolAlgorithmFixture.Default<IIllusionTowerDomainService>()), (p.GetService<IMarriageHousingQueryDomainService>() ?? ProtocolAlgorithmFixture.Default<IMarriageHousingQueryDomainService>()), TimeProvider.System, (p.GetService<IDungeonVisitDomainService>() ?? ProtocolAlgorithmFixture.Default<IDungeonVisitDomainService>()), (p.GetService<IDungeonRunDomainService>() ?? ProtocolAlgorithmFixture.Default<IDungeonRunDomainService>()), (p.GetService<IDungeonEntryIntentDomainService>() ?? ProtocolAlgorithmFixture.Default<IDungeonEntryIntentDomainService>()), (p.GetService<ICharacterQuestLogDomainService>() ?? ProtocolAlgorithmFixture.Default<ICharacterQuestLogDomainService>()), (p.GetService<ICharacterFatigueDomainService>() ?? ProtocolAlgorithmFixture.Default<ICharacterFatigueDomainService>()), (p.GetService<IInventorySlotProjectionDomainService>() ?? ProtocolAlgorithmFixture.Default<IInventorySlotProjectionDomainService>()), (p.GetService<IOutboundOrdering>() ?? ProtocolAlgorithmFixture.Default<IOutboundOrdering>()), (p.GetService<IDungeonEntryDecisionDomainService>() ?? ProtocolAlgorithmFixture.Default<IDungeonEntryDecisionDomainService>()), (p.GetService<IHellDungeonEntryDomainService>() ?? ProtocolAlgorithmFixture.Default<IHellDungeonEntryDomainService>()), (p.GetService<ICycleDungeonEntryDomainService>() ?? ProtocolAlgorithmFixture.Default<ICycleDungeonEntryDomainService>()), (p.GetService<IMirrorDungeonEntryDomainService>() ?? ProtocolAlgorithmFixture.Default<IMirrorDungeonEntryDomainService>()), (p.GetService<IDimensionDungeonDomainService>() ?? ProtocolAlgorithmFixture.NoDimension(p.GetRequiredService<IDungeonVisitDomainService>())), (p.GetService<IDespairTowerDomainService>() ?? ProtocolAlgorithmFixture.Default<IDespairTowerDomainService>()), (p.GetService<ITournamentDungeonDomainService>() ?? ProtocolAlgorithmFixture.Default<ITournamentDungeonDomainService>()), (p.GetService<IAltarDungeonDomainService>() ?? ProtocolAlgorithmFixture.Default<IAltarDungeonDomainService>()), (p.GetService<ISaoTowerDomainService>() ?? ProtocolAlgorithmFixture.Default<ISaoTowerDomainService>()), (p.GetService<ITauKingdomDomainService>() ?? ProtocolAlgorithmFixture.Default<ITauKingdomDomainService>()), (p.GetService<IHerasDungeonDomainService>() ?? ProtocolAlgorithmFixture.Default<IHerasDungeonDomainService>()), (p.GetService<ICoupleDungeonDomainService>() ?? ProtocolAlgorithmFixture.Default<ICoupleDungeonDomainService>()), (p.GetService<ISouthernDaleDomainService>() ?? ProtocolAlgorithmFixture.Default<ISouthernDaleDomainService>()), (p.GetService<ISealedTreasureDomainService>() ?? ProtocolAlgorithmFixture.Default<ISealedTreasureDomainService>()), (p.GetService<IGoldSilverHornDomainService>() ?? ProtocolAlgorithmFixture.Default<IGoldSilverHornDomainService>()), (p.GetService<IGiantDragonDomainService>() ?? ProtocolAlgorithmFixture.Default<IGiantDragonDomainService>()));
        internal ValueTask<DungeonEntryAttempt> Enter(DungeonEntryContext entry, CancellationToken token) => handler.RouteEntryAsync(entry, token);
    }

    private sealed class NullOutbox : IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId c,IGameCmdMessage m,CancellationToken t)=>throw new NotSupportedException();
        public ValueTask RegisterNotiAsync(NetworkConnectionId c,IGameNotiMessage m,CancellationToken t)=>throw new NotSupportedException();
        public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken t)=>throw new NotSupportedException();
        public ValueTask RegisterExplicitAsync(NetworkConnectionId c,GameExplicitMessageDelivery m,CancellationToken t)=>throw new NotSupportedException();
    }
}
