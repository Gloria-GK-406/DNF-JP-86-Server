using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

/// <summary>A21 NOTI 0227 sets an absolute remaining count for one dungeon.</summary>
internal sealed record DungeonRemainingEntriesMessage(int DungeonId, byte Remaining)
    : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0227;
    internal override byte[] EncodeBody()
    {
        var body = new byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(body, checked((uint)DungeonId));
        body[4] = Remaining;
        return body;
    }
}

/// <summary>A21 NOTI 0279: the first u32 remains unknown; the second is entries used, not remaining.</summary>
internal sealed record MirrorDungeonQuotaMessage(int EntriesUsed) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0279;
    internal override byte[] EncodeBody()
    {
        var body = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), checked((uint)EntriesUsed));
        return body;
    }
}

internal sealed class UsageQuotaViewChangedHandler(IUsageQuotaSnapshotDomainService snapshots,
    ISelectedSessionDomainService sessions) : INotificationHandler<UsageQuotaViewChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        UsageQuotaViewChanged notification, CancellationToken cancellationToken)
    {
        if (recipient.CharacterId != notification.CharacterId) return [];
        var session = sessions.FindByCharacter(recipient.CharacterId);
        if (session is null || session.ConnectionId != recipient.ConnectionId
            || recipient.ExpectedSession is { } expected && expected != session) return [];
        var snapshot = await snapshots.ReadAsync(session.AccountId, session.CharacterId,
            notification.Family, cancellationToken).ConfigureAwait(false);
        // Snapshot composition can await storage. Never route the result to a replacement generation.
        if (sessions.FindByCharacter(session.CharacterId) != session
            || sessions.FindByConnection(session.ConnectionId) != session) return [];
        return notification.Family switch
        {
            UsageQuotaFamily.GoldSilverHorn => [new GoldSilverHornQuotaMessage((snapshot.GoldSilverHorn
                ?? throw new InvalidDataException("Missing gold/silver horn quota snapshot.")).Used),
                new DungeonRemainingEntriesMessage(4123, checked((byte)snapshot.GoldSilverHorn.Remaining))],
            UsageQuotaFamily.HerasDungeon => [new DungeonRemainingEntriesMessage(3900, checked((byte)(snapshot.Heras
                ?? throw new InvalidDataException("Missing Heras quota snapshot.")).Remaining))],
            UsageQuotaFamily.TournamentDungeon => (snapshot.Tournaments
                ?? throw new InvalidDataException("Missing tournament quota snapshot.")).Select(x =>
                    (IServerNetworkMessage)new DungeonRemainingEntriesMessage(x.DungeonId, checked((byte)x.Remaining))).ToArray(),
            UsageQuotaFamily.AltarDungeon => (snapshot.Altars
                ?? throw new InvalidDataException("Missing altar quota snapshot.")).Select(x =>
                    (IServerNetworkMessage)new DungeonRemainingEntriesMessage(x.DungeonId, checked((byte)x.Remaining))).ToArray(),
            UsageQuotaFamily.DespairTower => (snapshot.Despair
                ?? throw new InvalidDataException("Missing despair quota snapshot.")).DungeonIds
                .Select(id => (IServerNetworkMessage)new DungeonRemainingEntriesMessage(id, snapshot.Despair!.FreeEntriesRemaining)).ToArray(),
            UsageQuotaFamily.DimensionDungeon => [new DimensionDungeonQuotaMessage(
                snapshot.Dimension ?? throw new InvalidDataException("Missing dimension quota snapshot."))],
            UsageQuotaFamily.Consumable => ItemUsageCountMessage.FullSnapshot(snapshot.Items)
                .Cast<IServerNetworkMessage>().Concat(ItemUsageCooldownProjection.Snapshot(snapshot.Items, snapshot.At)).ToArray(),
            UsageQuotaFamily.Premium => [new PremiumServiceStateMessage(
                snapshot.Entitlements ?? throw new InvalidDataException("Missing premium quota snapshot."), snapshot.PremiumUsage, snapshot.At)],
            UsageQuotaFamily.MirrorDungeon => [new MirrorDungeonQuotaMessage(
                (snapshot.Mirror ?? throw new InvalidDataException("Missing mirror quota snapshot.")).Entries)],
            _ => throw new ArgumentOutOfRangeException(nameof(notification)),
        };
    }
}

/// <summary>Read and send through EventService without reentering CommandBus or its transaction gate.</summary>
public sealed class UsageQuotaProjectionDelivery(IEventService events)
{
    public ValueTask<NotificationDelivery> SendAsync(SelectedSessionIdentity session, UsageQuotaFamily family,
        CancellationToken cancellationToken) => events.ProjectAsync(new NotificationAudience.TheseSessions([session]),
            new UsageQuotaViewChanged(session.CharacterId, family), cancellationToken);
}

// Mirror's original quota resets at local midnight, independently of the 06:00 item/premium period.
public sealed class MirrorQuotaDailyRefreshService(ISelectedSessionSnapshotQuery directory,
    ISelectedSessionDomainService sessions, SelectedSessionTransitionGate transitions,
    UsageQuotaProjectionDelivery delivery, TimeProvider clock, ILogger<MirrorQuotaDailyRefreshService> logger) : BackgroundService
{
    private readonly Dictionary<SelectedSessionIdentity, DateOnly> delivered = [];
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var current = directory.Snapshot();
        foreach (var stale in delivered.Keys.Except(current).ToArray()) delivered.Remove(stale);
        foreach (var session in current)
        {
            var day = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime);
            if (delivered.TryGetValue(session, out var previous) && previous == day) continue;
            try
            {
                await using var lease = await transitions.AcquireAsync(session.AccountId, UserAccessMode.Exclusive,
                    cancellationToken).ConfigureAwait(false);
                if (sessions.FindByCharacter(session.CharacterId) != session) continue;
                var result = await delivery.SendAsync(session, UsageQuotaFamily.MirrorDungeon, cancellationToken).ConfigureAwait(false);
                if (sessions.FindByCharacter(session.CharacterId) == session
                    && result.Attempts.Any(x => x.CharacterId == session.CharacterId && x.Outcome == NotificationOutcome.Delivered))
                    delivered[session] = day;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) { logger.LogWarning(error, "Mirror quota projection failed character={CharacterId}", session.CharacterId); }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        try { while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) await RefreshAsync(stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
