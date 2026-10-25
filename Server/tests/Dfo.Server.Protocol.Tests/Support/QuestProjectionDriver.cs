using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Protocol.Tests.Support;
internal sealed record QuestFinishProjectionInput(AccountId AccountId,CharacterId CharacterId,ushort QuestId,ushort RewardSelection,ushort CompletionCount,uint AcquiredAtUnixTime,SelectedSessionIdentity? Session):ICommand<QuestFinishOutcome>;
// Fixed-receipt projection fixtures only: no real transaction, scope or admission evidence.
internal static class QuestProjectionDriver
{
    internal static async ValueTask Finish(object?[] oldArgs,object?[] newArgs,NetworkDispatchContext dispatch,FinishQuestRequest input,SelectedSessionIdentity session)
    {
        var modern=typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.QuestCompletion.GameFinishQuestCommandHandler");
        var owner=modern??typeof(FinishQuestHandler);var args=(modern is null?oldArgs:newArgs).Select(x=>ReferenceEquals(x,SkillGrowthDirectDriver.Logger)?typeof(NullLogger<>).MakeGenericType(owner).GetField("Instance")!.GetValue(null):x).ToArray();
        var handler=Activator.CreateInstance(owner,args)!;
        if(modern is null){await ((IClientMessageHandler)handler).HandleAsync(dispatch,input,default);return;}
        var commandType=typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.QuestCompletion.GameFinishQuestCommand",true)!;
        var request=new CommandRequestContext.ProtocolEffectSelected(dispatch,input,new ProtocolFrameMetadata.Game(input.Metadata),()=>true,
            new ExplicitEffectRequest(new(session,1,1,0x22,0,"projection fixture",0),new(session,dispatch.Connection,input.Metadata),input.MinimumReplayNeeds,null!));
        var context=new CommandExecutionContext(Guid.NewGuid(),commandType,owner,CommandExecutionKind.Direct){Request=request};
        try{await (ValueTask<QuestFinishOutcome>)owner.GetMethod("HandleAsync")!.Invoke(handler,[context,Activator.CreateInstance(commandType,dispatch,input),CancellationToken.None])!;}
        finally{await request.DrainOperationsAsync();request.Close();}
    }
    internal static async ValueTask GiveUp(object?[] oldArgs,object?[] newArgs,NetworkDispatchContext dispatch,Dfo.Server.Protocol.Game.QuestGiveUp.GiveUpQuestRequest input)
    {
        var a=typeof(ProtocolModule).Assembly;var modern=a.GetType("Dfo.Server.Protocol.Game.QuestGiveUp.GameGiveUpQuestCommandHandler");var owner=modern??typeof(Dfo.Server.Protocol.Game.QuestGiveUp.GiveUpQuestHandler);
        var args=(modern is null?oldArgs:newArgs).Select(x=>ReferenceEquals(x,SkillGrowthDirectDriver.Logger)?typeof(NullLogger<>).MakeGenericType(owner).GetField("Instance")!.GetValue(null):x).ToArray();var handler=Activator.CreateInstance(owner,args)!;
        if(modern is null){await ((IClientMessageHandler)handler).HandleAsync(dispatch,input,default);return;}
        var cmd=a.GetType("Dfo.Server.Protocol.Game.QuestGiveUp.GameGiveUpQuestCommand",true)!;var request=new CommandRequestContext.ProtocolNonEffect(dispatch,input,new ProtocolFrameMetadata.Game(input.Metadata),()=>true);
        try{await (ValueTask<Dfo.Server.Protocol.Game.QuestGiveUp.GiveUpQuestOutcome>)owner.GetMethod("HandleAsync")!.Invoke(handler,[new CommandExecutionContext(Guid.NewGuid(),cmd,owner,CommandExecutionKind.Direct){Request=request},Activator.CreateInstance(cmd,dispatch,input),CancellationToken.None])!;}
        finally{await request.DrainOperationsAsync();request.Close();}
    }
    internal sealed class ValueUnits<TResult>(TResult result):ICommandWorkUnits where TResult:notnull
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default) where T:notnull=>ValueTask.FromResult((T)(object)result);
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default) where T:notnull=>throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default) where T:notnull=>throw new NotSupportedException();
    }
    internal sealed class Units(Func<ValueTask<QuestFinishOutcome>> result):ICommandWorkUnits
    {
        public async ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> action,CancellationToken ct=default) where T:notnull
        {try{return (T)(object)await result();}catch(QuestFinishRejectedException e)when(!TransactionOutcomeUncertainException.Contains(e)){return (T)(object)QuestFinishOutcome.Refuse(101);}}
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default) where T:notnull=>throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default) where T:notnull=>throw new NotSupportedException();
    }
    internal sealed class Bus(ICommandBus actual):ICommandBus
    {
        public ValueTask<T> SendAsync<T>(ICommand<T> command,CancellationToken ct=default) where T:notnull
        {
            var type=command.GetType();object Get(string name)=>type.GetProperty(name)!.GetValue(command)!;
            return (ValueTask<T>)(object)actual.SendAsync(new QuestFinishProjectionInput((AccountId)Get("AccountId"),(CharacterId)Get("CharacterId"),(ushort)Get("QuestId"),(ushort)Get("RewardSelection"),(ushort)Get("CompletionCount"),(uint)Get("AcquiredAtUnixTime"),(SelectedSessionIdentity?)Get("Session")),ct);
        }
    }
}
