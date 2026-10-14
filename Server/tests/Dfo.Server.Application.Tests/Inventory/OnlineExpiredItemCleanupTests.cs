using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class OnlineExpiredItemCleanupTests
{
    private static void Select(ShopFixture f, int character)
    {
        f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(new(character));
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
        f.Services.GetRequiredService<ISelectedSessionRuntimeDirectory>().Publish(f.Session, false);
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
        await f.Services.GetRequiredService<OnlineExpiredItemCleanupJob>().RunAsync(default);
        Assert.Equal(0, f.Count(9001));
        Assert.Equal(3, f.Count(9005));
        Assert.Equal(3, f.Count(9006));
        Assert.Single(await Pending(f));
        await f.Services.GetRequiredService<OnlineExpiredItemCleanupJob>().RunAsync(default);
        Assert.Single(await Pending(f));
        Select(f, 2);
        Assert.Equal(3, f.Count(9001)); // Offline character was not touched.
    }

    [Fact]
    public async Task Rollback_keeps_item_and_does_not_publish_cleanup()
    {
        using var f = new ShopFixture();
        await Seed(f);
        await using (var tx = Transactions(f).Begin())
            Assert.True(await f.Services.GetRequiredService<IOnlineExpiredItemCleanupDomainService>()
                .CleanupAsync(f.Session, default));
        Assert.Equal(3, f.Count(9001));
        Assert.Empty(await Pending(f));
        Assert.Empty(f.Sender.Messages);
    }

    [Fact]
    public async Task Queued_cleanup_revalidates_published_session_after_account_gate()
    {
        using var f = new ShopFixture();
        await Seed(f);
        var gate = f.Services.GetRequiredService<AccountExecutionGate>();
        var lease = await gate.AcquireAsync(f.Session.AccountId, UserAccessMode.Exclusive, default);
        var pending = f.Services.GetRequiredService<ICommandBus>().SendAsync(new CleanupOnlineExpiredItemsCommand(f.Session)).AsTask();
        Assert.False(pending.IsCompleted);
        f.Services.GetRequiredService<ISelectedSessionRuntimeDirectory>().RemoveExact(f.Session);
        await lease.DisposeAsync();
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Services.GetRequiredService<ICommandBus>()
            .SendAsync(new CleanupOnlineExpiredItemsCommand(old)).AsTask());
        Assert.Empty(await Pending(f));
        Select(f, 1);
        Assert.Equal(3, f.Count(9001));
    }

    [Fact]
    public async Task Committed_cleanup_refreshes_current_container_and_never_stale_generation()
    {
        using var f = new ShopFixture();
        await Seed(f);
        await f.Services.GetRequiredService<ICommandBus>().SendAsync(new CleanupOnlineExpiredItemsCommand(f.Session));
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
        await f.Services.GetRequiredService<ICommandBus>().SendAsync(new CleanupOnlineExpiredItemsCommand(f.Session));
        Select(f, 2);
        await f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync(Subscriber);
        Assert.Empty(f.Sender.Messages);
    }

    [Fact]
    public async Task Failure_does_not_skip_next_character_and_remains_retryable()
    {
        var sessions = new SelectedSessionDomainService();
        sessions.Select(SelectedSessionIdentity.Create(new(1), new(1), new("one"), 1, 10011));
        sessions.Select(SelectedSessionIdentity.Create(new(2), new(2), new("two"), 1, 10011));
        var commands = new FailingCommands();
        var job = new OnlineExpiredItemCleanupJob(sessions, commands, sessions, NullLogger<OnlineExpiredItemCleanupJob>.Instance);
        await Assert.ThrowsAsync<AggregateException>(() => job.RunAsync(default).AsTask());
        Assert.Equal(new[] { 1, 2 }, commands.Attempts);
        commands.Fail = false;
        await job.RunAsync(default);
        Assert.Equal(new[] { 1, 2, 1, 2 }, commands.Attempts);
    }
    private sealed class FailingCommands : ICommandBus
    {
        public bool Fail { get; set; } = true;
        public List<int> Attempts { get; } = [];
        public ValueTask<T> SendAsync<T>(ICommand<T> command, CancellationToken ct = default) where T : notnull
        {
            var id = ((IAccountCommand)command).AccountId.Value;
            Attempts.Add(id);
            return id == 1 && Fail ? ValueTask.FromException<T>(new IOException("failure")) : ValueTask.FromResult((T)(object)true);
        }
    }
}
