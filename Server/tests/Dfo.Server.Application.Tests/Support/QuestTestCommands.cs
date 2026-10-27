using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.QuestAcceptance;
using Dfo.Server.Protocol.Game.QuestGiveUp;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Support;

internal sealed record QuestAcceptTestInput(AccountId AccountId,CharacterId CharacterId,ushort QuestId);
internal sealed record QuestGiveUpTestInput(AccountId AccountId,CharacterId CharacterId,ushort QuestId);
internal sealed record QuestFinishTestInput(AccountId AccountId,CharacterId CharacterId,ushort QuestId,ushort RewardSelection,ushort CompletionCount,uint AcquiredAtUnixTime,SelectedSessionIdentity? Session=null);

// Shop integration fixtures retain actual registered decoder -> whole bus -> SQLite and production encoding.
// Non-network LearningFixture cases explicitly exercise the owned business operation in a real domain transaction;
// they are algorithm tests, not evidence about admission, protocol scopes, work-unit protection or delivery.
internal sealed class QuestTestCommands
{
    private readonly IServiceProvider services;private readonly ShopFixture? shop;
    internal QuestTestCommands(ShopFixture fixture){shop=fixture;services=fixture.Services;}
    internal QuestTestCommands(IServiceProvider provider){services=provider;}
    private static readonly ConditionalWeakTable<NetworkConnectionContext,Counter> Counters=new();
    private sealed class Counter {internal GameInboundFrameCounter Value=new();internal ushort Sequence;}
    internal static bool Modern=>typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.QuestAcceptance.GameAcceptQuestCommand") is not null;
    internal ValueTask<T> SendAsync<T>(ICommand<T> input,CancellationToken ct=default) where T:notnull => Dfo.Server.Testing.DomainCommandFixture.Bus(services).SendAsync(input,ct);
    internal ValueTask<QuestAcceptOutcome> SendAsync(QuestAcceptTestInput input,CancellationToken ct=default)=>Run<QuestAcceptOutcome>("QuestAcceptance","AcceptQuest","AcceptAsync",0x1F,input.AccountId,input.CharacterId,[input.AccountId,input.CharacterId,input.QuestId],ct);
    internal ValueTask<GiveUpQuestOutcome> SendAsync(QuestGiveUpTestInput input,CancellationToken ct=default)=>Run<GiveUpQuestOutcome>("QuestGiveUp","GiveUpQuest","GiveUpAsync",0x20,input.AccountId,input.CharacterId,[input.AccountId,input.CharacterId,input.QuestId],ct);
    internal ValueTask<QuestFinishOutcome> SendAsync(QuestFinishTestInput input,CancellationToken ct=default)=>Run<QuestFinishOutcome>("QuestCompletion","FinishQuest","CompleteAsync",0x22,input.AccountId,input.CharacterId,[input.AccountId,input.CharacterId,input.QuestId,input.RewardSelection,input.CompletionCount,input.AcquiredAtUnixTime,input.Session],ct);
    private async ValueTask<T> Run<T>(string slice,string name,string method,ushort wire,AccountId account,CharacterId character,object?[] args,CancellationToken ct) where T:notnull
    {
        var assembly=typeof(ProtocolModule).Assembly;
        if(assembly.GetType($"Dfo.Server.Protocol.Game.{slice}.{name}Command") is {} legacy)
            return await services.GetRequiredService<ICommandBus>().SendAsync((ICommand<T>)Activator.CreateInstance(legacy,args)!,ct);
        var owner=assembly.GetType($"Dfo.Server.Protocol.Game.{slice}.Game{name}CommandHandler",true)!;
        if(shop is not null && shop.Session.AccountId==account && shop.Session.CharacterId==character)
        {
            shop.Sql("INSERT OR IGNORE INTO character_condition(character_id,pc_room_state) VALUES(1,0); INSERT OR IGNORE INTO character_pvp(character_id) VALUES(1); INSERT OR IGNORE INTO character_settings(character_id) VALUES(1); INSERT OR IGNORE INTO character_expert_job(character_id) VALUES(1)");
            var bytes=new byte[wire==0x22?10:4];BinaryPrimitives.WriteUInt16LittleEndian(bytes,wire);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2),(ushort)args[2]!);
            if(wire==0x22){BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4),(ushort)args[3]!);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6),(ushort)args[4]!);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8),0xFFFF);}
            var counter=Counters.GetValue(shop.Connection,_=>new());var sequence=++counter.Sequence;var (ordinal,fresh)=counter.Value.Observe(sequence);
            var metadata=(new GameMessageMetadata(1,wire,0,sequence){InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody(bytes);
            var row=services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==wire);
            var message=((IGameMessageDecoder)services.GetRequiredService(row.DecoderType)).Decode(metadata,bytes)!;
            var command=Activator.CreateInstance(assembly.GetType($"Dfo.Server.Protocol.Game.{slice}.Game{name}Command",true)!,new NetworkDispatchContext(shop.Connection),message)!;
            return await services.GetRequiredService<ICommandBus>().SendAsync((ICommand<T>)command,ct);
        }
        return await TestGameplay.RunAsync(services,async()=>{
            await using var transaction=services.GetRequiredService<DomainTransactionScope>().Begin();
            var ctor=owner.GetConstructors().Single();var dependencies=ctor.GetParameters().Select(x=>x.ParameterType.IsGenericType&&x.ParameterType.GetGenericTypeDefinition()==typeof(Microsoft.Extensions.Logging.ILogger<>)?typeof(NullLogger<>).MakeGenericType(owner).GetField("Instance")!.GetValue(null):services.GetService(x.ParameterType)??(x.HasDefaultValue?x.DefaultValue:null)).ToArray();
            var handler=ctor.Invoke(dependencies);var inputType=owner.Assembly.GetType($"Dfo.Server.Protocol.Game.{slice}."+(wire==0x1F?"QuestAcceptanceInput":wire==0x20?"QuestGiveUpInput":"QuestCompletionInput"),true)!;
            var input=Activator.CreateInstance(inputType,args)!;using var footprint=new TestEffectFootprints.Lease(new EffectFootprintOwner(account,character,args.Length>6?(SelectedSessionIdentity?)args[6]:null));
            T result;
            try{result=await (ValueTask<T>)owner.GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(handler,wire==0x22?[input,footprint,ct]:[input,ct])!;}
            catch(TargetInvocationException e) when(e.InnerException is {} cause){ExceptionDispatchInfo.Capture(cause).Throw();throw;}
            await transaction.CommitAsync(ct);return result;
        });
    }
    internal static void RegisterLegacy(IServiceCollection services,string slice,string name)
    {
        var assembly=typeof(ProtocolModule).Assembly;var command=assembly.GetType($"Dfo.Server.Protocol.Game.{slice}.{name}Command");if(command is null)return;
        var handler=assembly.GetType($"Dfo.Server.Protocol.Game.{slice}.{name}CommandHandler",true)!;
        var result=command.GetInterfaces().Single(x=>x.IsGenericType&&x.GetGenericTypeDefinition()==typeof(ICommand<>)).GenericTypeArguments[0];
        typeof(CommandDispatchingExtensions).GetMethods().Single(x=>x.Name=="AddCommandHandler"&&x.GetGenericArguments().Length==3).MakeGenericMethod(command,result,handler).Invoke(null,[services,FixtureUnitKind.Transactional]);
    }
    internal sealed class PublishedView(ICharacterDomainService characters,ICharacterEntryStateDomainService entries,ICharacterInventoryEntryDomainService inventory,ICharacterSkillPointDomainService points):ISelectedCharacterCurrentViewDomainService
    {
        public async ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity s,CancellationToken ct){
            var entry=await entries.LoadAsync(s.AccountId,s.CharacterId,ct);entry=entry with {Skills=entry.Skills with {ProtocolMirror=await points.RemainingAsync(s.CharacterId,ct)}};
            return new(await characters.LoadForSelectionAsync(s.AccountId,0,ct),entry,await inventory.ReadCurrentAsync(s.AccountId,s.CharacterId,ct),new(s.AccountId,new(s.AccountId,new(InventoryContainerType.AccountCargo,0,[]),[],0,0,0),[]),null!,new GameWorldEntryState(DateTimeOffset.UnixEpoch,StubWorldContentEntryRuleService.EmptyContent,new ChannelEntryPolicyState(11,null!,true,false)),null!);
        }
    }
}
