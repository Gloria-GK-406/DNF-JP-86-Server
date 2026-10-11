using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class UsageQuotaProjectionTests
{
    [Fact]
    public async Task Despair_refresh_updates_each_floor_with_remaining_free_entries()
    {
        var sessions = new Sessions(); var snapshots = new Snapshots();
        snapshots.Value = snapshots.Value with { Despair = new(3, 0, Enumerable.Range(11008, 100).ToArray()) };
        var messages = await new UsageQuotaViewChangedHandler(snapshots, sessions).ComposeAsync(
            new(sessions.Current!.CharacterId, sessions.Current.ConnectionId),
            new(sessions.Current.CharacterId, UsageQuotaFamily.DespairTower), default);
        Assert.Equal(100, messages.Count);
        foreach (var (value, index) in messages.Select((value, index) => (value, index)))
        {
            var message = Assert.IsType<DungeonRemainingEntriesMessage>(value);
            Assert.IsAssignableFrom<IGameNotiMessage>(message);
            Assert.Equal(0x0227, message.Type);
            var body = message.EncodeBody();
            Assert.Equal(5, body.Length);
            Assert.Equal((uint)(11008 + index), BinaryPrimitives.ReadUInt32LittleEndian(body));
            Assert.Equal(0, body[4]);
        }
        Assert.Equal(Convert.FromHexString("002B000001"), new DungeonRemainingEntriesMessage(11008, 1).EncodeBody());
    }

    [Fact]
    public async Task Dimension_refresh_sends_remaining_and_extra_counts_as_NOTI_0326()
    {
        var sessions = new Sessions(); var snapshots = new Snapshots();
        snapshots.Value = snapshots.Value with { Dimension = new(new(2026, 10, 10), 2, 3, 1) };
        var message = Assert.IsType<DimensionDungeonQuotaMessage>(Assert.Single(await new UsageQuotaViewChangedHandler(snapshots, sessions)
            .ComposeAsync(new(sessions.Current!.CharacterId, sessions.Current.ConnectionId),
                new(sessions.Current.CharacterId, UsageQuotaFamily.DimensionDungeon), default)));
        Assert.IsAssignableFrom<IGameNotiMessage>(message);
        Assert.Equal(0x0326, message.Type);
        Assert.Equal(Convert.FromHexString("0300000001000000"), message.EncodeBody());
    }

    [Fact]
    public async Task Mirror_refresh_reads_latest_authority_and_never_projects_clears_into_the_unknown_field()
    {
        var sessions = new Sessions(); var snapshots = new Snapshots();
        var handler = new UsageQuotaViewChangedHandler(snapshots, sessions);
        var recipient = new NotificationRecipient(sessions.Current!.CharacterId, sessions.Current.ConnectionId);
        var notice = new UsageQuotaViewChanged(recipient.CharacterId, UsageQuotaFamily.MirrorDungeon);
        var first = Assert.IsType<MirrorDungeonQuotaMessage>(Assert.Single(await handler.ComposeAsync(recipient, notice, default)));
        Assert.Equal(Convert.FromHexString("0000000006000000"), first.EncodeBody());
        Assert.IsAssignableFrom<IGameNotiMessage>(first);
        Assert.Equal(0x0279, first.Type);
        snapshots.Value = snapshots.Value with { Mirror = new(7, 2, true) };
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(Assert.IsType<MirrorDungeonQuotaMessage>(
            Assert.Single(await handler.ComposeAsync(recipient, notice, default))).EncodeBody().AsSpan(4)));
        Assert.Throws<OverflowException>(() => new MirrorDungeonQuotaMessage(-1).EncodeBody());
    }

    [Fact]
    public async Task Consumable_refresh_clears_counts_then_restores_counts_and_active_cooldowns_without_truncating()
    {
        var sessions = new Sessions(); var snapshots = new Snapshots();
        snapshots.Value = snapshots.Value with { Items = [new(2600026, 1, 1, null, null),
            new(7518, 1, null, null, snapshots.Value.At.AddSeconds(30))] };
        var handler = new UsageQuotaViewChangedHandler(snapshots, sessions);
        var recipient = new NotificationRecipient(sessions.Current!.CharacterId, sessions.Current.ConnectionId);
        var notice = new UsageQuotaViewChanged(recipient.CharacterId, UsageQuotaFamily.Consumable);
        var messages = (await handler.ComposeAsync(recipient, notice, default)).Cast<GameStateSynchronizationMessage>().ToArray();
        Assert.Equal(new ushort[] { 0x021E, 0x021E, 0x00AC }, messages.Select(x => x.Type));
        Assert.Equal(new byte[] { 0 }, messages[0].EncodeBody());
        Assert.Equal(Convert.FromHexString("015AAC270001"), messages[1].EncodeBody());
        Assert.Equal(30, BinaryPrimitives.ReadInt32LittleEndian(messages[2].EncodeBody().AsSpan(5)));
        snapshots.Value = snapshots.Value with { Items = [new(1, 256, 300, null, null)] };
        await Assert.ThrowsAsync<OverflowException>(() => handler.ComposeAsync(recipient, notice, default).AsTask());
    }

    [Fact]
    public async Task Premium_refresh_is_the_existing_full_CMD_snapshot()
    {
        var sessions = new Sessions(); var snapshots = new Snapshots();
        snapshots.Value = snapshots.Value with { Entitlements = new([]), PremiumUsage = [new(0, 3)] };
        var message = Assert.IsType<PremiumServiceStateMessage>(Assert.Single(await new UsageQuotaViewChangedHandler(snapshots, sessions)
            .ComposeAsync(new(sessions.Current!.CharacterId, sessions.Current.ConnectionId),
                new(sessions.Current.CharacterId, UsageQuotaFamily.Premium), default)));
        Assert.IsAssignableFrom<IGameCmdMessage>(message);
        Assert.Equal(0x036F, message.Type);
        Assert.Equal(77, message.EncodeBody().Length);
    }

    [Fact]
    public async Task Offline_wrong_recipient_and_replaced_during_read_never_receive_stale_snapshots()
    {
        var sessions = new Sessions(); var original = sessions.Current!; var snapshots = new Snapshots();
        var handler = new UsageQuotaViewChangedHandler(snapshots, sessions);
        var notice = new UsageQuotaViewChanged(original.CharacterId, UsageQuotaFamily.MirrorDungeon);
        Assert.Empty(await handler.ComposeAsync(new(new(999), original.ConnectionId), notice, default));
        Assert.Equal(0, snapshots.Reads);
        Assert.Empty(await handler.ComposeAsync(new(original.CharacterId, original.ConnectionId,
            original with { ConnectionGeneration = 2 }), notice, default));
        Assert.Equal(0, snapshots.Reads);
        snapshots.BeforeReturn = () => sessions.Current = original with { ConnectionGeneration = 2 };
        Assert.Empty(await handler.ComposeAsync(new(original.CharacterId, original.ConnectionId, original), notice, default));
        Assert.Equal(1, snapshots.Reads);
        sessions.Current = null;
        Assert.Empty(await handler.ComposeAsync(new(original.CharacterId, original.ConnectionId), notice, default));
        Assert.Equal(1, snapshots.Reads);
    }

    [Fact]
    public async Task Mirror_online_worker_refreshes_at_local_midnight_and_retries_failed_delivery()
    {
        var sessions = new Sessions(); var clock = new Clock(); var events = new Events();
        using var worker = new MirrorQuotaDailyRefreshService(sessions, sessions, new SelectedSessionTransitionGate(),
            new UsageQuotaProjectionDelivery(events), clock, NullLogger<MirrorQuotaDailyRefreshService>.Instance);
        await worker.RefreshAsync(default); await worker.RefreshAsync(default);
        Assert.Single(events.Projections);
        var exact = Assert.IsType<NotificationAudience.TheseSessions>(events.Projections[0]);
        Assert.Equal(sessions.Current, Assert.Single(exact.Sessions));
        clock.Now = clock.Now.AddMinutes(2);
        events.Outcome = NotificationOutcome.Failed;
        await worker.RefreshAsync(default); Assert.Equal(2, events.Projections.Count);
        events.Outcome = NotificationOutcome.Delivered;
        await worker.RefreshAsync(default); await worker.RefreshAsync(default);
        Assert.Equal(3, events.Projections.Count);
        sessions.Current = sessions.Current! with { ConnectionGeneration = 2 };
        await worker.RefreshAsync(default); Assert.Equal(4, events.Projections.Count);
        sessions.Current = null;
        await worker.RefreshAsync(default); Assert.Equal(4, events.Projections.Count);
    }

    private sealed class Snapshots : IUsageQuotaSnapshotDomainService
    {
        public UsageQuotaSnapshot Value = new(new(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(8)), [], null, [], new(6, 1, true));
        public int Reads;
        public Action? BeforeReturn;
        public ValueTask<UsageQuotaSnapshot> ReadAsync(AccountId accountId, CharacterId characterId,
            UsageQuotaFamily family, CancellationToken cancellationToken)
        { Reads++; BeforeReturn?.Invoke(); return ValueTask.FromResult(Value); }
    }
    private sealed class Sessions : ISelectedSessionDomainService, ISelectedSessionSnapshotQuery
    {
        public SelectedSessionIdentity? Current = SelectedSessionIdentity.Create(new(1), new(2), new("quota"), 1, 10011);
        public IReadOnlyList<SelectedSessionIdentity> Snapshot() => Current is { } s ? [s] : [];
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => Current?.CharacterId == id ? Current : null;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => Current?.ConnectionId == id ? Current : null;
        public SelectedSessionIdentity? Find(AccountId id) => Current?.AccountId == id ? Current : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => Current?.WireUserId == id ? Current : null;
        public SelectedSessionIdentity? Select(SelectedSessionIdentity s) { var old = Current; Current = s; return old; }
        public bool ReleaseExact(SelectedSessionIdentity s) { if (s != Current) return false; Current = null; return true; }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 10, 23, 59, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Events : IEventService
    {
        public List<NotificationAudience> Projections = [];
        public NotificationOutcome Outcome = NotificationOutcome.Delivered;
        public ValueTask<Guid> EnqueueAsync<T>(T payload, CancellationToken cancellationToken = default) where T : class, IDomainEvent => throw new NotSupportedException();
        public ValueTask DirectAsync<T>(T payload, CancellationToken cancellationToken = default) where T : class, IDomainEvent => throw new NotSupportedException();
        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notification, CancellationToken cancellationToken = default)
            where T : class, INotification<T>
        {
            Projections.Add(audience); var view = Assert.IsType<UsageQuotaViewChanged>(notification);
            Assert.Equal(UsageQuotaFamily.MirrorDungeon, view.Family);
            return ValueTask.FromResult(new NotificationDelivery([new(view.CharacterId, Outcome)]));
        }
    }
}
