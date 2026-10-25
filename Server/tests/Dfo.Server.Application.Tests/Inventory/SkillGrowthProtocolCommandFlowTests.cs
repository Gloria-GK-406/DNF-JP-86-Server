using System.Collections;
using System.Buffers.Binary;
using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Game;

using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Characters;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

// Actual decoder/dispatcher, command scopes, SQLite, registered encoders and production outbox.
// Only content definitions, the published view and physical transport are controlled.
public sealed class SkillGrowthProtocolCommandFlowTests(ITestOutputHelper output)
{
    private const int Book = 690001555, Ticket = 10006124;
    private static readonly string[] Fine = ["LearnSkillCommand", "IncreaseStatusCommand", "UseLevelUpTicketCommand", "ChangeSkillSlotCommand", "SwitchSkillBuildCommand", "GrantTutorialLevelCommand"];
    private static readonly string[] Whole = ["GameLearnSkillCommand", "GameIncreaseStatusCommand", "GameLevelUpTicketCommand", "GameChangeSkillSlotCommand", "GameSwitchSkillBuildCommand", "GameTutorialLevelUpCommand"];
    private static bool Modern => typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.LearnSkill.GameLearnSkillCommand") is not null;

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Committed_Main_same_frame_replay_uses_original_entry_and_real_encoder_failure_closes_only_captured_physical(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = await Create(protocol, trace, delivery);
        var physical = delivery.Add(f.Connection);
        await Send(f, protocol); await delivery.Settled();
        var entry = Entry(f); var ordinal = entry.Ordinal; var after = Assets(f);
        Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Domain.Attempts).Transaction.Completion);
        Assert.True(Split(physical.Sends).Any(frame => frame[0] == 1 && Type(frame) == WireType(protocol)),string.Join("\n",trace.Errors));
        AssertSuccessfulChange(f,protocol);
        await Send(f, protocol); await delivery.Settled();
        Assert.Equal(after, Assets(f)); Assert.Single(trace.Domain.Attempts);
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal);
        Assert.Contains(delivery.Recorder.Entries.Last().Messages, m => m.GetType().Name.Contains("InventorySnapshot"));
        var replacement = f.Connection with { }; var next = delivery.Add(replacement);
        GameplayCallChain replayChain=default; var beforeSends = physical.Sends.Count; var cause = new IOException("TASK50 real replay encoder"); var calls = 0;
        f.Sender.Outbound.Encoder.Fail((target, message) => {
            replayChain=GameplayJob.Current!.Value; Assert.Same(f.Connection, target); Assert.Contains("InventorySnapshot", message.GetType().Name); calls++;
            f.Sender.Outbound.Connections.Remove(target.ConnectionId); f.Sender.Outbound.Connections.Add(replacement); return cause;
        });
        Assert.Same(cause, await Record.ExceptionAsync(() => Send(f, protocol).AsTask()));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1, calls); Assert.Equal(1, physical.CloseCalls); Assert.Equal(beforeSends, physical.Sends.Count);
        Assert.Equal(0, next.CloseCalls); Assert.Empty(next.Events); Assert.Same(next, delivery.Resolve(replacement));
        Assert.Equal(after, Assets(f)); Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal);
        Assert.Equal(EffectOperationState.Committed, entry.State); Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        CloseChain(f, replayChain); ClosedMainRequest(f, trace, protocol);
        output.WriteLine($"route={(Modern ? "whole" : "fine")} protocol={protocol} entry={entry.State} ordinal={ordinal} causeSame=True physicalClose=1 replacementClose=0 assetsOnce=True");
    }

    [Theory] [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)] [InlineData(2, false)] [InlineData(2, true)]
    public async Task Actual_SQL_action_or_native_commit_failure_restores_assets_and_original_entry_retry(int protocol, bool native)
    {
        var trace = new Trace(); using var f = await Create(protocol, trace); var before = Assets(f);
        var mutation = protocol == 0 ? "UPDATE OF level ON character_skills" : "UPDATE OF exp ON characters";
        f.Sql(native
            ? $"CREATE TABLE task50_parent(id INTEGER PRIMARY KEY); CREATE TABLE task50_child(id INTEGER REFERENCES task50_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER task50_fail AFTER {mutation} BEGIN INSERT INTO task50_child VALUES(404); END"
            : $"CREATE TRIGGER task50_fail BEFORE {mutation} BEGIN SELECT RAISE(ABORT,'TASK50 action SQL'); END");
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        if (protocol == 0) Assert.IsType<SqliteException>(failure); else Assert.Null(failure);
        var attempt = Assert.Single(trace.Domain.Attempts);
        var cause = Assert.IsType<SqliteException>(native ? attempt.Transaction.CommitFailure!.Cause : attempt.Failure);
        Assert.Equal(native ? 787 : 1811, cause.SqliteExtendedErrorCode);
        Assert.Equal(1,attempt.ForeignKeys);Assert.Equal(native,attempt.Returned);
        if (native) SqliteFailureAssertions.FromCommit(attempt.Transaction, cause);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion); Assert.Equal(before, Assets(f));
        var entry = Entry(f); var ordinal = entry.Ordinal; Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State);
        if (protocol == 0) { Assert.Same(cause, failure); Assert.Empty(f.Sender.Messages); }
        else Assert.Equal(new byte[] { 0, protocol == 1 ? (byte)2 : (byte)99 }, Assert.Single(f.Sender.Outbound.Frames)[15..]);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        f.Sql("DROP TRIGGER task50_fail"); await Send(f, protocol);
        Assert.Same(entry, Entry(f)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(2, trace.Domain.Attempts.Count); AssertSuccessfulChange(f,protocol); var committed = Assets(f); await Send(f, protocol);
        Assert.Equal(committed, Assets(f)); Assert.Equal(2, trace.Domain.Attempts.Count);
        output.WriteLine($"protocol={protocol} native={native} cause={cause.SqliteExtendedErrorCode} confirmedRollback=True originalEntryRetry=True");
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Actual_DI_scope_failure_keeps_transaction_completion_frames_and_exact_source_cause(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace { ScopeFailure = new IOException("TASK50 actual command scope Dispose") };
        using var f = await Create(protocol, trace, delivery); var physical = delivery.Add(f.Connection);
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        if (!Modern && protocol is 1 or 2) Assert.Null(failure);
        else if (Modern) Assert.Same(trace.ScopeFailure, Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        else Assert.Same(trace.ScopeFailure, failure);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Domain.Attempts).Transaction.Completion);
        AssertSuccessfulChange(f,protocol);
        if(protocol==3) Assert.Equal(0,f.Scalar("SELECT slot FROM character_skills WHERE character_id=1 AND page_index=0"));
        if(protocol==4) Assert.Equal(1,f.Scalar("SELECT active_skill_build FROM characters WHERE character_id=1"));
        if(protocol==5) Assert.Equal(2,f.Scalar("SELECT level FROM characters WHERE character_id=1"));
        var blocked = f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection);
        Assert.Equal(Modern && protocol < 3, blocked);
        await delivery.Settled();
        if (Modern || protocol is 0 or 3 or 4 or 5) {
            await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, physical.CloseCalls); CloseChain(f, trace);
        } else { Assert.Equal(0, physical.CloseCalls); Assert.Equal(new byte[] { 0, protocol == 1 ? (byte)2 : (byte)99 }, Assert.Single(Split(physical.Sends))[15..]); }
        var wire=Split(physical.Sends).ToArray();
        if(!Modern && protocol is 0 or 3 or 4 or 5) Assert.Empty(wire);
        if (Modern) {
            Assert.Contains(wire,x=>x[0]==1 && Type(x)==WireType(protocol));
            if(protocol==0) Assert.Equal(new ushort[]{0x1D},wire.Select(Type));
            if(protocol==1) Assert.Equal(new ushort[]{0x1E,0x0E,0x25},wire.Select(Type));
            if(protocol==2) {Assert.Equal(new ushort[]{0x1A2,0x0E,0x25},wire.Take(3).Select(Type));Assert.True(Array.FindIndex(wire,x=>Type(x)==0x1A2)<Array.FindIndex(wire,x=>Type(x)==0x25));}
            if(protocol is 3 or 4) Assert.Single(wire);
            if(protocol==5) Assert.Equal(new ushort[]{0x25,0x15,0x1E4},wire.Select(Type));
            var declaration = Assert.Single(trace.Units); Assert.Equal(protocol < 3 ? CommandWorkUnitKind.MainSettlement : CommandWorkUnitKind.Ordinary, declaration.Kind);
            if(protocol<3) { var actor=Assert.Single(declaration.Participants); Assert.Equal(f.Session,actor.Session);Assert.Same(f.Connection,actor.Connection); }
            if (protocol >= 3) { Assert.Empty(declaration.Participants); Assert.Equal(CommandFootprintMode.None, declaration.Footprint); }
            ClosedMainRequest(f, trace, protocol);
        }
        output.WriteLine($"protocol={protocol} modern={Modern} completion=Committed failure={failure?.GetType().Name ?? "none"} blocked={blocked} physicalClose={physical.CloseCalls} frames={string.Join(',', wire.Select(Type))}");
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Initial_real_encoder_failure_keeps_committed_assets_and_observes_original_projection_boundary(int protocol)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var trace = new Trace(); using var f = await Create(protocol, trace, delivery); var physical = delivery.Add(f.Connection);
        var cause = new IOException("TASK50 initial encoder"); f.Sender.Outbound.Encoder.Fail((target, _) => { Assert.Same(f.Connection, target); return cause; });
        var failure = await Record.ExceptionAsync(() => Send(f, protocol).AsTask());
        if (protocol == 2) Assert.Null(failure);
        else if (Modern) Assert.Same(cause, Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        else Assert.Same(cause, failure);
        Assert.Equal(EffectOperationState.Committed, Entry(f).State);AssertSuccessfulChange(f,protocol);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Domain.Attempts).Transaction.Completion);
        Assert.Equal(Modern && protocol != 2, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        if (protocol != 2) { await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); CloseChain(f, trace); }
        await delivery.Settled(); Assert.Equal(protocol == 2 ? 0 : 1, physical.CloseCalls); Assert.Empty(physical.Sends);
        output.WriteLine($"protocol={protocol} modern={Modern} committed=True failure={failure?.GetType().Name ?? "none"} physicalClose={physical.CloseCalls}");
    }

    [Theory] [InlineData(1, false)] [InlineData(1, true)] [InlineData(2, false)] [InlineData(2, true)]
    public async Task Cancellation_and_typed_uncertainty_are_never_mapped_to_refusal_ACK(int protocol, bool uncertain)
    {
        var cause = uncertain ? (Exception)new TransactionOutcomeUncertainException(TransactionUncertaintyReason.PostCommitPublicationFailure, "TASK50 typed", new IOException("TASK50 typed")) : new OperationCanceledException("TASK50 cancellation");
        var trace = new Trace(); trace.Domain.DomainFailure = cause;
        using var f = await Create(protocol, trace); var before = Assets(f);
        Assert.Same(cause, await Record.ExceptionAsync(() => Send(f, protocol).AsTask()));
        Assert.Empty(f.Sender.Messages); Assert.Equal(before, Assets(f));
        Assert.Equal(uncertain ? DomainTransactionCompletion.Uncertain : DomainTransactionCompletion.ConfirmedRollback, Assert.Single(trace.Domain.Attempts).Transaction.Completion);
    }

    private static async Task<ShopFixture> Create(int protocol, Trace trace, RemainingOpeningProtocolCommandFlowTests.SenderProbe? delivery = null)
    {
        var f = new ShopFixture(s => {
            s.AddSingleton<ILoggerProvider>(new LogProbe(trace));
            s.AddSingleton<ISkillRuleDefinitionSource>(new LearningContent()); s.AddSingleton<ICharacterStatDefinitionSource>(new TestCharacterContent());
            s.AddSingleton<IItemPlacementDefinitionSource>(new OpeningContent());
            s.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((Book, ItemUseRules.Potion(0)), (Ticket, ItemUseRules.Potion(0))));
            s.AddSingleton<IExperienceItemDefinitionSource, BookDefinition>(); s.AddSingleton<IStackableActionDefinitionSource, TicketDefinition>();
            s.AddSingleton<ICharacterSkillPointDomainService, CharacterSkillPointDomainService>();
            s.AddSingleton<IQuestContentRuleService, EmptyQuests>(); s.AddSingleton<ISelectedCharacterCurrentViewDomainService, PublishedView>();
            if (delivery is not null) s.AddSingleton<IOutboundSender>(delivery.Recorder);
            s.AddSingleton(p => new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(),p.GetRequiredService<GameplayCloseCauses>(),p.GetRequiredService<ILogger<GameplayOutbox>>()));
            if (protocol is 0 or 3 or 4) RemainingOpeningProtocolCommandFlowTests.DomainObserver.Add<ICharacterSkillLearningDomainService>(s, trace.Domain);
            else if (protocol == 1) RemainingOpeningProtocolCommandFlowTests.DomainObserver.Add<IExperienceItemDomainService>(s, trace.Domain);
            else if (protocol == 2) RemainingOpeningProtocolCommandFlowTests.DomainObserver.Add<ILevelUpTicketDomainService>(s, trace.Domain);

            var name = Modern ? Whole[protocol] : Fine[protocol];
            var descriptor = s.Last(x => x.ServiceType.IsGenericType && x.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) && x.ServiceType.GenericTypeArguments[0].Name == name);
            var wrapper = typeof(CommandProbe<,>).MakeGenericType(descriptor.ServiceType.GenericTypeArguments);
            s.AddScoped(descriptor.ServiceType, p => Activator.CreateInstance(wrapper, ActivatorUtilities.CreateInstance(p, descriptor.ImplementationType!), trace, p, protocol)!);
            var units = s.Last(x => x.ServiceType == typeof(ICommandWorkUnits));
            s.AddSingleton<ICommandWorkUnits>(p => new UnitProbe((ICommandWorkUnits)(units.ImplementationInstance ?? units.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p, units.ImplementationType!)), trace, p, protocol));
        });
        f.SetLevel(protocol == 5 ? (byte)1 : (byte)20);
        f.Sql("UPDATE characters SET job=12,bonus_sp=5,bonus_tp=3,skill_tree_index=-1; INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level,extra_values) VALUES(1,'pve',0,5,1,2,X'0908'),(1,'pve',1,15,1,1,X'07'); INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0); INSERT INTO character_pvp(character_id) VALUES(1); INSERT INTO character_settings(character_id) VALUES(1); INSERT INTO character_expert_job(character_id) VALUES(1)");
        if (protocol == 4) f.Sql("UPDATE characters SET skill_tree_index=0 WHERE character_id=1");
        if (protocol == 1) await f.Seed(5, Book, 2);
        if (protocol == 2) await f.Seed(5, Ticket, 2);
        if (protocol == 5) f.InTransaction(() => f.Services.GetRequiredService<IDungeonRunDomainService>().Begin(f.Session.CharacterId,
            new DungeonLayout(60,0,1,1,new(0,0),new(0,0),1,0,new Dictionary<MazeCell,DungeonCellDefinition> { [new(0,0)] = DungeonCellDefinition.Fixed(1) },1,1), DungeonDifficulty.Normal));
        return f;
    }
    private static ushort WireType(int p) => p switch { 0=>0x1D,1=>0x1E,2=>0x1A2,3=>0x1C,4=>0x104,_=>0x1E4 };
    private static ValueTask Send(ShopFixture f, int protocol) => EffectRequestTransport.SendRaw(f, new(1,WireType(protocol),0,7), protocol switch {
        0 => Convert.FromHexString("00010100000200BFD63B2A"), 1=>[5,0],2=>[5,0,0],3=>Convert.FromHexString("000500FFFFFFFFD74132E1"),4=>[0],_=>[] });
    private static string Assets(ShopFixture f) => string.Join('|', f.Scalar("SELECT exp FROM characters WHERE character_id=1"), f.Scalar("SELECT bonus_sp FROM characters WHERE character_id=1"), f.Scalar("SELECT COALESCE(sum(count),0) FROM item_slots"), f.Scalar("SELECT COALESCE(sum(level),0) FROM character_skills"), f.Scalar("SELECT count(*) FROM character_invisible_flags"));
    private static ushort Type(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
    private static EffectOperationEntry Entry(ShopFixture f) {
        var table=typeof(EffectOperationLedger).GetField("ledgers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Services.GetRequiredService<EffectOperationLedger>())!;
        object?[] args=[f.Connection,null]; Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,args)!);
        return Assert.Single(((IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!).Values.Cast<EffectOperationEntry>());
    }
    private static void CloseChain(ShopFixture f, Trace trace) => CloseChain(f,trace.Chain);
    private static void CloseChain(ShopFixture f, GameplayCallChain expected) {
        Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var close));
        Assert.Equal(expected.RootId,close.RootId); Assert.Equal(GameplayCallSource.Connection,close.Source); Assert.Equal(f.Connection.ConnectionId,close.SourceConnection);
    }
    private static void ClosedMainRequest(ShopFixture f, Trace trace, int protocol) {
        if (!Modern) { Assert.Null(trace.Context!.Request); return; }
        var request=Assert.IsAssignableFrom<CommandRequestContext>(trace.Context!.Request); Assert.True(request.IsClosed);
        if(protocol<3) { var selected=Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(request); Assert.Same(f.Connection,selected.Dispatch.Connection); Assert.Equal(f.Session,selected.Recipient.Session); }
        else { var plain=Assert.IsType<CommandRequestContext.ProtocolNonEffect>(request);Assert.Same(f.Connection,plain.Dispatch.Connection);var metadata=Assert.IsType<ProtocolFrameMetadata.Game>(plain.Metadata).Value;Assert.Equal(WireType(protocol),metadata.Type);Assert.Equal(1,metadata.InboundOrdinal); }
    }
    public sealed class Trace {
        internal RemainingOpeningProtocolCommandFlowTests.Trace Domain = new(); internal Exception? ScopeFailure; internal GameplayCallChain Chain; internal CommandExecutionContext? Context; internal List<CommandWorkUnitDeclaration> Units=[]; internal List<string> Errors=[];
    }
    public sealed class CommandProbe<TCommand,TResult>(object actual,Trace trace,IServiceProvider p,int protocol):ICommandHandler<TCommand,TResult>,IDisposable where TCommand:class,ICommand<TResult> where TResult:notnull {
        public ValueTask<TResult> HandleAsync(CommandExecutionContext c,TCommand command,CancellationToken ct) { trace.Context=c;trace.Chain=GameplayJob.Current!.Value; if(protocol==5 && !Modern) trace.Domain.Attempts.Add(new((DomainTransaction)p.GetRequiredService<IDomainTransactionAccessor>().Current!)); return ((ICommandHandler<TCommand,TResult>)actual).HandleAsync(c,command,ct); }
        public void Dispose() { if(trace.ScopeFailure is {} cause) throw cause; }
    }
    private sealed class UnitProbe(ICommandWorkUnits actual,Trace trace,IServiceProvider p,int protocol):ICommandWorkUnits {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken t=default) where T:notnull {trace.Units.Add(d);return actual.WriteAsync(c,d,(u,ct)=> { if(protocol==5) trace.Domain.Attempts.Add(new((DomainTransaction)p.GetRequiredService<IDomainTransactionAccessor>().Current!)); return a(u,ct); },t);}
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken t=default) where T:notnull {trace.Units.Add(d);return actual.WriteResultAsync(c,d,a,t);}
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken t=default) where T:notnull => actual.ReadOnlyAsync(c,d,a,t);
    }
    private sealed class BookDefinition:IExperienceItemDefinitionSource { public ValueTask<ExperienceItemDefinition?> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<ExperienceItemDefinition?>(id==Book?new(ExperienceItemGrantKind.Fixed,1000,20,84,true):null); }
    private sealed class TicketDefinition:IStackableActionDefinitionSource { public ValueTask<StackableActionDefinition?> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<StackableActionDefinition?>(new("[level up ticket]",1,85,0,true){LevelIncrease=1}); }
    private sealed class EmptyQuests:IQuestContentRuleService {
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([]);
        public ValueTask<QuestDefinition?> FindAsync(int id,CancellationToken ct)=>ValueTask.FromResult<QuestDefinition?>(null);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
        public QuestParameters Parameters()=>new([],new Dictionary<char,int>(),100,100,100,100);
    }
    private sealed class PublishedView(ICharacterDomainService characters,ICharacterEntryStateDomainService entries,ICharacterInventoryEntryDomainService inventory,ICharacterSkillPointDomainService points):ISelectedCharacterCurrentViewDomainService {
        public async ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity s,CancellationToken ct) {
            var entry=await entries.LoadAsync(s.AccountId,s.CharacterId,ct);
            entry=entry with {Skills=entry.Skills with {ProtocolMirror=await points.RemainingAsync(s.CharacterId,ct)}};
            return new(await characters.LoadForSelectionAsync(s.AccountId,0,ct),entry,
                await inventory.ReadCurrentAsync(s.AccountId,s.CharacterId,ct),null!,null!,new GameWorldEntryState(DateTimeOffset.UnixEpoch,StubWorldContentEntryRuleService.EmptyContent,new ChannelEntryPolicyState(11,null!,true,false)),null!);
        }
    }
    private static void AssertSuccessfulChange(ShopFixture f,int protocol) {
        if(protocol==0) Assert.Equal(4,f.Scalar("SELECT level FROM character_skills WHERE character_id=1 AND page_index=0 AND skill_id=1"));
        if(protocol==1) {Assert.Equal(1,f.Count(Book));Assert.Equal((int)f.ExperienceForLevel(20)+1000,f.Scalar("SELECT exp FROM characters WHERE character_id=1"));}
        if(protocol==2) {Assert.Equal(1,f.Count(Ticket));Assert.Equal(21,f.Scalar("SELECT level FROM characters WHERE character_id=1"));}
    }
    private sealed class LogProbe(Trace trace):ILoggerProvider,ILogger { public ILogger CreateLogger(string category)=>this;public void Dispose(){}public IDisposable? BeginScope<T>(T state) where T:notnull=>null;public bool IsEnabled(LogLevel level)=>true;public void Log<T>(LogLevel level,EventId id,T state,Exception? e,Func<T,Exception?,string> formatter) {if(e is not null) trace.Errors.Add(formatter(state,e)+e);}}
    private static IEnumerable<byte[]> Split(IEnumerable<byte[]> sends) { foreach(var bytes in sends) for(var offset=0;offset<bytes.Length;) { var length=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset+3));yield return bytes[offset..(offset+length)];offset+=length; } }
}
