using System.Reflection;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.QuestAcceptance;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Application.Tests.Support;

// Isolated business-operation / receipt-projection fixtures only. Their caller owns the explicit
// transaction and footprint; the production decoder/scope/work-unit evidence lives in wire tests.
internal sealed record QuestAcceptOperation(AccountId AccountId,CharacterId CharacterId,ushort QuestId);
internal sealed record QuestFinishOperation(AccountId AccountId,CharacterId CharacterId,ushort QuestId,ushort RewardSelection,ushort CompletionCount,uint AcquiredAtUnixTime,SelectedSessionIdentity? Session=null);
internal sealed class QuestAcceptOperationDriver(params object[] dependencies)
{
    internal ValueTask<QuestAcceptOutcome> HandleAsync(CommandExecutionContext context,QuestAcceptOperation input,CancellationToken ct) => QuestOperationDriver.Invoke<QuestAcceptOutcome>("QuestAcceptance","AcceptQuest","QuestAcceptanceInput","AcceptAsync",dependencies,[input.AccountId,input.CharacterId,input.QuestId],context,null,ct);
}
internal static class QuestOperationDriver
{
    internal static object Create(Type owner,IServiceProvider? services,params object[] dependencies)=>owner.GetConstructors().Single().Invoke(owner.GetConstructors().Single().GetParameters().Select(p=>dependencies.FirstOrDefault(p.ParameterType.IsInstanceOfType)??services?.GetService(p.ParameterType)??(p.ParameterType.IsGenericType&&p.ParameterType.GetGenericTypeDefinition()==typeof(Microsoft.Extensions.Logging.ILogger<>)?typeof(NullLogger<>).MakeGenericType(owner).GetField("Instance")!.GetValue(null):p.HasDefaultValue?p.DefaultValue:null)).ToArray());
    internal static async ValueTask<T> Invoke<T>(string slice,string name,string inputName,string method,object[] dependencies,object?[] fields,CommandExecutionContext context,IEffectFootprintRecorder? footprint,CancellationToken ct) where T:notnull
    {
        var a=typeof(ProtocolModule).Assembly;var old=a.GetType($"Dfo.Server.Protocol.Game.{slice}.{name}CommandHandler");var owner=old??a.GetType($"Dfo.Server.Protocol.Game.{slice}.Game{name}CommandHandler",true)!;
        var instance=Create(owner,null,dependencies);var input=Activator.CreateInstance(a.GetType($"Dfo.Server.Protocol.Game.{slice}."+(old is null?inputName:name+"Command"),true)!,fields)!;
        try{return await (ValueTask<T>)owner.GetMethod(old is null?method:"HandleAsync",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(instance,old is not null?[context,input,ct]:footprint is not null?[input,footprint,ct]:[input,ct])!;}
        catch(TargetInvocationException e) when(e.InnerException is {} cause){ExceptionDispatchInfo.Capture(cause).Throw();throw;}
    }
}
