using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using System.Buffers.Binary;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging.Abstractions;

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
        var sessions = new Sessions(); var bus = new Bus(); var sender = new Sender();
        bus.Items = [new(7518, 1, null, bus.At.AddMinutes(-2), bus.At.AddSeconds(86280)),
            new(2600026, 1, 1, null, null)];
        await new ItemUsageProjectionDelivery(bus, sessions, sender).SendAsync(sessions.Current!, default);
        Assert.Equal(new ushort[] { 0x021E, 0x021E, 0x00AC }, sender.Types);
        Assert.Equal(2600026, BinaryPrimitives.ReadInt32LittleEndian(sender.Bodies[1].AsSpan(1)));
        Assert.Equal(Convert.FromHexString("015E1D000008510100"), sender.Bodies[2]);
        Assert.Empty(ItemUsageCooldownProjection.Snapshot([new(7518, 1, null, null, bus.At)], bus.At));
        var chunks = ItemUsageCooldownProjection.Snapshot(Enumerable.Range(1, 256)
            .Select(id => new ItemUsageState(id, 1, null, null, bus.At.AddMilliseconds(1))).ToArray(), bus.At);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(255, chunks[0].EncodeBody()[0]);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(chunks[1].EncodeBody().AsSpan(5)));
    }

    [Fact]
    public async Task Daily_worker_only_refreshes_changed_business_day_and_does_not_deliver_to_replaced_session()
    {
        var sessions = new Sessions(); var clock = new Clock(); var bus = new Bus(); var sender = new Sender();
        var delivery = new ItemUsageProjectionDelivery(bus, sessions, sender);
        using var worker = new ItemUsageDailyRefreshService(sessions, sessions, new SelectedSessionTransitionGate(),
            new Days(), clock, delivery, NullLogger<ItemUsageDailyRefreshService>.Instance);
        await worker.RefreshAsync(default); Assert.Equal(2, sender.Bodies.Count);
        await worker.RefreshAsync(default); Assert.Equal(2, sender.Bodies.Count);
        clock.Now = clock.Now.AddMinutes(2); bus.Used = 0;
        await worker.RefreshAsync(default); Assert.Equal(4, sender.Bodies.Count);
        Assert.Equal(0, sender.Bodies[^1][5]);
        var old = sessions.Current!;
        bus.BeforeReturn = () => sessions.Current = old with { ConnectionId = new("replacement") };
        await delivery.SendAsync(old, default); Assert.Equal(4, sender.Bodies.Count);
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
        public SelectedSessionIdentity? Current = SelectedSessionIdentity.Create(new(1), new(2), new("usage"), 1, 10011);
        public IReadOnlyList<SelectedSessionIdentity> Snapshot() => Current is { } s ? [s] : [];
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => Current?.CharacterId == id ? Current : null;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => Current?.ConnectionId == id ? Current : null;
        public SelectedSessionIdentity? Find(AccountId id) => Current?.AccountId == id ? Current : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => Current?.WireUserId == id ? Current : null;
        public SelectedSessionIdentity? Select(SelectedSessionIdentity s) { var old = Current; Current = s; return old; }
        public bool ReleaseExact(SelectedSessionIdentity s) { if (s != Current) return false; Current = null; return true; }
    }
    private sealed class Bus : ICommandBus
    {
        public int Used = 1;
        public DateTimeOffset At = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        public IReadOnlyList<ItemUsageState>? Items;
        public Action? BeforeReturn;
        public ValueTask<T> SendAsync<T>(ICommand<T> command, CancellationToken token = default) where T : notnull
        {
            Assert.IsType<ReadItemUsageSnapshotCommand>(command);
            BeforeReturn?.Invoke();
            return ValueTask.FromResult((T)(object)new ItemUsageSnapshotResult(Items ?? [new(2600026, Used, 1, null, null)], true) { At = At });
        }
    }
    private sealed class Sender : IGameMessageSender
    {
        public List<byte[]> Bodies = [];
        public List<ushort> Types = [];
        public ValueTask SendNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken token)
        {
            var state = Assert.IsAssignableFrom<GameStateSynchronizationMessage>(message);
            Types.Add(state.Type); Bodies.Add(state.EncodeBody()); return ValueTask.CompletedTask;
        }
        public ValueTask SendCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken token) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken token) => throw new NotSupportedException();
    }
}
