using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Altar;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task58_prepare_projects_before_current_quota_even_when_ready_is_refused(bool refuse)
    {
        var altar = new AltarProtocolTrace { Refuse = refuse };
        using var f = Fixture(configure: altar.Configure);
        await SetupAltarProtocol(f, altar);
        await EffectRequestTransport.SendRaw(f, new(1, 0x102, 0, 7), []);
        var frames = f.Sender.Outbound.Frames.ToArray();
        Assert.Equal(refuse ? new ushort[] { 0x227 } : new ushort[] { 0xD7, 0xD2, 0x227 }, frames.Select(Type));
        Assert.Equal(2, frames[^1][19]);
        Assert.Equal(new[] { "ready", "quota" }, altar.Calls);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(altar.Transactions).Completion);
        Assert.Equal(!refuse, WakeObservation.Pending(f.Services.GetRequiredService<AltarWakeSignal>()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task58_manual_difficulty_ack_precedes_wave_without_duplicate_resolution_and_preserves_rejection(bool refuse)
    {
        var altar = new AltarProtocolTrace { Refuse = refuse };
        using var f = Fixture(configure: altar.Configure);
        await SetupAltarProtocol(f, altar);
        await EffectRequestTransport.SendRaw(f, new(1, 0x13D, 0, 7), [2]);
        var frames = f.Sender.Outbound.Frames.ToArray();
        Assert.Equal(refuse ? new ushort[] { 0x13D } : new ushort[] { 0x13D, 0xD7, 0xD2 }, frames.Select(Type));
        Assert.Equal(1, frames[0][0]);
        Assert.Equal(refuse ? new byte[] { 0, 1 } : new byte[] { 1, 2 }, frames[0][15..]);
        Assert.DoesNotContain(frames, x => Type(x) == 0x129);
        Assert.Equal(new[] { "difficulty:2" }, altar.Calls);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(altar.Transactions).Completion);
        Assert.Equal(!refuse, WakeObservation.Pending(f.Services.GetRequiredService<AltarWakeSignal>()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task58_death_batch_deduplicates_in_one_transaction_and_ignores_nonfighting_state(bool fighting)
    {
        var altar = new AltarProtocolTrace();
        using var f = Fixture(configure: altar.Configure);
        await SetupAltarProtocol(f, altar);
        altar.State = altar.State! with { Status = fighting ? AltarStatus.Fighting : AltarStatus.ChoosingDifficulty };
        await EffectRequestTransport.SendRaw(f, new(1, 0xFF, 0, 7), [3, 1, 0, 1, 0, 2, 0]);
        Assert.Equal(fighting ? new ushort[] { 1, 2 } : [], altar.Actors);
        if (fighting)
        {
            Assert.Equal(2, altar.Transactions.Count);
            Assert.Same(altar.Transactions[0], altar.Transactions[1]);
            Assert.All(altar.Transactions, x => Assert.Equal(DomainTransactionCompletion.Committed, x.Completion));
        }
        else Assert.Empty(altar.Transactions);
        var frames = f.Sender.Outbound.Frames.ToArray();
        Assert.Equal(fighting ? new ushort[] { 0x25, 0x26, 0x25, 0x26 } : [], frames.Select(Type));
        if (fighting) Assert.Equal(new ushort[] { 1, 2 }, frames.Where(x => Type(x) == 0x26).Select(x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(15))));
        Assert.True(WakeObservation.Pending(f.Services.GetRequiredService<AltarWakeSignal>()));
        var experience = f.Scalar("SELECT exp FROM characters WHERE character_id=1");
        await EffectRequestTransport.SendRaw(f, new(1, 0xFF, 0, 8), [2, 1, 0, 2, 0]);
        Assert.Equal(experience, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(frames.Length, f.Sender.Outbound.Frames.Count);
    }

    private static ushort Type(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
    private static Task SetupAltarProtocol(ShopFixture f, AltarProtocolTrace altar) => TestGameplay.RunAsync(f.Services, async () =>
    {
        f.SetLevel(65);
        await using var tx = Scope(f).Begin();
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(144, 0, 1, 1, cell, cell, 61000, 42,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(61000) })
            { Mode = new DungeonRunMode.Altar(new(AltarDungeonKind.Ultimate, new(2026, 10, 26))) };
        var run = f.Services.GetRequiredService<IDungeonRunDomainService>().PreparePartyEntry(
            [new(f.Session.AccountId, f.Session.CharacterId, 0, true)], layout, 0,
            new(cell, 61000, 1, [new(1, new(13099, 1, 0), false, false, []), new(2, new(13099, 1, 0), false, false, [])], [])
            { MechanismBlocksClear = true }, DungeonRunId.New(), f.Clock.GetUtcNow()).Run;
        f.Services.GetRequiredService<DungeonRunDeliveryRegistry>().Register(run.RunId, [f.Session]);
        altar.State = new(run.RunId, new(144, AltarDungeonKind.Ultimate, 1, 1, 1, 0, [], new(0, 0, 0, 0, [], [], [], [], ""), 3),
            [], f.Clock.GetUtcNow(), run.RoomVersion, 61000) { Generation = 4, Difficulty = 2 };
        await tx.CommitAsync(default);
    });

    private sealed class AltarProtocolTrace
    {
        internal AltarDungeonState? State;
        internal bool Refuse;
        internal List<string> Calls = [];
        internal List<ushort> Actors = [];
        internal List<DomainTransaction> Transactions = [];
        internal void Configure(IServiceCollection services)
        {
            services.AddSingleton<IAltarDungeonDomainService>(p => TownMovementProtocolCommandFlowTests.Proxy<IAltarDungeonDomainService>.Create((name, args) => name switch
            {
                "Find" => State,
                "ReadyAsync" => Act(p, "ready"),
                "SelectDifficultyAsync" => Act(p, "difficulty:" + args![4]),
                _ => throw new NotSupportedException(name),
            }));
            services.AddSingleton<IUsageQuotaSnapshotDomainService>(p => TownMovementProtocolCommandFlowTests.Proxy<IUsageQuotaSnapshotDomainService>.Create((name, _) =>
            {
                Calls.Add("quota");
                Assert.Null(p.GetRequiredService<IDomainTransactionAccessor>().Current);
                return ValueTask.FromResult(new UsageQuotaSnapshot(DateTimeOffset.UnixEpoch, [], null, [], null,
                    Altars: [new(144, new(2026, 10, 26), 1, 2, 3)]));
            }));
            var descriptor = services.Last(x => x.ServiceType == typeof(IMonsterDeathDomainService));
            services.Remove(descriptor);
            services.AddSingleton<IMonsterDeathDomainService>(p => new TracedDeaths(
                (IMonsterDeathDomainService)(descriptor.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p, descriptor.ImplementationType!)), p, this));
        }
        private ValueTask<AltarActionResult?> Act(IServiceProvider p, string call)
        {
            Calls.Add(call);
            Transactions.Add(Assert.IsType<DomainTransaction>(p.GetRequiredService<IDomainTransactionAccessor>().Current));
            Assert.False(WakeObservation.Pending(p.GetRequiredService<AltarWakeSignal>()));
            return ValueTask.FromResult<AltarActionResult?>(Refuse ? null : new(State!)
            { RoundStarted = true, DifficultyResolved = call.StartsWith("difficulty", StringComparison.Ordinal), Spawns = [new(3, 13099, 0, 1, 100, 20, 30, 0)] });
        }
        private sealed class TracedDeaths(IMonsterDeathDomainService actual, IServiceProvider p, AltarProtocolTrace trace) : IMonsterDeathDomainService
        {
            public ValueTask<ObjectDestructionResult> DestroyObjectAsync(SelectedSessionIdentity reporter, DungeonRunId runId, long roomVersion,
                int objectCode, IReadOnlyList<SelectedSessionIdentity> recipients, CancellationToken ct) =>
                actual.DestroyObjectAsync(reporter, runId, roomVersion, objectCode, recipients, ct);
            public ValueTask<MonsterDeathSettlementResult> SettleAsync(MonsterDeathSettlementRequest request, IEffectFootprintRecorder footprint, CancellationToken ct)
            {
                trace.Actors.Add(request.Actor);
                trace.Transactions.Add(Assert.IsType<DomainTransaction>(p.GetRequiredService<IDomainTransactionAccessor>().Current));
                return actual.SettleAsync(request, footprint, ct);
            }
        }
    }
}
