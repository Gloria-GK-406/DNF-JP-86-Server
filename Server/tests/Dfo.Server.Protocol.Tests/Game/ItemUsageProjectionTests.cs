using Dfo.Server.Protocol.Tests.Support;
using Dfo.Server.Application.Execution;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using System.Buffers.Binary;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game;
public sealed class ItemUsageProjectionTests
{
    [Fact]
    public void Full_snapshot_clears_stale_character_counts_chunks_without_truncating_and_serializes_used_count()
    {
        var sample = new ItemUsageCountMessage([new(2600026, 1, 1, null, null)]);
        Assert.Equal(Convert.FromHexString("015AAC270001"), sample.EncodeBody());
        Assert.Equal(new byte[] { 0 }, Assert.Single(ItemUsageCountMessage.FullSnapshot([])).EncodeBody());
        var chunks = ItemUsageCountMessage.FullSnapshot(Enumerable.Range(1, 256).Select(i => new ItemUsageState(i, 1, 3, null, null)).ToArray());
        Assert.Equal(3, chunks.Count);
        Assert.Equal(255, chunks[1].EncodeBody()[0]); Assert.Equal(1, chunks[2].EncodeBody()[0]);
        Assert.Throws<OverflowException>(() => ItemUsageCountMessage.FullSnapshot([new(1, 256, 300, null, null)]));
    }
    [Fact]
    public async Task Login_delivery_restores_unlimited_cooldown_in_seconds_without_creating_a_daily_quota()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new Sessions(); var bus = new Bus(); var sender = new Sender();
            bus.Items = [new(7518, 1, null, bus.At.AddMinutes(-2), bus.At.AddSeconds(86280)),
                new(2600026, 1, 1, null, null)];
            await new ItemUsageProjectionFixture(bus, sessions, sender).SendAsync(sessions.Current!, default);
            Assert.Equal(new ushort[] { 0x021E, 0x021E, 0x00AC }, sender.Types);
            Assert.Equal(2600026, BinaryPrimitives.ReadInt32LittleEndian(sender.Bodies[1].AsSpan(1)));
            Assert.Equal(Convert.FromHexString("015E1D000008510100"), sender.Bodies[2]);
            Assert.Empty(ItemUsageCooldownProjection.Snapshot([new(7518, 1, null, null, bus.At)], bus.At));
            var chunks = ItemUsageCooldownProjection.Snapshot(Enumerable.Range(1, 256)
                .Select(id => new ItemUsageState(id, 1, null, null, bus.At.AddMilliseconds(1))).ToArray(), bus.At);
            Assert.Equal(2, chunks.Count);
            Assert.Equal(255, chunks[0].EncodeBody()[0]);
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(chunks[1].EncodeBody().AsSpan(5)));
        });
    }

    [Fact]
    public async Task Daily_worker_only_refreshes_changed_business_day_and_does_not_deliver_to_replaced_session()
    {
        var sessions = new Sessions(); var clock = new Clock(); var bus = new Bus(); var sender = new Sender();
        var delivery = new ItemUsageProjectionFixture(bus, sessions, sender);
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        using var worker = new TimerRefreshFixture(SelectedSessionRefreshTopic.ItemUsage, sessions, sessions, null,
            new Days(), clock, new GameplayQueue(new GameplayShardSelector(executor)), bus, sender);
        await worker.RefreshAsync(default); Assert.Equal(2, sender.Bodies.Count);
        await worker.RefreshAsync(default); Assert.Equal(2, sender.Bodies.Count);
        clock.Now = clock.Now.AddMinutes(2); bus.Used = 0;
        await worker.RefreshAsync(default); Assert.Equal(4, sender.Bodies.Count);
        Assert.Equal(0, sender.Bodies[^1][5]);
        var old = sessions.Current!;
        bus.BeforeReturn = () => sessions.Current = old with { ConnectionId = new("replacement") };
        await TestGameplay.RunAsync(async () =>
        {
            await delivery.SendAsync(old, default); Assert.Equal(4, sender.Bodies.Count);
        });
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 29, 5, 59, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Days : IDailyResetRuleService
    {
        public DateTimeOffset PeriodStart(DateTimeOffset now) => new DateTimeOffset(now.AddHours(-6).Date, now.Offset).AddHours(6);
        public bool IsCurrentPeriod(DateTimeOffset a, DateTimeOffset b) => PeriodStart(a) == PeriodStart(b);
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

        public SelectedSessionIdentity? Current = SelectedSessionIdentity.Create(new(1), new(2), new("usage"), 1, 10011);
        public IReadOnlyList<SelectedSessionIdentity> Snapshot() => Current is { } s ? [s] : [];
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => Current?.CharacterId == id ? Current : null;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => Current?.ConnectionId == id ? Current : null;
        public SelectedSessionIdentity? Find(AccountId id) => Current?.AccountId == id ? Current : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => Current?.WireUserId == id ? Current : null;
        public SelectedSessionIdentity? Select(SelectedSessionIdentity s) { var old = Current; Current = s; return old; }
        public bool ReleaseExact(SelectedSessionIdentity s) { if (s != Current) return false; Current = null; return true; }
    }
    private sealed class Bus : IItemUsageDomainService
    {
        public int Used = 1;
        public DateTimeOffset At = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        public IReadOnlyList<ItemUsageState>? Items;
        public Action? BeforeReturn;
        public ValueTask<IReadOnlyList<ItemUsageState>> GetSnapshotAtAsync(CharacterId id, DateTimeOffset at, CancellationToken token)
        { BeforeReturn?.Invoke(); return ValueTask.FromResult(Items ?? (IReadOnlyList<ItemUsageState>)[new(2600026, Used, 1, null, null)]); }
        public ValueTask<IReadOnlyList<ItemUsageState>> GetSnapshotAsync(CharacterId id, CancellationToken token) => GetSnapshotAtAsync(id, At, token);
        public ValueTask<ItemUsageState> GetAsync(CharacterId id, int item, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ItemUsageState> RecordUseAsync(CharacterId id, int item, CancellationToken token) => throw new NotSupportedException();
    }
    // Direct domain projection oracle; production daily scopes and FIFO use the registered source tests.
    private sealed class ItemUsageProjectionFixture(Bus usage, Sessions sessions, IGameMessageSender sender)
    {
        public async ValueTask SendAsync(SelectedSessionIdentity session, CancellationToken token)
        {
            var snapshot = await ItemUsageSnapshotReader.ReadAsync(usage, sessions, new FixedClock(usage.At), session, token);
            if (!snapshot.IsCurrent || !((ISelectedSessionDomainService)sessions).IsInWorld(session)) return;
            foreach (var message in ItemUsageCountMessage.FullSnapshot(snapshot.Items).Cast<IGameNotiMessage>().Concat(ItemUsageCooldownProjection.Snapshot(snapshot.Items, snapshot.At)))
            { if (!((ISelectedSessionDomainService)sessions).IsInWorld(session)) return; await sender.SendNotiAsync(session.ConnectionId, message, token); }
        }
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    /// <summary>
    /// The state messages a real registration adapter registered, read from its outbox; every call goes to the real
    /// protocol sender over it (TASK-25). A CMD or an explicit delivery fails where it is registered, as the old double
    /// refused one.
    /// </summary>
    private sealed class Sender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();
        public Sender()
        {
            outbound.RefuseExplicit();
            outbound.FailAny(message => outbound.ExplicitInFlight is null && message is IGameCmdMessage, () => new NotSupportedException());
        }
        public List<byte[]> Bodies => [.. States.Select(state => state.EncodeBody())];
        public List<ushort> Types => [.. States.Select(state => state.Type)];
        private IEnumerable<GameStateSynchronizationMessage> States =>
            outbound.Messages.Select(message => Assert.IsAssignableFrom<GameStateSynchronizationMessage>(message));
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }
}
