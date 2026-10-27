using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.FinishLoading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData("action")]
    [InlineData("commit")]
    [InlineData("uncertain")]
    [InlineData("scope")]
    public async Task Departure_source_keeps_a_later_actual_ready_cohort_after_the_first_real_command_fails(string failure)
    {
        var trace = new TowerReturnSourceTrace { FailureMode = failure };
        var log = new RecordingExecutionLogger<BossTowerReturnSweepService>();
        using var f = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<ILogger<BossTowerReturnSweepService>>(log);
        });
        SelectedSessionIdentity first = null!, second = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65);
            await f.Seed(65, 2749110, 3);
            first = await AddMember(f, 3);
            Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(first.ConnectionId, out var online));
            f.Sender.Outbound.Connections.Add(online!.Connection);
            var third = await TowerSourceMembers.AddAsync(f, 4, 3, 65, 2749110);
            second = await TowerSourceMembers.AddAsync(f, 5, 4, 65, 2749110);
            foreach (var cohort in new[] { new[] { f.Session, first }, new[] { third, second } })
            {
                var run = (await Tower(f).EnterAsync([new(cohort[0], 0), new(cohort[1], 1)], cohort[0], 42, default)).Run!;
                await Tower(f).LoadAsync(Context(run, cohort[0]), default);
                await Tower(f).LoadAsync(Context(run, cohort[1]), default);
                var departure = await TestEffectFootprints.Run(cohort,
                    cap => Tower(f).LeaveAsync(Context(run, cohort[0]), false, cap, default));
                Assert.True(departure.Changed);
                Assert.Equal(TowerRoomPhase.Ready, departure.Run!.Room.Phase);
                f.Services.GetRequiredService<BossTowerDepartureProjectionQueue>().Enqueue(departure);
            }
        });
        trace.FaultSession = first;
        if (failure == "action")
            f.Sql("CREATE TRIGGER tower_source_fault BEFORE UPDATE OF carried_gold ON character_stats WHEN NEW.character_id=3 BEGIN SELECT RAISE(ABORT,'tower source actual action SQL'); END;");
        if (failure == "commit")
            f.Sql("CREATE TABLE tower_source_parent(id INTEGER PRIMARY KEY); CREATE TABLE tower_source_child(id INTEGER REFERENCES tower_source_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER tower_source_fault AFTER UPDATE OF carried_gold ON character_stats WHEN NEW.character_id=3 BEGIN INSERT INTO tower_source_child VALUES(404); END;");

        await f.Services.GetServices<IHostedService>().OfType<BossTowerReturnSweepService>().Single().TickAsync(default);

        Assert.Equal([first, second], trace.Attempts.Select(attempt => attempt.Session));
        var failed = trace.Attempts[0];
        var later = trace.Attempts[1];
        Assert.NotSame(failed.Transaction, later.Transaction);
        Assert.Equal(DomainTransactionCompletion.Committed, later.Transaction.Completion);
        Assert.Equal(107, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=5"));
        Assert.Equal(failure is "action" or "commit" ? 100 : 107,
            f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
        var warning = Assert.Single(trace.ExecutorLog.Entries, entry => entry.Level == LogLevel.Error);
        if (failure is "action" or "commit")
        {
            var cause = Assert.IsType<SqliteException>(warning.Exception);
            Assert.Equal(failure == "action" ? 1811 : 787, cause.SqliteExtendedErrorCode);
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, failed.Transaction.Completion);
            if (failure == "action") Assert.Same(cause, failed.Error);
            else
            {
                Assert.Same(cause, failed.Transaction.CommitFailure!.Cause);
                SqliteFailureAssertions.FromCommit(failed.Transaction, cause);
            }
        }
        else if (failure == "uncertain")
        {
            Assert.Equal(DomainTransactionCompletion.Uncertain, failed.Transaction.Completion);
            var uncertain = Assert.IsType<TransactionOutcomeUncertainException>(warning.Exception);
            Assert.Same(trace.Cause, uncertain.InnerException);
        }
        else
        {
            Assert.Equal(DomainTransactionCompletion.Committed, failed.Transaction.Completion);
            Assert.Same(trace.Cause, Assert.IsType<TransactionOutcomeUncertainException>(warning.Exception).InnerException);
        }
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Equal(failure is "action" or "commit" ? TowerRoomPhase.Ready : TowerRoomPhase.Fighting,
                Tower(f).Find(first)!.Room.Phase);
            Assert.Equal(TowerRoomPhase.Fighting, Tower(f).Find(second)!.Room.Phase);
            var selected = f.Services.GetRequiredService<ISelectedSessionDomainService>();
            Assert.Equal(failure is "uncertain" or "scope", selected.StandingOf(first)!.Fenced);
            Assert.False(selected.StandingOf(second)!.Fenced);
        });
        Assert.Contains(f.Sender.Outbound.MessagesFor(second.ConnectionId), message => message is LoadingCompleteNotification);
        Assert.Equal(failure is "uncertain" or "scope" ? new[] { first.ConnectionId } : [], f.Sender.Outbound.Closes);
        Assert.Null(failed.Chain.SourceConnection);
        Assert.Null(failed.Recipient!.Metadata);
        Assert.NotNull(failed.Recipient.Connection);
    }
}

internal static class TowerSourceMembers
{
    internal static async Task<SelectedSessionIdentity> AddAsync(ShopFixture fixture, int character, int account, int level, int ticket)
    {
        fixture.Sql($"INSERT INTO accounts(account_id,m_id) VALUES({account},'return-source-{account}'); INSERT INTO characters(character_id,account_id,name,job,level,exp,slot_index) VALUES({character},{account},'return{character}',12,{level},{fixture.ExperienceForLevel((byte)level)},0); INSERT INTO character_stats(character_id,carried_gold) VALUES({character},100);");
        var registry = fixture.Services.GetRequiredService<OnlineStateRegistry>();
        var connection = new NetworkConnectionContext(new($"return-source-{character}"), NetworkEndpointKind.Game, 10011);
        var user = registry.RegisterConnection(connection).OnlineUserId;
        registry.Authenticate(user, new(account));
        var session = SelectedSessionIdentity.Create(new(account), new(character), connection.ConnectionId,
            registry.GetRequiredUser(user).Generation, 10011);
        registry.Select(session);
        registry.EnterWorld(session);
        fixture.Sender.Outbound.Connections.Add(connection);
        var accessor = fixture.Services.GetRequiredService<IDomainTransactionAccessor>();
        var store = fixture.Services.GetRequiredService<IItemDomainStore>();
        fixture.Services.GetRequiredService<ICharacterContainerDomainService>().Register(session.CharacterId,
            new PersistentItemContainer(ContainerId.ForCharacter(session.CharacterId, InventoryContainerType.Main),
                new(InventoryContainerType.Main, 0), store, accessor,
                fixture.Services.GetRequiredService<IItemPlacementRuleService>(), fixture.Services.GetRequiredService<IItemSlotRuleService>()));
        await using var transaction = ((DomainTransactionScope)accessor).Begin();
        var storage = store.SessionIn(accessor.Current!);
        await storage.PutAsync(ContainerId.ForCharacter(session.CharacterId, InventoryContainerType.Main), 65,
            await storage.CreateAsync(ItemSnapshot.FromCore(StackableItemCore.Create(2, ticket, 3), true), default), default);
        await transaction.CommitAsync(default);
        return session;
    }
}
