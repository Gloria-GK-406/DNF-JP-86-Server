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
        ILogger<EnterSelectDungeonHandler> logger,IPartyService parties,IBossTowerDomainService? bossTower=null)=>
        new(new Driver(new Handler(sessions,views,null!,null!,parties,current,visits,null!,null!,sender,town,intents,bossTower)));
    internal static BackToVillageHandler Back(ISelectedSessionDomainService sessions,ISelectedCharacterCurrentViewDomainService views,
        IOnlineCharacterRuntimeDomainService online,IDungeonRunDomainService runs,DungeonRunDeliveryRegistry registry,IGameMessageSender sender,TownAreaProjectionDelivery town,
        ILogger<BackToVillageHandler> logger,IPartyService parties,IDungeonEntryIntentDomainService? entryIntents=null)=>
        new(new Driver(new Handler(sessions,views,online,null!,parties,null!,null!,runs,registry,sender,town,entryIntents,null)));
    private sealed class Handler(ISelectedSessionDomainService sessions,ISelectedCharacterCurrentViewDomainService views,IOnlineCharacterRuntimeDomainService online,
        ITownAreaDomainService areas,IPartyService parties,ICharacterStateProjectionDomainService current,IDungeonVisitDomainService visits,IDungeonRunDomainService runs,
        IDungeonRunProjectionDomainService registry,IGameMessageSender sender,TownAreaProjectionDelivery town,IDungeonEntryIntentDomainService? intents,IBossTowerDomainService? boss)
        :GameDungeonSelectionCommandHandler(sessions,views,online,areas,parties,new PartyProjectionStreamRegistry(),current,null!,null!,visits,runs,registry,null!,new Outbox(sender),new Units(),null!,null!,NullLogger<GameDungeonSelectionCommandHandler>.Instance,intents,bossTowers:boss)
    {
        protected override ValueTask<IReadOnlyList<TownAreaMembershipChange>> TownLeaveManyAsync(IReadOnlyList<SelectedSessionIdentity> members,TownAreaDepartureReason reason,CancellationToken ct)=>town.LeaveManyAsync(members,reason,ct);
        protected override ValueTask<int> ReturnTownAsync(IReadOnlyList<TownAreaEntry> entries,CancellationToken ct)=>town.EnterAsync(entries,ct);
        protected override ValueTask SelectionQuotaAsync(SelectedSessionIdentity session,CancellationToken ct)=>ValueTask.CompletedTask;
    }
    private sealed class Driver(GameDungeonSelectionCommandHandler handler):ICommandBus
    {
        public async ValueTask<T> SendAsync<T>(ICommand<T> value,CancellationToken token=default)where T:notnull
        {var command=(IProtocolCommand)value;var request=new CommandRequestContext.ProtocolNonEffect(command.Dispatch,command.Message,command.Frame,()=>true);var context=new CommandExecutionContext(Guid.NewGuid(),value.GetType(),handler.GetType(),CommandExecutionKind.Direct){Request=request};var result=value switch{GameEnterSelectDungeonCommand enter=>await handler.HandleAsync(context,enter,token),GameBackToVillageCommand back=>await handler.HandleAsync(context,back,token),_=>throw new NotSupportedException()};return(T)(object)result;}
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
