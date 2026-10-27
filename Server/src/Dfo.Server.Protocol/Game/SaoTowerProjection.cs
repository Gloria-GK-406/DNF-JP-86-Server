using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

internal sealed record SaoCurrentFloorNotification(SaoTowerProgress Progress) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSaoCurrentFloor;
}
internal sealed class SaoCurrentFloorEncoder : GameNotiMessageEncoder<SaoCurrentFloorNotification>
{
    internal static byte[] Body(SaoTowerProgress progress)
    {
        if (progress.Round < 1 || progress.NextFloor is < 1 or > 100) throw new InvalidDataException("Invalid SAO status.");
        var body = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(body, checked((uint)progress.Round));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), checked((uint)progress.NextFloor));
        return body;
    }
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SaoCurrentFloorNotification message) =>
        OnePayload(0x03E9, Body(message.Progress));
}

internal sealed record SaoTowerRefresh(Guid RunId, ImmutableArray<SaoTowerRewardRefresh> Rewards, DateTimeOffset At) : INotification<SaoTowerRefresh>;

internal sealed class SaoTowerNotificationSubscriber(IEventService events) : IDomainEventSubscriber<SaoTowerCleared>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<SaoTowerCleared> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters(message.Payload.Rewards.Select(x => x.CharacterId).ToArray()),
            new SaoTowerRefresh(message.Payload.RunId, message.Payload.Rewards, message.OccurredAt), ct).ConfigureAwait(false);
}

internal sealed class SaoTowerRefreshHandler(ISaoTowerDomainService sao, ISelectedSessionDomainService sessions,
    IDungeonRunDomainService runs, ICharacterInventoryEntryDomainService inventories,
    IAccountInventoryEntryDomainService accounts, IInventorySlotProjectionDomainService slots)
    : INotificationHandler<SaoTowerRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        SaoTowerRefresh notification, CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(recipient.ConnectionId, out var session) || session.CharacterId != recipient.CharacterId
            || recipient.ExpectedSession is { } expected && expected != session) return [];
        var reward = notification.Rewards.SingleOrDefault(x => x.CharacterId == session.CharacterId);
        if (reward is null) return [];
        var result = new List<IServerNetworkMessage>();
        var currentRun = runs.Find(session.CharacterId)?.RunId.Value;
        // Inventory is committed character state. Moving on must not discard its refresh.
        // A floor notification, however, must not replace the floor of a different live run.
        if ((currentRun is null || currentRun == notification.RunId)
            && await sao.ReadStatusAsync(ct).ConfigureAwait(false) is { } progress)
            result.Add(new SaoCurrentFloorNotification(progress));
        if (reward.Slots.Length > 0)
        {
            var inventory = await inventories.ReadCurrentAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            var account = await accounts.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            foreach (var space in reward.Slots.Select(x => slots.Project(x).Space).Distinct())
                result.Add(new InventoryFullSnapshotMessage(inventory.Containers.Single(x => x.ContainerType == space),
                    inventory, account, notification.At));
        }
        if (runs.Find(session.CharacterId)?.RunId.Value != currentRun)
            result.RemoveAll(x => x is SaoCurrentFloorNotification);
        return sessions.IsInWorld(session) ? result : [];
    }
}
