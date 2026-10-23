using Dfo.Server.Application.Execution;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
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
    [Theory]
    [InlineData(0, "00000000")]
    [InlineData(1, "00000100")]
    [InlineData(3, "00000300")]
    [InlineData(258, "00000201")]
    public async Task Gold_silver_horn_sends_used_in_the_second_u16_and_keeps_the_unconfigured_gold_slot_zero(int used, string hex)
    {
        var sessions = new Sessions(); var snapshots = new Snapshots();
        snapshots.Value = snapshots.Value with { GoldSilverHorn = new(new(2026, 10, 19), used, 3) };
        var messages = await new UsageQuotaViewChangedHandler(snapshots, sessions)
            .ComposeAsync(new(sessions.Current!.CharacterId, sessions.Current.ConnectionId),
                new(sessions.Current.CharacterId, UsageQuotaFamily.GoldSilverHorn), default);
        var message = Assert.Single(messages.OfType<GoldSilverHornQuotaMessage>());
        Assert.Equal(0x042B, message.Type);
        Assert.Equal(Convert.FromHexString(hex), message.EncodeBody());
        Assert.Throws<OverflowException>(() => new GoldSilverHornQuotaMessage(-1).EncodeBody());
    }

    [Fact]
    public async Task Gold_silver_horn_committed_refresh_updates_used_and_absolute_remaining_caches()
    {
        var sessions = new Sessions(); var snapshots = new Snapshots();
        snapshots.Value = snapshots.Value with { GoldSilverHorn = new(new(2026, 10, 19), 1, 3) };
        var messages = await new UsageQuotaViewChangedHandler(snapshots, sessions).ComposeAsync(
            new(sessions.Current!.CharacterId, sessions.Current.ConnectionId),
            new(sessions.Current.CharacterId, UsageQuotaFamily.GoldSilverHorn), default);
        Assert.Equal(2, messages.Count);
        Assert.Equal(Convert.FromHexString("00000100"), Assert.Single(messages.OfType<GoldSilverHornQuotaMessage>()).EncodeBody());
        var remaining = Assert.Single(messages.OfType<DungeonRemainingEntriesMessage>());
        Assert.Equal(0x0227, remaining.Type);
        Assert.Equal(Convert.FromHexString("1B10000002"), remaining.EncodeBody());
    }

    [Fact]
    public async Task Gold_silver_horn_online_reset_refresh_retries_failures_without_sending_every_tick()
    {
        var sessions = new Sessions(); var clock = new Clock();
        var events = new Events { Family = UsageQuotaFamily.GoldSilverHorn };
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        using var worker = new GoldSilverHornQuotaDailyRefreshService(sessions, sessions,
            new UsageQuotaProjectionDelivery(events), new Daily(), clock,
            NullLogger<GoldSilverHornQuotaDailyRefreshService>.Instance, new GameplayQueue(new GameplayShardSelector(executor)));
        await worker.RefreshAsync(default); await worker.RefreshAsync(default);
        Assert.Single(events.Projections);
        clock.Now = new(2026, 10, 11, 5, 59, 0, TimeSpan.FromHours(8));
        await worker.RefreshAsync(default); Assert.Single(events.Projections);
        clock.Now = clock.Now.AddMinutes(1); events.Outcome = NotificationOutcome.Failed;
        await worker.RefreshAsync(default); Assert.Equal(2, events.Projections.Count);
        events.Outcome = NotificationOutcome.Delivered;
        await worker.RefreshAsync(default); await worker.RefreshAsync(default);
        Assert.Equal(3, events.Projections.Count);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(8)]
    [InlineData(0)]
    public async Task Black_dragon_projects_absolute_remaining_entries_in_existing_NOTI0227(byte remaining)
    {
        var sessions = new Sessions(); var snapshots = new Snapshots();
        snapshots.Value = snapshots.Value with { Tournaments = [new(122, new(2026, 10, 14), 9 - remaining, remaining, 9)] };
        var message = Assert.IsType<DungeonRemainingEntriesMessage>(Assert.Single(await new UsageQuotaViewChangedHandler(snapshots, sessions)
            .ComposeAsync(new(sessions.Current!.CharacterId, sessions.Current.ConnectionId),
                new(sessions.Current.CharacterId, UsageQuotaFamily.TournamentDungeon), default)));
        Assert.Equal(0x0227, message.Type);
        Assert.Equal(new byte[] { 122, 0, 0, 0, remaining }, message.EncodeBody());
    }

    [Fact]
    public async Task Tournament_online_refresh_follows_daily_reset_and_retries_failed_delivery()
    {
        var sessions = new Sessions(); var clock = new Clock();
        var events = new Events { Family = UsageQuotaFamily.TournamentDungeon };
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        using var worker = new Dfo.Server.Protocol.Game.Tournament.TournamentQuotaDailyRefreshService(sessions, sessions,
            new UsageQuotaProjectionDelivery(events), new Daily(), clock,
            NullLogger<Dfo.Server.Protocol.Game.Tournament.TournamentQuotaDailyRefreshService>.Instance, new GameplayQueue(new GameplayShardSelector(executor)));
        await worker.RefreshAsync(default); await worker.RefreshAsync(default);
        Assert.Single(events.Projections);
        clock.Now = new(2026, 10, 11, 5, 59, 0, TimeSpan.FromHours(8));
        await worker.RefreshAsync(default); Assert.Single(events.Projections);
        clock.Now = clock.Now.AddMinutes(1); events.Outcome = NotificationOutcome.Failed;
        await worker.RefreshAsync(default); Assert.Equal(2, events.Projections.Count);
        events.Outcome = NotificationOutcome.Delivered;
        await worker.RefreshAsync(default); await worker.RefreshAsync(default);
        Assert.Equal(3, events.Projections.Count);
    }

    private sealed class Daily : Dfo.Server.Abstractions.Configuration.IDailyResetRuleService
    {
        public DateTimeOffset PeriodStart(DateTimeOffset now)
        {
            var local = now.ToOffset(TimeSpan.FromHours(8));
            var start = new DateTimeOffset(local.Date.AddHours(6), local.Offset);
            return local < start ? start.AddDays(-1) : start;
        }
        public bool IsCurrentPeriod(DateTimeOffset recordedAt, DateTimeOffset now) => recordedAt >= PeriodStart(now);
    }
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
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        using var worker = new MirrorQuotaDailyRefreshService(sessions, sessions,
            new UsageQuotaProjectionDelivery(events), clock, NullLogger<MirrorQuotaDailyRefreshService>.Instance, new GameplayQueue(new GameplayShardSelector(executor)));
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
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

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
        public UsageQuotaFamily Family = UsageQuotaFamily.MirrorDungeon;
        public List<NotificationAudience> Projections = [];
        public NotificationOutcome Outcome = NotificationOutcome.Delivered;
        public ValueTask<Guid> EnqueueAsync<T>(T payload, CancellationToken cancellationToken = default) where T : class, IDomainEvent => throw new NotSupportedException();
        public ValueTask DirectAsync<T>(T payload, CancellationToken cancellationToken = default) where T : class, IDomainEvent => throw new NotSupportedException();
        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notification, CancellationToken cancellationToken = default)
            where T : class, INotification<T>
        {
            Projections.Add(audience); var view = Assert.IsType<UsageQuotaViewChanged>(notification);
            Assert.Equal(Family, view.Family);
            return ValueTask.FromResult(new NotificationDelivery([new(view.CharacterId, Outcome)]));
        }
    }
}
