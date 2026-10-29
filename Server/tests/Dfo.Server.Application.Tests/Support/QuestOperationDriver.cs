using System.Reflection;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Quests;
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
// The acceptance domain service is assembled from the fixture's own dependencies; quest kinds the
// fixture does not supply are absent, so they refuse acceptance and leave progress unmerged.
internal sealed class QuestAcceptOperationDriver(params object[] dependencies)
{
    private readonly object[] all=[..dependencies,QuestOperationDriver.Create(typeof(QuestAcceptanceDomainService),null,[..dependencies,new AbsentQuestKinds()])];
    internal ValueTask<QuestAcceptOutcome> HandleAsync(CommandExecutionContext context,QuestAcceptOperation input,CancellationToken ct) => QuestOperationDriver.Invoke<QuestAcceptOutcome>("QuestAcceptance","AcceptQuest","QuestAcceptanceInput","AcceptAsync",all,[input.AccountId,input.CharacterId,input.QuestId],context,null,ct);
}
internal sealed class AbsentQuestKinds:IMirrorAradQuestDomainService,IMarriageQuestDomainService,ICycleDungeonEntryDomainService,ISouthernDaleQuestDomainService,IGiantDragonCrystalExchangeDomainService
{
    public ValueTask<QuestAcceptance> AcceptAsync(AccountId account,CharacterId character,ushort quest,CancellationToken ct)=>ValueTask.FromResult(QuestAcceptance.Refused(QuestRefusal.UnsupportedObjective));
    public ValueTask<QuestProgressSnapshot> MergeAsync(CharacterId character,QuestProgressSnapshot ordinary,CancellationToken ct)=>ValueTask.FromResult(ordinary);
    public ValueTask<bool> CanAcceptAsync(AccountId account,CharacterId character,int quest,CancellationToken ct)=>ValueTask.FromResult(false);
    public ValueTask<bool> CanAcceptAsync(CharacterId character,CancellationToken ct)=>ValueTask.FromResult(false);
    public ValueTask<QuestAdvance> TriggerAsync(AccountId account,CharacterId character,ushort quest,CancellationToken ct)=>throw new NotSupportedException();
    public ValueTask<MirrorQuestCompletionPreparation> PrepareCompletionAsync(AccountId account,CharacterId character,ushort quest,ushort selection,ushort count,DateTimeOffset at,SelectedSessionIdentity? session,Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint,CancellationToken ct)=>throw new NotSupportedException();
    public ValueTask CommitCompletionAsync(AccountId account,CharacterId character,MirrorQuestCompletionPreparation preparation,CancellationToken ct)=>throw new NotSupportedException();
    public ValueTask<bool> GiveUpAsync(AccountId account,CharacterId character,ushort quest,CancellationToken ct)=>throw new NotSupportedException();
    public ValueTask<bool> ReportAsync(AccountId account,CharacterId character,IReadOnlyList<QuestProgressFact> facts,CancellationToken ct)=>throw new NotSupportedException();
    public ValueTask<QuestProgressSnapshot> SnapshotAsync(AccountId account,CharacterId character,CancellationToken ct)=>throw new NotSupportedException();
    public ValueTask ClearRunAsync(CharacterId character,DungeonRunId run,CancellationToken ct)=>throw new NotSupportedException();
    public ValueTask<IReadOnlyCollection<int>> EligibleQuestIdsAsync(AccountId account,CharacterId character,CancellationToken ct)=>throw new NotSupportedException();
    public ValueTask BindAsync(AccountId account,CharacterId character,int quest,CancellationToken ct)=>throw new NotSupportedException();
    public ValueTask<bool> CanCompleteAsync(AccountId account,CharacterId character,int quest,CancellationToken ct)=>throw new NotSupportedException();
    public ValueTask<CycleDungeonPreparation> PrepareAsync(SelectedSessionIdentity session,int dungeonId,int questId,CancellationToken cancellationToken)=>throw new NotSupportedException();
    public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context,CancellationToken cancellationToken)=>throw new NotSupportedException();
    public ValueTask<bool> TryCompleteAsync(CharacterId character,CancellationToken ct)=>throw new NotSupportedException();
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
