using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
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
        if (!sessions.TryGetInWorld(recipient.CharacterId, out var session) || session.ConnectionId != recipient.ConnectionId
            || recipient.ExpectedSession is { } expected && expected != session) return [];
        var snapshot = await snapshots.ReadAsync(session.AccountId, session.CharacterId,
            notification.Family, cancellationToken).ConfigureAwait(false);
        // Snapshot composition can await storage. Never route the result to a replacement generation.
        if (!sessions.IsInWorld(session)) return [];
        return notification.Family switch
        {
            UsageQuotaFamily.GiantDragonCrystalExchange => [],
            UsageQuotaFamily.LostLand => [new DungeonRemainingEntriesMessage(4000, checked((byte)Math.Min(byte.MaxValue,
                (snapshot.LostLand ?? throw new InvalidDataException("Missing LostLand quota snapshot.")).Remaining)))],
            UsageQuotaFamily.ReviveCoin => [new DailyReviveCoinStateMessage(snapshot.ReviveCoinClaimed
                ?? throw new InvalidDataException("Missing revive coin quota snapshot."))],
            UsageQuotaFamily.GoldSilverHorn => [new GoldSilverHornQuotaMessage((snapshot.GoldSilverHorn
                ?? throw new InvalidDataException("Missing gold/silver horn quota snapshot.")).Used),
                new DungeonRemainingEntriesMessage(snapshot.GoldSilverHorn.DungeonId, checked((byte)snapshot.GoldSilverHorn.Remaining))],
            UsageQuotaFamily.HerasDungeon => [snapshot.Heras is { } heras
                ? new DungeonRemainingEntriesMessage(heras.DungeonId, checked((byte)heras.Remaining))
                : throw new InvalidDataException("Missing Heras quota snapshot.")],
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
        };
    }
}
