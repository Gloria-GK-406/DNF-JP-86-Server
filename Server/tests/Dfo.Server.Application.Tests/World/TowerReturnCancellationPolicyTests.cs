using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Actual_typed_sweep_returns_members_with_None_after_final_leave_commit_cancels_the_producer()
    {
        var trace = new TowerReturnSourceTrace();
        var view = new TowerReturnCancellationView();
        using var fixture = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(view);
        });
        await TowerReturnCancellationCases.RunAsync(fixture, trace, view, true);
    }
}

public sealed partial class IllusionTowerDomainServiceTests
{
    [Fact]
    public async Task Actual_typed_sweep_keeps_the_producer_token_for_return_after_final_leave_commit()
    {
        var trace = new TowerReturnSourceTrace();
        var view = new TowerReturnCancellationView();
        using var fixture = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(view);
        });
        await TowerReturnCancellationCases.RunAsync(fixture, trace, view, false);
    }
}

internal sealed class TowerReturnCancellationView : ISelectedCharacterCurrentViewDomainService
{
    internal List<(SelectedSessionIdentity Session, CancellationToken Token)> Reads { get; } = [];
    public ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken token)
    {
        Reads.Add((session, token));
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult<SelectCharacterResult?>(null);
    }
}

internal static class TowerReturnCancellationCases
{
    internal static async Task RunAsync(ShopFixture fixture, TowerReturnSourceTrace trace, TowerReturnCancellationView view, bool boss)
    {
        using var producer = new CancellationTokenSource();
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            fixture.SetLevel((byte)(boss ? 65 : 50));
            await fixture.Seed(65, boss ? 2749110 : 4183, 3);
            peer = await TowerSourceMembers.AddAsync(fixture, 3, 2, boss ? 65 : 50, boss ? 2749110 : 4183);
            if (boss)
                Assert.NotNull((await UseCaseDungeons.Boss(fixture.Services)
                    .EnterAsync([new(fixture.Session, 0), new(peer, 1)], fixture.Session, 42, default)).Run);
            else
            {
                var towers = UseCaseDungeons.Illusion(fixture.Services);
                var run = (await towers.EnterAsync([new(fixture.Session, 0), new(peer, 1)], fixture.Session, 42, default, 11001)).Run!;
                var context = new TowerActionContext(fixture.Session, run.RunId, run.Room.Version);
                await towers.LoadAsync(context, default);
                await towers.LoadAsync(context with { Session = peer }, default);
                await towers.StartAsync(context, default);
                await TestEffectFootprints.Run([fixture.Session, peer], cap => towers.DieAsync(context, cap, default));
                await TestEffectFootprints.Run([fixture.Session, peer], cap => towers.DieAsync(context with { Session = peer }, cap, default));
                Assert.True(towers.Find(context.Session)!.Completion!.Settled); // a death settles the run
            }
        });
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        trace.CancelAfterCommit = producer;
        trace.CancelAfterSession = peer;
        var failure = await Record.ExceptionAsync(() => TowerTestSources.Tick(fixture, boss, producer.Token));

        Assert.True(producer.IsCancellationRequested);
        Assert.Equal([fixture.Session, peer], trace.Attempts.Select(attempt => attempt.Session));
        Assert.All(trace.Attempts, attempt =>
        {
            Assert.Equal(producer.Token, attempt.Token);
            Assert.Equal(DomainTransactionCompletion.Committed, attempt.Transaction.Completion);
        });
        if (boss)
        {
            Assert.Null(failure);
            Assert.Equal([fixture.Session, peer], view.Reads.Select(read => read.Session));
            Assert.All(view.Reads, read => Assert.Equal(CancellationToken.None, read.Token));
        }
        else
        {
            Assert.Null(failure);
            var error = Assert.Single(trace.ExecutorLog.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error).Exception;
            Assert.Equal(producer.Token, Assert.IsType<OperationCanceledException>(Assert.IsType<Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException>(error).InnerException).CancellationToken);
            Assert.Equal(producer.Token, Assert.Single(view.Reads).Token);
        }
        TestGameplay.Run(fixture.Services, () =>
        {
            if (boss)
            {
                Assert.Null(fixture.Services.GetRequiredService<IBossTowerDomainService>().Find(fixture.Session));
                Assert.Null(fixture.Services.GetRequiredService<IBossTowerDomainService>().Find(peer));
            }
            else
            {
                Assert.Null(fixture.Services.GetRequiredService<IIllusionTowerDomainService>().Find(fixture.Session));
                Assert.Null(fixture.Services.GetRequiredService<IIllusionTowerDomainService>().Find(peer));
            }
        });
    }
}
