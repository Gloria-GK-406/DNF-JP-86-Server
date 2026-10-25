using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonExitLifecycleTests
{
    internal static bool Whole => typeof(EplpCommandHandler).Assembly.GetType("Dfo.Server.Protocol.Game.DungeonClear.GameEplpCommand") is not null;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task49_ordinary_Eplp_real_scope_has_no_transaction_and_ends_before_scope_failure(bool fail)
    {
        await using var probe = new EntryFailureProbe(false, fail, ordinary: true,
            commandOverride: Whole ? "GameEplpCommand" : "LeaveDungeonCommand");
        using var f = new ShopFixture(s => { probe.Configure(s); s.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>(); });
        var run = CharacterDeathProtocolLifecycleTests.Enter(f);
        var physical = probe.Sender.Add(f.Connection);
        var endedAtScope = false;
        probe.BeforeDispose = () => endedAtScope = f.Services.GetRequiredService<IDungeonRunDomainService>().Find(f.Session.CharacterId) is null;
        var error = await Record.ExceptionAsync(() => probe.Wire(f, 0x48, [1, 2]).AsTask());
        if (fail) Assert.Same(probe.Cause, error); else Assert.Null(error);
        if (fail) Assert.True(endedAtScope);
        Assert.Empty(probe.Transactions); Assert.Empty(probe.Declarations);
        TestGameplay.Run(f.Services, () => { Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(f.Session.CharacterId)); Assert.Equal(fail && !Whole ? 1 : 0, f.Services.GetRequiredService<DungeonRunDeliveryRegistry>().Find(run).Count); });
        await probe.Sender.Settled();
        if (fail) await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(Dfo.Server.Abstractions.Execution.GameplayCallSource.Connection, probe.Chain.Source);
        Assert.Equal(f.Connection.ConnectionId, probe.Chain.SourceConnection);
        Assert.NotEqual(Guid.Empty, probe.Chain.RootId);
        if (fail)
        {
            Assert.True(f.Services.GetRequiredService<Dfo.Server.Application.Execution.GameplayCloseCauses>().TryTake(f.Connection, out var closedFrom));
            Assert.Equal(probe.Chain.Id, closedFrom.Id); Assert.Equal(probe.Chain.RootId, closedFrom.RootId);
            Assert.Equal(f.Connection.ConnectionId, closedFrom.SourceConnection);
        }
        if (Whole)
        {
            var request = Assert.IsType<CommandRequestContext.ProtocolNonEffect>(Assert.Single(probe.Executions).Request);
            Assert.Same(f.Connection, request.Dispatch.Connection);
            var metadata = Assert.IsType<ProtocolFrameMetadata.Game>(request.Metadata).Value;
            Assert.Equal((ushort)0x48, metadata.Type); Assert.Equal(7, metadata.InboundOrdinal);
            Assert.True(request.IsClosed);
        }
        // The legacy fine context has no protocol request; its actual job/close chain is checked above.
        if (!fail || Whole)
        {
            var types = physical.Sends.SelectMany(CharacterDeathOracle.Types).ToArray();
            Assert.Equal((ushort)0x48, types[0]); Assert.Contains((ushort)0x03, types);
        }
        else Assert.Empty(physical.Sends);
        Assert.Equal(fail ? 1 : 0, physical.CloseCalls);
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
        if (Whole) Assert.True(Assert.Single(probe.Executions).Request!.IsClosed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task49_ordinary_GiveUp_has_no_unit_and_encoder_failure_keeps_ended_run(bool fail)
    {
        await using var probe = new EntryFailureProbe(false, false, ordinary: true,
            commandOverride: Whole ? "GameGiveUpGameCommand" : "LeaveDungeonCommand");
        using var f = new ShopFixture(s => { probe.Configure(s); s.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>(); });
        var run = CharacterDeathProtocolLifecycleTests.Enter(f);
        var physical = probe.Sender.Add(f.Connection);
        if (fail) f.Sender.Outbound.Encoder.Fail((_, message) => message is GiveUpGameResponse ? probe.Cause : null);
        var error = await Record.ExceptionAsync(() => probe.Wire(f, 0x2A, []).AsTask());
        if (fail) Assert.Same(probe.Cause, error); else Assert.Null(error);
        Assert.Empty(probe.Transactions); Assert.Empty(probe.Declarations);
        TestGameplay.Run(f.Services, () => { Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(f.Session.CharacterId)); Assert.Empty(f.Services.GetRequiredService<DungeonRunDeliveryRegistry>().Find(run)); });
        await probe.Sender.Settled();
        if (fail) { await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Empty(physical.Sends); }
        else { var types = physical.Sends.SelectMany(CharacterDeathOracle.Types).ToArray(); Assert.Equal((ushort)0x2A, types[0]); Assert.Contains((ushort)0x03, types); }
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Task49_altar_exit_real_reward_rollback_retry_once_and_postcommit_encoder(bool giveUp, bool publication)
    {
        await using var probe = new EntryFailureProbe(false, false, ordinary: true,
            commandOverride: Whole ? giveUp ? "GameGiveUpGameCommand" : "GameEplpCommand" : "AltarActionCommand");
        using var f = new ShopFixture(s =>
        {
            probe.Configure(s); s.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>();
            s.AddSingleton<IAltarDungeonRuleService, ExitAltarRules>();
            s.AddSingleton<IDungeonStructureRuleService, ExitAltarLayout>();
            s.AddSingleton<IDungeonAdmissionRuleService, OrdinaryEntryStructures>();
            s.AddSingleton<IDungeonDifficultyRuleService, Support.TestDungeonDifficultyRules>();
            s.AddSingleton<IDungeonCellFactory, OrdinaryEntryCells>();
        });
        DungeonRunId run = default; SelectedSessionIdentity? peer = null; NetworkConnectionContext? peerConnection = null;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            if (publication)
            {
                peer = await BossTowerDomainServiceTests.AddMember(f, 0);
                var parties = f.Services.GetRequiredService<Dfo.Server.Abstractions.Parties.IPartyService>();
                PartyCharacterSnapshot Member(SelectedSessionIdentity member) => new(member.AccountId, member.CharacterId, member.WireUserId, new(1, 0, 0));
                Assert.True(parties.RequestInvite(Member(f.Session), Member(peer), new(0, 0, 0, 0)).Succeeded);
                Assert.True(parties.AcceptInvite(Member(peer), Member(f.Session)).Succeeded);
                f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().Begin(peer, new(0, 0, 0, 0, 0), 100);
                Assert.True(f.Services.GetRequiredService<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService>().TryGetByConnection(peer.ConnectionId, out var user));
                peerConnection = user!.Connection; f.Sender.Outbound.Connections.Add(peerConnection); probe.Sender.Add(peerConnection);
            }
            var scope = (Dfo.Server.Application.Transactions.DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using var tx = scope.Begin();
            var result = await f.Services.GetRequiredService<IAltarDungeonDomainService>().EnterAsync(
                new(11007, DungeonEntryFacts.None, 0, 0, peer is null ? [new(f.Session.AccountId, f.Session.CharacterId, 0, true)] : [new(f.Session.AccountId, f.Session.CharacterId, 0, true), new(peer.AccountId, peer.CharacterId, 1, false)]), default);
            Assert.NotNull(result.Entry); await tx.CommitAsync(default);
            var actual = f.Services.GetRequiredService<IDungeonRunDomainService>().Find(f.Session.CharacterId)!;
            Assert.NotNull(actual.Layout.Altar); run = actual.RunId;
            f.Services.GetRequiredService<DungeonRunDeliveryRegistry>().Register(run, peer is null ? [f.Session] : [f.Session, peer]);
        });
        f.Sql("UPDATE character_stats SET carried_gold=0 WHERE character_id=1");
        var physical = probe.Sender.Add(f.Connection); var gold = f.Gold; var encodeAttempts = 0;
        if (publication) f.Sender.Outbound.Encoder.Fail((_, message) => message.MessageType == ServerMessageType.GameExperience ? (++encodeAttempts > 0 ? probe.Cause : null) : null);
        else f.Sql("CREATE TRIGGER task49_fail_gold BEFORE UPDATE OF carried_gold ON character_stats WHEN NEW.carried_gold > OLD.carried_gold BEGIN SELECT RAISE(ABORT,'task49 reward rollback'); END");
        var error = await Record.ExceptionAsync(() => probe.Wire(f, giveUp ? (ushort)0x2A : (ushort)0x48, giveUp ? [] : [1, 2]).AsTask());
        Assert.NotNull(error);
        if (publication)
        {
            var typed = Assert.IsType<TransactionOutcomeUncertainException>(error);
            Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure, typed.Reason);
            Assert.Equal(giveUp ? 1 : 2, encodeAttempts);
            Assert.Equal("A committed altar transition could not be projected.", typed.InnerException!.Message);
            Assert.Equal(gold + 17, f.Gold); Assert.Equal(30, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(probe.Transactions).Completion);
            TestGameplay.Run(f.Services, () => { Assert.NotNull(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(f.Session.CharacterId)); Assert.Contains(f.Session.CharacterId, f.Services.GetRequiredService<IAltarDungeonDomainService>().Find(run)!.SettledParticipants); });
            Assert.True(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
            Assert.True(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(peer!)!.Fenced);
            Assert.Equal(Whole, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
            Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(peerConnection!));
            Assert.Equal(giveUp ? 100 : 117, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
            await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        else
        {
            Assert.Equal(gold, f.Gold); Assert.Equal(0, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, Assert.Single(probe.Transactions).Completion);
            TestGameplay.Run(f.Services, () => Assert.Empty(f.Services.GetRequiredService<IAltarDungeonDomainService>().Find(run)!.SettledParticipants));
            f.Sql("DROP TRIGGER task49_fail_gold");
            await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await probe.Sender.Settled();
            Assert.Empty(physical.Sends);
            Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
            Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
            var replacement = f.Connection with { };
            probe.Replacements[replacement.ConnectionId] = replacement;
            f.Sender.Outbound.Connections.Remove(replacement.ConnectionId); f.Sender.Outbound.Connections.Add(replacement);
            var retryPhysical = probe.Sender.Add(replacement);
            var retry = await Record.ExceptionAsync(() => WireOn(f, replacement, giveUp ? (ushort)0x2A : (ushort)0x48, giveUp ? [] : [1, 2]).AsTask());
            Assert.Null(retry); Assert.Equal(gold + 17, f.Gold); Assert.Equal(30, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            await WireOn(f, replacement, giveUp ? (ushort)0x2A : (ushort)0x48, giveUp ? [] : [1, 2]);
            await probe.Sender.Settled(); Assert.NotEmpty(retryPhysical.Sends); Assert.Equal(0, retryPhysical.CloseCalls);
            Assert.Equal(gold + 17, f.Gold); Assert.Equal(30, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        }
        if (Whole)
        {
            for (var i = 0; i < probe.Declarations.Count; i++)
            {
                var d = probe.Declarations[i];
                Assert.Equal(CommandWorkUnitKind.Ordinary, d.Kind); Assert.Equal(CommandFootprintMode.None, d.Footprint);
                Assert.Equal(peer is null ? new[] { f.Session } : new[] { f.Session, peer }, d.Participants.Select(p => p.Session));
                Assert.Same(i == 0 ? f.Connection : probe.Replacements[f.Connection.ConnectionId], d.Participants[0].Connection);
                if (peer is not null) Assert.Null(d.Participants[1].Connection);
            }
        }
    }
    internal static ValueTask WireOn(ShopFixture f, NetworkConnectionContext connection, ushort type, byte[] body)
    {
        var metadata = new GameMessageMetadata(1, type, 0, 9) { InboundOrdinal = 9, SequenceIsFresh = true }.WithRawBody(body);
        var registration = f.Services.GetServices<GameMessageDecoderRegistration>().Single(r => r.Selector == 1 && r.Type == type);
        var message = ((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata, body)!;
        return f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(connection), message, default);
    }
    internal static bool Contains(Exception error, Exception cause) => ReferenceEquals(error, cause) || error is AggregateException a && a.InnerExceptions.Any(x => Contains(x, cause)) || error.InnerException is { } inner && Contains(inner, cause);
}

internal sealed class ExitAltarRules : IAltarDungeonRuleService
{
    public ValueTask<AltarDungeonDefinition> GetDefinitionAsync(int id, CancellationToken ct) => ValueTask.FromResult(new AltarDungeonDefinition(id, AltarDungeonKind.Ultimate, 1, 1, 1, 3, [new(800, [], [new(1, [])])], new(0, 0, 0, 0, [], [], [], [], ""), 3));
    public AltarDungeonPolicy Policy => new();
    public bool IsOpen(int id, DateTimeOffset now) => true;
    public int AdmissionGold(AltarDungeonDefinition definition, int level, int previous) => 0;
    public IReadOnlyList<AltarScheduledWave> PlanRound(AltarMapDefinition map, int local, byte difficulty, int completed, byte level, int first) => [];
    public IReadOnlyList<AltarParticipantReward> Rewards(AltarDungeonState state, int seed) => state.Participants.Select(p => new AltarParticipantReward(p.Participant, 30, 17, [])).ToArray();
}
internal sealed class ExitAltarLayout : IDungeonStructureRuleService
{
    public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken ct) => ValueTask.FromResult<DungeonDefinition?>(new(id, "altar", 1, true, [], 86));
    public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken ct) => ValueTask.FromResult<DungeonLayout?>(new(id, 0, 1, 1, new(0, 0), new(0, 0), 800, seed, new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(800) }));
    public ValueTask<DungeonLayout> PrepareLayoutAsync(DungeonLayout layout, CancellationToken ct) => ValueTask.FromResult(layout);
    public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken ct) => ValueTask.FromResult(DungeonMapSelection.Selected(800));
}

public sealed partial class BossTowerDomainServiceTests
{
    [Theory] [InlineData((byte)0, false)] [InlineData((byte)1, false)] [InlineData((byte)2, false)] [InlineData((byte)2, true)]
    public async Task Task49_boss_result_choice_keeps_select_then_independent_leaves_and_exact_wire(byte option, bool scopeFailure)
    {
        await using var probe = new EntryFailureProbe(false, scopeFailure,
            commandOverride: DungeonExitLifecycleTests.Whole ? "GameEplpCommand" : "SelectBossTowerResultCommand|LeaveBossTowerCommand");
        using var f = Fixture(configure: s => { probe.Configure(s); });
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 3);
            var tower = Tower(f); var run = (await tower.EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default); await tower.LoadAsync(Context(run, peer), default); await tower.StartAsync(context, default);
            await Support.TestEffectFootprints.Run([f.Session, peer], cap => tower.DieAsync(context, cap, default));
            await Support.TestEffectFootprints.Run([f.Session, peer], cap => tower.DieAsync(Context(run, peer), cap, default));
        });
        var physical = probe.Sender.Add(f.Connection);
        var gold = f.Gold; var exp = f.Scalar("SELECT exp FROM characters WHERE character_id=1");
        var error = await Record.ExceptionAsync(() => probe.Wire(f, 0x48, [1, option]).AsTask()); await probe.Sender.Settled();
        if (!scopeFailure) Assert.Null(error);
        else if (DungeonExitLifecycleTests.Whole) Assert.True(DungeonExitLifecycleTests.Contains(Assert.IsType<TransactionOutcomeUncertainException>(error), probe.Cause));
        else Assert.Same(probe.Cause, error);
        Assert.Equal(option == 2 && (!scopeFailure || DungeonExitLifecycleTests.Whole) ? 3 : 1, probe.Transactions.Count);
        Assert.All(probe.Transactions, tx => Assert.Equal(DomainTransactionCompletion.Committed, tx.Completion));
        Assert.Equal(gold, f.Gold); Assert.Equal(exp, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(scopeFailure && !DungeonExitLifecycleTests.Whole ? [] : new ushort[] { 0x48 }, physical.Sends.SelectMany(CharacterDeathOracle.Types).ToArray());
        TestGameplay.Run(f.Services, () => { if (option == 2 && (!scopeFailure || DungeonExitLifecycleTests.Whole)) { Assert.Null(Tower(f).Find(f.Session)); Assert.Null(Tower(f).Find(peer)); } else Assert.NotNull(Tower(f).Find(f.Session)); });
        if (scopeFailure)
        {
            await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, physical.CloseCalls);
            foreach (var member in new[] { f.Session, peer })
                Assert.Equal(DungeonExitLifecycleTests.Whole, f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(member)!.Fenced);
            Assert.Equal(DungeonExitLifecycleTests.Whole, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        }
        if (DungeonExitLifecycleTests.Whole)
        {
            Assert.Equal(option == 2 ? 3 : 1, probe.Declarations.Count);
            Assert.All(probe.Declarations, d => { Assert.Equal(CommandWorkUnitKind.Ordinary, d.Kind); Assert.Equal(new[] { f.Session, peer }, d.Participants.Select(p => p.Session)); });
            Assert.Equal(CommandFootprintMode.None, probe.Declarations[0].Footprint);
            Assert.Same(f.Connection, probe.Declarations[0].Participants[0].Connection); Assert.Null(probe.Declarations[0].Participants[1].Connection);
            if (option == 2) { Assert.Same(f.Connection, probe.Declarations[1].Participants[0].Connection); Assert.Null(probe.Declarations[1].Participants[1].Connection); Assert.Null(probe.Declarations[2].Participants[0].Connection); Assert.Equal(peer.ConnectionId, probe.Declarations[2].Participants[1].Connection!.ConnectionId); }
            if (option == 2) Assert.All(probe.Declarations.Skip(1), d => { Assert.Equal(CommandFootprintMode.DeclaredNonRequest, d.Footprint); Assert.Equal(new[] { f.Session, peer }, d.FootprintOwners); });
        }
    }
}
public sealed partial class IllusionTowerDomainServiceTests
{
    [Theory] [InlineData((byte)0, false)] [InlineData((byte)1, false)] [InlineData((byte)2, false)] [InlineData((byte)2, true)]
    public async Task Task49_illusion_result_choice_keeps_plain_empty_selected_transaction(byte option, bool scopeFailure)
    {
        await using var probe = new EntryFailureProbe(true, scopeFailure,
            commandOverride: DungeonExitLifecycleTests.Whole ? "GameEplpCommand" : "SelectIllusionTowerReturnCommand");
        using var f = Fixture(rewards: true, configure: probe.Configure);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await f.Seed(65, 4183, 3); var tower = Tower(f); var entry = await Enter(f); Assert.Null(entry.Rejection); var run = entry.Run!; var context = Context(run, f.Session);
            await tower.LoadAsync(context, default); await tower.StartAsync(context, default);
            await Support.TestEffectFootprints.Run(f.Session, cap => tower.DieAsync(context, cap, default));
        });
        var physical = probe.Sender.Add(f.Connection); var gold = f.Gold;
        var error = await Record.ExceptionAsync(() => probe.Wire(f, 0x48, [1, option]).AsTask()); await probe.Sender.Settled();
        if (!scopeFailure) Assert.Null(error);
        else if (DungeonExitLifecycleTests.Whole) Assert.True(DungeonExitLifecycleTests.Contains(Assert.IsType<TransactionOutcomeUncertainException>(error), probe.Cause));
        else Assert.Same(probe.Cause, error);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(probe.Transactions).Completion);
        Assert.Equal(scopeFailure && !DungeonExitLifecycleTests.Whole ? [] : new ushort[] { 0x48 }, physical.Sends.SelectMany(CharacterDeathOracle.Types).ToArray());
        Assert.Equal(gold, f.Gold);
        TestGameplay.Run(f.Services, () => Assert.Equal(option, Tower(f).Find(f.Session)!.ReturnOption));
        if (scopeFailure) { await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, physical.CloseCalls); }
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        if (DungeonExitLifecycleTests.Whole) { var d = Assert.Single(probe.Declarations); Assert.Empty(d.Participants); Assert.Equal(CommandFootprintMode.None, d.Footprint); }
    }
}
