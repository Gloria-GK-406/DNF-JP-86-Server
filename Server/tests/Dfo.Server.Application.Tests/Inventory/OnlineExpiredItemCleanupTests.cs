using Dfo.Server.Testing;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class OnlineExpiredItemCleanupTests
{
    private static ValueTask<bool> CleanupSession(ShopFixture f, SelectedSessionIdentity session) =>
        ScheduledCleanupFixture.RunSessionAsync(f.Services, session);
    private static void Select(ShopFixture f, int character)
    {
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(new(character)));
        f.Select(character);
    }
    private const string Subscriber = "inventory.expiration.notifications";
    private static DomainTransactionScope Transactions(ShopFixture f) =>
        (DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
    private static async Task Seed(ShopFixture f)
    {
        await f.SeedCore(121, StackableItemCore.Create(2, 9001, 3) with { ExpireTime = 1 });
        await f.SeedCore(122, StackableItemCore.Create(2, 9005, 3) with { ExpireTime = int.MaxValue });
        await f.SeedCore(123, StackableItemCore.Create(2, 9006, 3) with { ExpireTime = int.MaxValue - 1 });
        f.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().EnsureInWorld(f.Session);
    }
    private static ValueTask<IReadOnlyList<PendingDomainEventDelivery>> Pending(ShopFixture f) =>
        f.Services.GetRequiredService<IDomainEventPersistence>().ReadDueAsync(Subscriber, f.Clock.GetUtcNow(), 100, default);

    [Fact]
    public async Task Online_job_deletes_only_online_expired_items_and_repeat_has_no_notification()
    {
        using var f = new ShopFixture();
        Select(f, 2);
        await Seed(f);
        Select(f, 1);
        await Seed(f);
        await Dfo.Server.Application.Tests.Scheduling.TestScheduledTasks.RunActualAsync(f.Services, "inventory.expiration.online");
        Assert.Equal(0, f.Count(9001));
        Assert.Equal(3, f.Count(9005));
        Assert.Equal(3, f.Count(9006));
        Assert.Single(await Pending(f));
        await Dfo.Server.Application.Tests.Scheduling.TestScheduledTasks.RunActualAsync(f.Services, "inventory.expiration.online");
        Assert.Single(await Pending(f));
        Select(f, 2);
        Assert.Equal(3, f.Count(9001)); // Offline character was not touched.
    }

    [Fact]
    public async Task Online_rollover_removes_old_puppet_before_refreshing_new_mailbox()
    {
        using var f = new ShopFixture(s => s.AddSingleton<IQuestHelperPuppetRuleService>(new PuppetRules()));
        f.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,582,4000000000)");
        var puppets = f.Services.GetRequiredService<IQuestHelperPuppetDomainService>();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = Transactions(f).Begin())
            {
                Assert.True(await puppets.EnsureDailyAsync(new(1), new(1), default));
                await tx.CommitAsync(default);
            }
        });
        var mailbox = f.Services.GetRequiredService<IMailboxDomainService>();
        var old = await TestGameplay.RunAsync(f.Services, async () =>
        {
            var old = Assert.Single(await mailbox.InboxAsync(new(1)));
            await mailbox.ClaimAsync(new(1), old.Attachments[0].Id);
            Assert.Equal(1, f.Count(3257));
            f.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().EnsureInWorld(f.Session);
            return old;
        });
        f.Clock.Advance(TimeSpan.FromDays(1));
        await CleanupSession(f, f.Session);
        Assert.Equal(0, f.Count(3257));
        var current = Assert.Single(await mailbox.InboxAsync(new(1)));
        Assert.NotEqual(old.Id, current.Id);
        Assert.Equal(4, current.Attachments.Length);
        f.Sender.Messages.Clear();
        await f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync(Subscriber);
        var kinds = f.Sender.Messages.Select(x => x.GetType().Name).ToList();
        Assert.Contains("InventoryFullSnapshotMessage", kinds);
        Assert.Contains("MailboxStateMessage", kinds);
        Assert.True(kinds.IndexOf("InventoryFullSnapshotMessage") < kinds.IndexOf("MailboxStateMessage"));
        await CleanupSession(f, f.Session);
        Assert.Equal(current.Id, Assert.Single(await mailbox.InboxAsync(new(1))).Id);
    }

    private sealed class PuppetRules : IQuestHelperPuppetRuleService
    {
        public IReadOnlyList<int> PuppetItemIds => [3257, 3258, 3259, 3260];
    }

    [Fact]
    public async Task Rollback_keeps_item_and_does_not_publish_cleanup()
    {
        using var f = new ShopFixture();
        await Seed(f);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = Transactions(f).Begin())
                Assert.True(await f.Services.GetRequiredService<IOnlineExpiredItemCleanupDomainService>()
                    .CleanupAsync(f.Session, default));
        });
        Assert.Equal(3, f.Count(9001));
        Assert.Empty(await Pending(f));
        Assert.Empty(f.Sender.Messages);
    }

    [Fact]
    public async Task Queued_cleanup_revalidates_published_session_after_account_gate()
    {
        using var f = new ShopFixture();
        await Seed(f);
        // The release and reselection are a CMD of their own that runs before the cleanup CMD.
        TestGameplay.Run(f.Services, () =>
        {
        f.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().ReleaseExact(f.Session);
        f.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().Select(f.Session);
        });
        var pending = CleanupSession(f, f.Session).AsTask();
        // Out of the world means no longer the active character either, so the queued cleanup is
        // refused at the account gate rather than skipped inside the service.
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(3, f.Count(9001));
        Assert.Empty(await Pending(f));
    }

    [Fact]
    public async Task Displaced_session_cannot_delete_or_notify_replacement()
    {
        using var f = new ShopFixture();
        await Seed(f);
        var old = f.Session;
        Select(f, 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CleanupSession(f, old).AsTask());
        Assert.Empty(await Pending(f));
        Select(f, 1);
        Assert.Equal(3, f.Count(9001));
    }

    [Fact]
    public async Task Committed_cleanup_refreshes_current_container_and_never_stale_generation()
    {
        using var f = new ShopFixture();
        await Seed(f);
        await CleanupSession(f, f.Session);
        Assert.Empty(f.Sender.Messages);
        // Another item takes the same slot before the live projection is consumed.
        await f.Seed(121, 9001, 8);
        await f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync(Subscriber);
        var registrations = f.Services.GetServices<ServerMessageEncoderRegistration>().ToDictionary(x => x.MessageType);
        var frames = f.Sender.Messages.SelectMany(message => ((IServerMessageEncoder)f.Services.GetRequiredService(
            registrations[message.GetType()].EncoderType)).Encode(message)).Select(x => x.ToArray());
        var snapshot = Assert.Single(frames,
            x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1, 2)) == 0x000D);
        var entry = Enumerable.Range(0, BinaryPrimitives.ReadUInt16LittleEndian(snapshot.AsSpan(18, 2)))
            .Select(i => snapshot.AsSpan(20 + i * 101, 101).ToArray())
            .Single(x => BinaryPrimitives.ReadInt16LittleEndian(x) == 121);
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(6, 4)));
        f.Sender.Messages.Clear();
        await f.SeedCore(124, StackableItemCore.Create(2, 9001, 1) with { ExpireTime = 1 });
        await CleanupSession(f, f.Session);
        Select(f, 2);
        await f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync(Subscriber);
        Assert.Empty(f.Sender.Messages);
    }

    [Fact]
    public async Task Failure_does_not_skip_next_character_and_remains_retryable()
    {
        using var f = new ShopFixture(services => services.AddSingleton<IOnlineExpiredItemCleanupDomainService>(new ControlledFailures()));
        TestGameplay.Run(f.Services, () =>
        {
            var state = f.Services.GetRequiredService<Dfo.Server.Application.Users.OnlineStateRegistry>();
            var connection = new Dfo.Server.Data.Networking.NetworkConnectionContext(new("second-failed-cleanup"), Dfo.Server.Data.Networking.NetworkEndpointKind.Game, 10011);
            var user = state.RegisterConnection(connection);
            state.Authenticate(user.OnlineUserId, new(2));
            var second = SelectedSessionIdentity.Create(new(2), new(2), connection.ConnectionId, state.GetRequiredUser(user.OnlineUserId).Generation, 10011);
            state.Select(second); state.EnterWorld(second);
        });
        await Assert.ThrowsAsync<AggregateException>(() => Dfo.Server.Application.Tests.Scheduling.TestScheduledTasks.RunActualAsync(f.Services, "inventory.expiration.online").AsTask());
        var cleanup = (ControlledFailures)f.Services.GetRequiredService<IOnlineExpiredItemCleanupDomainService>();
        Assert.Equal(new[] { 1, 2 }, cleanup.Attempts);
        cleanup.Fail = false;
        await Dfo.Server.Application.Tests.Scheduling.TestScheduledTasks.RunActualAsync(f.Services, "inventory.expiration.online");
        Assert.Equal(new[] { 1, 2, 1, 2 }, cleanup.Attempts);
    }

    private sealed class ControlledFailures : IOnlineExpiredItemCleanupDomainService
    {
        internal bool Fail = true;
        internal List<int> Attempts { get; } = [];
        public ValueTask<bool> CleanupAsync(SelectedSessionIdentity session, CancellationToken ct)
        {
            Attempts.Add(session.CharacterId.Value);
            return Fail && session.CharacterId.Value == 1 ? ValueTask.FromException<bool>(new IOException("failure")) : ValueTask.FromResult(true);
        }
    }
}
