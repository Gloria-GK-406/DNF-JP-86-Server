using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ScheduledCleanupTransactionTests
{
    [Fact]
    public async Task A_failed_character_rolls_back_its_items_and_event_while_the_next_commits_and_a_later_run_retries()
    {
        using var f = new ShopFixture(services =>
        {
            services.AddSingleton<OnlineExpiredItemCleanupDomainService>();
            services.AddSingleton<ControlledCleanup>();
            services.AddSingleton<IOnlineExpiredItemCleanupDomainService>(sp => sp.GetRequiredService<ControlledCleanup>());
        });
        await f.SeedCore(121, StackableItemCore.Create(2, 9001, 3) with { ExpireTime = 1 });
        f.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'scheduled-second'); " +
            "INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(3,2,'second',12,1,0);");
        var second = await TestGameplay.RunAsync(f.Services, async () =>
        {
            var state = f.Services.GetRequiredService<OnlineStateRegistry>();
            var connection = new NetworkConnectionContext(new("scheduled-second"), NetworkEndpointKind.Game, 10011);
            var user = state.RegisterConnection(connection);
            state.Authenticate(user.OnlineUserId, new(2));
            var session = SelectedSessionIdentity.Create(new(2), new(3), connection.ConnectionId,
                state.GetRequiredUser(user.OnlineUserId).Generation, 10011);
            state.Select(session);
            state.EnterWorld(session);
            await using var tx = ((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            var items = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(tx.Transaction);
            var record = await items.CreateAsync(ItemSnapshot.FromCore(
                StackableItemCore.Create(2, 9001, 3) with { ExpireTime = 1 }, true), default);
            await items.PutAsync(ContainerId.ForCharacter(session.CharacterId, InventoryContainerType.Main), 121, record, default);
            await tx.CommitAsync(default);
            return session;
        });
        var controlled = f.Services.GetRequiredService<ControlledCleanup>();
        var failure = await Assert.ThrowsAsync<AggregateException>(() => TestGameplay.RunAsync(f.Services,
            async () => await f.Services.GetRequiredService<OnlineExpiredItemCleanupJob>().RunAsync(default)));

        Assert.Same(controlled.Failure, Assert.Single(failure.InnerExceptions));
        Assert.Equal(new[] { 1, 3 }, controlled.Attempts);
        Assert.Equal(3, f.Count(9001));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_scope='character' AND owner_id=3"));
        var persistence = f.Services.GetRequiredService<IDomainEventPersistence>();
        var firstEvents = await persistence.ReadDueAsync("inventory.expiration.notifications", f.Clock.GetUtcNow(), 100, default);
        Assert.Equal(second.CharacterId, System.Text.Json.JsonSerializer.Deserialize<OnlineExpiredItemsRemoved>(
            Assert.Single(firstEvents).Event.PayloadJson)!.CharacterId);

        controlled.Fail = false;
        await TestGameplay.RunAsync(f.Services,
            async () => await f.Services.GetRequiredService<OnlineExpiredItemCleanupJob>().RunAsync(default));
        Assert.Equal(new[] { 1, 3, 1, 3 }, controlled.Attempts);
        Assert.Equal(0, f.Count(9001));
        Assert.Equal(2, (await persistence.ReadDueAsync("inventory.expiration.notifications", f.Clock.GetUtcNow(), 100, default)).Count);
    }

    private sealed class ControlledCleanup(OnlineExpiredItemCleanupDomainService real) : IOnlineExpiredItemCleanupDomainService
    {
        internal IOException Failure { get; } = new("first character fails after its actual item and event writes");
        internal List<int> Attempts { get; } = [];
        internal bool Fail { get; set; } = true;
        public async ValueTask<bool> CleanupAsync(SelectedSessionIdentity session, CancellationToken ct)
        {
            Attempts.Add(session.CharacterId.Value);
            var result = await real.CleanupAsync(session, ct);
            if (Fail && session.CharacterId.Value == 1) throw Failure;
            return result;
        }
    }
}
