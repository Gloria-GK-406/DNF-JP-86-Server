using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Abstractions.Quests;

using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CycleDungeonEntry;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Quests;

public sealed partial class CycleQuestFlowTests
{
    private static bool LegacyPreparation => typeof(PrepareCycleDungeonEntryHandler).Assembly.GetType("Dfo.Server.Protocol.Game.CycleDungeonEntry.PrepareCycleDungeonEntryCommand") is not null;
    [Theory] [InlineData(8,84,2001,true)] [InlineData(7,84,2001,false)]
    [InlineData(8,0,2001,false)] [InlineData(8,84,65536,false)] [InlineData(8,85,2001,false)]
    public async Task Task42_real_decoder_dispatch_and_encoder_preserve_preparation_without_accepting_the_quest(int length,int dungeon,int quest,bool accepted)
    {
        using var f=new ShopFixture(s=>{s.AddSingleton<IQuestContentRuleService>(new QuestRules());s.AddSingleton<ICycleDungeonRuleService>(new CycleRules());});
        TestGameplay.Run(f.Services,()=>f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Record(f.Session,DungeonEntryIntent.Interactive));
        await PrepareWire(f,length,dungeon,quest);
        var response=Assert.Single(f.Sender.Messages.OfType<PrepareCycleDungeonEntryResponse>());
        Assert.Equal(accepted,response.Accepted);Assert.Equal(accepted?quest:0,response.QuestId);
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM character_active_quests"));
        TestGameplay.Run(f.Services,()=>Assert.Equal(accepted?DungeonEntryIntent.Cycle(dungeon,quest):DungeonEntryIntent.Interactive,f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Find(f.Session)));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Task42_content_rejection_and_real_command_scope_disposal_have_distinct_boundaries(bool disposal)
    {
        var cause=new InvalidDataException(disposal?"task42 scope disposal":"task42 cycle content");
        using var f=new ShopFixture(s=>
        {
            if(!disposal)s.AddSingleton<ICycleDungeonRuleService>(TownMovementProtocolCommandFlowTests.Proxy<ICycleDungeonRuleService>.Create((_,_)=>throw cause));
            else s.AddSingleton<ICycleDungeonRuleService>(new CycleRules());
            s.AddSingleton<IQuestContentRuleService>(new QuestRules());
            if(disposal)
            {
                var d=s.Single(x=>x.ServiceType.IsGenericType&&x.ServiceType.GetGenericTypeDefinition()==typeof(ICommandHandler<,>)&&x.ServiceType.GenericTypeArguments[0].Name==(LegacyPreparation?"PrepareCycleDungeonEntryCommand":"GamePrepareCycleDungeonEntryCommand"));
                s.Remove(d);s.AddScoped(d.ServiceType,p=>Activator.CreateInstance(typeof(PreparationScopeFault<,>).MakeGenericType(d.ServiceType.GenericTypeArguments),ActivatorUtilities.CreateInstance(p,d.ImplementationType!),cause)!);
            }
        });
        TestGameplay.Run(f.Services,()=>f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Record(f.Session,DungeonEntryIntent.Interactive));
        var failure=await Record.ExceptionAsync(()=>PrepareWire(f,8,84,2001).AsTask());
        if(disposal&&!LegacyPreparation)Assert.Same(cause,failure);else Assert.Null(failure);
        var response=Assert.Single(f.Sender.Messages.OfType<PrepareCycleDungeonEntryResponse>());
        Assert.Equal(disposal&&!LegacyPreparation,response.Accepted);
        TestGameplay.Run(f.Services,()=>Assert.Equal(disposal?DungeonEntryIntent.Cycle(84,2001):DungeonEntryIntent.Interactive,f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Find(f.Session)));
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM character_active_quests"));
    }
    [Fact]
    public async Task Task42_scope_failure_keeps_the_exact_physical_source_and_cause_without_selected_fencing()
    {
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var cause=new InvalidDataException("task42 physical cycle scope");var trace=new PreparationTrace();
        using var f=new ShopFixture(s=>
        {
            s.AddSingleton<IOutboundSender>(delivery.Recorder);
            s.AddSingleton(p=>new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(),p.GetRequiredService<GameplayCloseCauses>(),p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GameplayOutbox>>()));
            s.AddSingleton<IQuestContentRuleService>(new QuestRules());s.AddSingleton<ICycleDungeonRuleService>(new CycleRules());
            var d=s.Single(x=>x.ServiceType.IsGenericType&&x.ServiceType.GetGenericTypeDefinition()==typeof(ICommandHandler<,>)&&x.ServiceType.GenericTypeArguments[0].Name==(LegacyPreparation?"PrepareCycleDungeonEntryCommand":"GamePrepareCycleDungeonEntryCommand"));
            s.Remove(d);s.AddScoped(d.ServiceType,p=>Activator.CreateInstance(typeof(PhysicalPreparationScopeFault<,>).MakeGenericType(d.ServiceType.GenericTypeArguments),ActivatorUtilities.CreateInstance(p,d.ImplementationType!),cause,trace)!);
        });
        var physical=delivery.Add(f.Connection);var replacement=f.Connection with{};var next=delivery.Add(replacement);
        trace.Before=()=>{f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);};
        TestGameplay.Run(f.Services,()=>f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Record(f.Session,DungeonEntryIntent.Interactive));
        var failure=await Record.ExceptionAsync(()=>PrepareWire(f,8,84,2001).AsTask());
        if(LegacyPreparation)Assert.Null(failure);else Assert.Same(cause,failure);
        if(!LegacyPreparation)
        {
            await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();Assert.Equal(1,physical.CloseCalls);
            Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));Assert.Equal(trace.Chain.Id,chain.Id);Assert.Equal(trace.Chain.RootId,chain.RootId);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);
        }
        else {await delivery.Settled();Assert.Equal(0,physical.CloseCalls);}
        Assert.Equal(0,next.CloseCalls);if(LegacyPreparation){Assert.Single(next.Sends);Assert.Empty(physical.Sends);}else{Assert.Empty(next.Events);Assert.Single(physical.Sends);}
        var encoded = Assert.Single(LegacyPreparation ? next.Sends : physical.Sends);
        Assert.Equal((ushort)0x308, BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(1)));
        Assert.Equal(LegacyPreparation ? new byte[] { 0 } : Convert.FromHexString("0101000000D1070000"), encoded[15..]);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
    }
    private sealed class PreparationTrace {internal Action? Before;internal GameplayCallChain Chain;}
    private sealed class PhysicalPreparationScopeFault<TCommand,TResult>(object actual,Exception cause,PreparationTrace trace):ICommandHandler<TCommand,TResult>,IDisposable where TCommand:class,ICommand<TResult> where TResult:notnull
    {public ValueTask<TResult> HandleAsync(CommandExecutionContext context,TCommand command,CancellationToken ct){trace.Chain=GameplayJob.Current!.Value;return ((ICommandHandler<TCommand,TResult>)actual).HandleAsync(context,command,ct);}public void Dispose(){trace.Before?.Invoke();throw cause;}}
    private sealed class PreparationScopeFault<TCommand,TResult>(object actual,Exception cause):ICommandHandler<TCommand,TResult>,IDisposable where TCommand:class,ICommand<TResult> where TResult:notnull
    {public ValueTask<TResult> HandleAsync(CommandExecutionContext context,TCommand command,CancellationToken ct)=>((ICommandHandler<TCommand,TResult>)actual).HandleAsync(context,command,ct);public void Dispose()=>throw cause;}
    private static ValueTask<CycleDungeonPreparation> PrepareWhole(ShopFixture f,int dungeon,int quest)=>f.Services.GetRequiredService<ICommandBus>().SendAsync(new GamePrepareCycleDungeonEntryCommand(new(f.Connection),new(GameMessageMetadata.Empty,dungeon,quest)));
    private static ValueTask PrepareWire(ShopFixture f,int length,int dungeon,int quest)
    {
        var body=new byte[8];BinaryPrimitives.WriteInt32LittleEndian(body,dungeon);BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4),quest);body=body[..length];
        var metadata=new GameMessageMetadata(1,0x308,0,7){InboundOrdinal=7,SequenceIsFresh=true}.WithRawBody(body);
        var registration=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==0x308);
        var message=((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata,body)!;
        return f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),message,default);
    }
}
