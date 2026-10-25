using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData("action")]
    [InlineData("commit")]
    [InlineData("uncertain")]
    [InlineData("scope")]
    public async Task Test_only_authorized_sweep_keeps_later_due_run_after_real_SQL_transaction_or_scope_failure(string mode)
    {
        var trace = new TowerReturnSourceTrace { FailureMode = mode };
        var log = new RecordingExecutionLogger<BossTowerProjectionDelivery>();
        using var fixture = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
            services.AddSingleton<ILogger<BossTowerProjectionDelivery>>(log);
        });
        await TowerReturnAuthorizedCases.RunAsync(fixture, trace, mode, true, () => log.Entries.ToArray());
    }
}

public sealed partial class IllusionTowerDomainServiceTests
{
    [Theory]
    [InlineData("action")]
    [InlineData("commit")]
    [InlineData("uncertain")]
    [InlineData("scope")]
    public async Task Test_only_authorized_sweep_keeps_later_due_run_after_real_SQL_transaction_or_scope_failure(string mode)
    {
        var trace = new TowerReturnSourceTrace { FailureMode = mode };
        var log = new RecordingExecutionLogger<IllusionTowerProjectionDelivery>();
        using var fixture = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
            services.AddSingleton<ILogger<IllusionTowerProjectionDelivery>>(log);
        });
        await TowerReturnAuthorizedCases.RunAsync(fixture, trace, mode, false, () => log.Entries.ToArray());
    }
}

internal static class TowerReturnAuthorizedCases
{
    internal static async Task RunAsync(ShopFixture fixture, TowerReturnSourceTrace trace, string mode, bool boss,
        Func<IReadOnlyList<ExecutionLogEntry>> logs)
    {
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            fixture.SetLevel((byte)(boss ? 65 : 50));
            await fixture.Seed(65, boss ? 2749110 : 4183, 3);
            peer = await TowerSourceMembers.AddAsync(fixture, 3, 2, boss ? 65 : 50, boss ? 2749110 : 4183);
            foreach (var session in new[] { fixture.Session, peer })
            {
                if (boss)
                    Assert.NotNull((await fixture.Services.GetRequiredService<IBossTowerDomainService>()
                        .EnterAsync([new(session, 0)], session, 42, default)).Run);
                else
                {
                    var towers = fixture.Services.GetRequiredService<IIllusionTowerDomainService>();
                    var run = (await towers.EnterAsync([new(session, 0)], session, 42, default)).Run!;
                    var context = new TowerActionContext(session, run.RunId, run.Room.Version);
                    await towers.LoadAsync(context, default);
                    await towers.StartAsync(context, default);
                    await TestEffectFootprints.Run([session], cap => towers.DieAsync(context, cap, default));
                    Assert.True((await towers.MarkSettledAsync(context, new([]), default)).Changed);
                }
            }
        });
        trace.FaultSession = fixture.Session;
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        if (mode == "action")
            fixture.Sql("CREATE TRIGGER tower_algorithm_fault BEFORE UPDATE OF carried_gold ON character_stats WHEN NEW.character_id=1 BEGIN SELECT RAISE(ABORT,'tower algorithm action SQL'); END;");
        if (mode == "commit")
            fixture.Sql("CREATE TABLE tower_algorithm_parent(id INTEGER PRIMARY KEY); CREATE TABLE tower_algorithm_child(id INTEGER REFERENCES tower_algorithm_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER tower_algorithm_fault AFTER UPDATE OF carried_gold ON character_stats WHEN NEW.character_id=1 BEGIN INSERT INTO tower_algorithm_child VALUES(404); END;");

        // This is the authorized legacy algorithm seam, not the production timer source: that
        // source currently cannot obtain a legacy footprint at all. It does not run this frame.
        await TestGameplay.RunAsync(fixture.Services, async () =>
            await TestEffectFootprints.NonEffect(fixture.Services, GameMessageMetadata.Empty, async () =>
            {
                if (boss) await fixture.Services.GetRequiredService<BossTowerProjectionDelivery>().SweepAsync(fixture.Clock.GetUtcNow(), default);
                else await fixture.Services.GetRequiredService<IllusionTowerProjectionDelivery>().SweepAsync(fixture.Clock.GetUtcNow(), default);
            }));

        Assert.Equal([fixture.Session, peer], trace.Attempts.Select(attempt => attempt.Session));
        var first = trace.Attempts[0];
        Assert.NotSame(first.Transaction, trace.Attempts[1].Transaction);
        Assert.Equal(DomainTransactionCompletion.Committed, trace.Attempts[1].Transaction.Completion);
        Assert.Equal(107, fixture.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
        Assert.Equal(mode is "action" or "commit" ? 100000 : 100007, fixture.Gold);
        var warning = Assert.Single(logs(), entry => entry.Level == LogLevel.Warning);
        if (mode is "action" or "commit")
        {
            var cause = Assert.IsType<SqliteException>(warning.Exception);
            Assert.Equal(mode == "action" ? 1811 : 787, cause.SqliteExtendedErrorCode);
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, first.Transaction.Completion);
            if (mode == "action") Assert.Same(cause, first.Error);
            else
            {
                Assert.Same(cause, first.Transaction.CommitFailure!.Cause);
                Assert.Contains("SqliteTransaction.Commit", cause.StackTrace!);
            }
        }
        else if (mode == "uncertain")
        {
            Assert.Equal(DomainTransactionCompletion.Uncertain, first.Transaction.Completion);
            Assert.Same(trace.Cause, Assert.IsType<TransactionOutcomeUncertainException>(warning.Exception).InnerException);
        }
        else
        {
            Assert.Equal(DomainTransactionCompletion.Committed, first.Transaction.Completion);
            Assert.Same(trace.Cause, warning.Exception);
        }
        TestGameplay.Run(fixture.Services, () =>
        {
            if (boss)
            {
                Assert.Equal(mode is "action" or "commit", fixture.Services.GetRequiredService<IBossTowerDomainService>().Find(fixture.Session) is not null);
                Assert.Null(fixture.Services.GetRequiredService<IBossTowerDomainService>().Find(peer));
            }
            else
            {
                Assert.Equal(mode is "action" or "commit", fixture.Services.GetRequiredService<IIllusionTowerDomainService>().Find(fixture.Session) is not null);
                Assert.Null(fixture.Services.GetRequiredService<IIllusionTowerDomainService>().Find(peer));
            }
            var selected = fixture.Services.GetRequiredService<ISelectedSessionDomainService>();
            Assert.Equal(boss && mode == "uncertain", selected.StandingOf(fixture.Session)!.Fenced);
            Assert.False(selected.StandingOf(peer)!.Fenced);
        });
        Assert.Equal(boss && mode == "uncertain" ? new[] { fixture.Session.ConnectionId } : [], fixture.Sender.Outbound.Closes);
        Assert.Equal(boss, first.Recipient is not null);
        if (mode is "action" or "commit")
        {
            // The failing first run remains due. A new later cohort must still finish on the next
            // scan; merely attempting it once would not detect starvation behind a persistent fault.
            await TestGameplay.RunAsync(fixture.Services, async () =>
            {
                if (boss)
                    Assert.NotNull((await fixture.Services.GetRequiredService<IBossTowerDomainService>()
                        .EnterAsync([new(peer, 0)], peer, 44, default)).Run);
                else
                {
                    var towers = fixture.Services.GetRequiredService<IIllusionTowerDomainService>();
                    var run = (await towers.EnterAsync([new(peer, 0)], peer, 44, default)).Run!;
                    var context = new TowerActionContext(peer, run.RunId, run.Room.Version);
                    await towers.LoadAsync(context, default);
                    await towers.StartAsync(context, default);
                    await TestEffectFootprints.Run([peer], cap => towers.DieAsync(context, cap, default));
                    Assert.True((await towers.MarkSettledAsync(context, new([]), default)).Changed);
                }
            });
            fixture.Clock.Advance(TimeSpan.FromMinutes(5));
            await TestGameplay.RunAsync(fixture.Services, async () =>
                await TestEffectFootprints.NonEffect(fixture.Services, GameMessageMetadata.Empty, async () =>
                {
                    if (boss) await fixture.Services.GetRequiredService<BossTowerProjectionDelivery>().SweepAsync(fixture.Clock.GetUtcNow(), default);
                    else await fixture.Services.GetRequiredService<IllusionTowerProjectionDelivery>().SweepAsync(fixture.Clock.GetUtcNow(), default);
                }));
            Assert.Equal([fixture.Session, peer, fixture.Session, peer], trace.Attempts.Select(attempt => attempt.Session));
            Assert.Equal(4, trace.Attempts.Select(attempt => attempt.Transaction).Distinct().Count());
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, trace.Attempts[2].Transaction.Completion);
            Assert.Equal(DomainTransactionCompletion.Committed, trace.Attempts[3].Transaction.Completion);
            Assert.Equal(114, fixture.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
            Assert.Equal(100000, fixture.Gold);
            Assert.Equal(2, logs().Count(entry => entry.Level == LogLevel.Warning));
        }
    }
}
