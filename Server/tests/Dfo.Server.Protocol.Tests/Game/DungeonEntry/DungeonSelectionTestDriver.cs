using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.World.IllusionTower;
using System;
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;
// Existing characterization keeps its fixture and drives the real whole CMD body.
internal static class DungeonSelectionTestDriver
{
    internal static EnterSelectDungeonHandler Enter(ICharacterStateProjectionDomainService current,ISelectedSessionDomainService sessions,ISelectedCharacterCurrentViewDomainService views,
        IDungeonVisitDomainService visits,IDungeonEntryIntentDomainService intents,IGameMessageSender sender,TownAreaProjectionDelivery town,
        ILogger<EnterSelectDungeonHandler> logger,IPartyService parties,IBossTowerDomainService? bossTower=null,IDarkElfRuinsDomainService? ruins=null)=>
        new(new Driver(new Handler(sessions,views,null!,null!,parties,current,visits,null!,null!,sender,town,intents,bossTower,ruins)));
    internal static BackToVillageHandler Back(ISelectedSessionDomainService sessions,ISelectedCharacterCurrentViewDomainService views,
        IOnlineCharacterRuntimeDomainService online,IDungeonRunDomainService runs,DungeonRunDeliveryRegistry registry,IGameMessageSender sender,TownAreaProjectionDelivery town,
        ILogger<BackToVillageHandler> logger,IPartyService parties,IDungeonEntryIntentDomainService? entryIntents=null)=>
        new(new Driver(new Handler(sessions,views,online,null!,parties,null!,null!,runs,registry,sender,town,entryIntents,null)));
    private sealed class Handler(ISelectedSessionDomainService sessions,ISelectedCharacterCurrentViewDomainService views,IOnlineCharacterRuntimeDomainService online,
        ITownAreaDomainService areas,IPartyService parties,ICharacterStateProjectionDomainService current,IDungeonVisitDomainService visits,IDungeonRunDomainService runs,
        IDungeonRunProjectionDomainService registry,IGameMessageSender sender,TownAreaProjectionDelivery town,IDungeonEntryIntentDomainService? intents,IBossTowerDomainService? boss,IDarkElfRuinsDomainService? ruins=null)

    {
        private readonly IBossTowerDomainService bossTowers = boss ?? (IBossTowerDomainService)ProtocolAlgorithmFixture.Default(typeof(IBossTowerDomainService));
        private readonly IIllusionTowerDomainService illusionTowers = ProtocolAlgorithmFixture.Default<IIllusionTowerDomainService>();
        internal GameDungeonSelectionCommandHandler Command => command ??= Build();
        private GameDungeonSelectionCommandHandler? command;
        // The registry is read off the same tower services the handler holds; the enter-only driver has no run service.
        private GameDungeonSelectionCommandHandler Build() => new GameDungeonSelectionCommandHandler(sessions,views,online ?? ProtocolAlgorithmFixture.TownField<IOnlineCharacterRuntimeDomainService>(town),areas ?? ProtocolAlgorithmFixture.TownField<ITownAreaDomainService>(town),parties,new PartyProjectionStreamRegistry(),current,ProtocolAlgorithmFixture.Default<ICharacterDomainService>(),ProtocolAlgorithmFixture.TownTransit(town, sessions, areas, online),visits,runs,ProtocolAlgorithmFixture.Exit(parties,runs),registry,global::Dfo.Server.Testing.ProtocolAlgorithmFixture.TownField<global::Dfo.Server.Abstractions.DomainEvents.ICommandProjections>(town),new Outbox(sender),new Units(),ProtocolAlgorithmFixture.Default<ICommandConnectionClosures>(),global::Dfo.Server.Testing.ProtocolAlgorithmFixture.QueryFor(sessions),NullLogger<GameDungeonSelectionCommandHandler>.Instance,intents ?? (IDungeonEntryIntentDomainService)ProtocolAlgorithmFixture.Default(typeof(IDungeonEntryIntentDomainService)),bossTowers:bossTowers,housing: ProtocolAlgorithmFixture.Default<IMarriageHousingQueryDomainService>(),illusionTowers: illusionTowers,participation: new DerivedDungeonParticipation(bossTowers,illusionTowers,runs ?? ProtocolAlgorithmFixture.Default<IDungeonRunDomainService>(),sessions),despair: ProtocolAlgorithmFixture.Default<IDespairTowerDomainService>(),sao: ProtocolAlgorithmFixture.Default<ISaoTowerDomainService>(),clock: ProtocolAlgorithmFixture.Default<TimeProvider>(),weakness: ProtocolAlgorithmFixture.TownField<IWeaknessDomainService>(town), ruins: ruins ?? ProtocolAlgorithmFixture.Default<IDarkElfRuinsDomainService>());
        internal void PrepareBack(NetworkConnectionId id)
        { if (intents is null && ProtocolAlgorithmFixture.Field<ISelectedSessionDomainService>(Command).FindByConnection(id) is { } session) ProtocolAlgorithmFixture.Field<IDungeonEntryIntentDomainService>(Command).Record(session, DungeonEntryIntent.Interactive); }
    }
    private sealed class Driver(Handler handler):ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public async ValueTask<T> SendAsync<T>(ICommand<T> value,CancellationToken token=default)where T:notnull
        {var command=(IProtocolCommand)value;if(value is GameBackToVillageCommand backInput)handler.PrepareBack(backInput.Dispatch.Connection.ConnectionId);var request=new CommandRequestContext.ProtocolNonEffect(command.Dispatch,command.Message,command.Frame,()=>true);var context=DomainCommandFixture.Context(Guid.NewGuid(),value.GetType(),handler.Command.GetType()) with{Request=request};var result=value switch{GameEnterSelectDungeonCommand enter=>await handler.Command.HandleAsync(context,enter,token),GameBackToVillageCommand back=>await handler.Command.HandleAsync(context,back,token),_=>throw new NotSupportedException()};return(T)(object)result;}
    }
    private sealed class Units:ICommandWorkUnits
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull{Assert.Equal(CommandWorkUnitKind.Ordinary,d.Kind);return a(null!,ct);}
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
    }
    private sealed class Outbox(IGameMessageSender sender):IGameCommandOutbox
    {public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage m,CancellationToken ct)=>sender.SendCmdAsync(id,m,ct);public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage m,CancellationToken ct)=>sender.SendNotiAsync(id,m,ct);public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken ct)=>sender.SendBatchAsync(c,m,ct);public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery d,CancellationToken ct)=>sender.SendExplicitAsync(id,d,ct);}
}
