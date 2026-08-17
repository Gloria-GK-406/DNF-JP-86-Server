using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DungeonSelection;

/// <summary>
/// CMD 0x000F <c>ENTER_SELECT_DUNGEON</c>: the client opening the dungeon-selection screen once a
/// town has finished loading.
/// </summary>
/// <remarks>
/// Observed 2026-08-17 with a four-byte body carrying 10000. The client registers a CMD receive
/// handler for this type (RVA 0x008CFF30), so a reply is expected -- but walking that handler
/// recovers a single <c>u8</c> before the trail leaves the scannable text section, which is not
/// enough to construct one. See protocol/game/client-to-server/0x000F.md.
/// </remarks>
public sealed record EnterSelectDungeonRequest(
    GameMessageMetadata Metadata,
    uint Selection) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameEnterSelectDungeon;
}

internal sealed class EnterSelectDungeonDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < sizeof(uint))
        {
            throw new InvalidDataException(
                $"ENTER_SELECT_DUNGEON requires at least four body bytes; received '{body.Length}'.");
        }

        return new EnterSelectDungeonRequest(
            metadata, BinaryPrimitives.ReadUInt32LittleEndian(body));
    }
}

/// <summary>NOTI 0x0017 <c>USER_AREA</c>: where a user is, as the client should display it.</summary>
internal sealed record UserAreaNotification(
    ushort WireUserId,
    EntrySpawnState Spawn,
    byte AreaId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameUserArea;
}

/// <summary>NOTI 0x0003 <c>USER_STATE</c>: a counted list of (user, state) pairs.</summary>
internal sealed record UserStateNotification(
    ushort WireUserId,
    byte UserState) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameUserState;
}

/// <summary>NOTI 0x001A: the peer-to-peer UDP host offer. A single zero means "none".</summary>
internal sealed record UdpHostNotification : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameUdpHost;
}

/// <summary>NOTI 0x001B: the dungeon-selection screen's own state.</summary>
internal sealed record EnterSelectDungeonNotification(
    ushort WireUserId,
    ushort TowerOfDespairFloor) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEnterSelectDungeonState;
}

/// <summary>
/// Bodies for the four notifications the reference server pushes when the dungeon-selection screen
/// opens.
/// </summary>
/// <remarks>
/// Transcribed from <c>86JP</c>: <c>TownAreaNotificationBuilder.BuildUserArea</c>,
/// <c>EnterSelectDungeonStateBuilder.BuildUserState</c> / <c>BuildEnterSelectDungeon</c> and
/// <c>UdpHostBuilder.BuildUnavailable</c>. That server enters towns successfully, so these shapes
/// work against some client -- but the reference is not the authority here, the client is, and the
/// client's own handlers for these types were not walked. Treat the whole set as one experiment:
/// if the client advances past loading, the shapes are close enough to refine field by field; if
/// it does not, the premise that a missing reply is what stalls it was wrong.
/// </remarks>
internal static class DungeonSelectionPayloadCodec
{
    /// <summary>
    /// The area byte the reference server substitutes while the selection screen is open, in place
    /// of the character's real area.
    /// </summary>
    public const byte SelectionScreenAreaId = 0xFF;

    /// <summary>Floor used when no Tower of Despair progress is known.</summary>
    public const ushort DefaultTowerOfDespairFloor = 1;

    public static byte[] EncodeUserArea(ushort wireUserId, EntrySpawnState spawn, byte areaId)
    {
        using var stream = new MemoryStream(10);
        using var writer = new BinaryWriter(stream);
        writer.Write(wireUserId);
        writer.Write(spawn.TownId);
        writer.Write(areaId);
        writer.Write(spawn.X);
        writer.Write(spawn.Y);
        writer.Write(spawn.Direction);
        writer.Write(spawn.AreaState);
        return stream.ToArray();
    }

    public static byte[] EncodeUserState(ushort wireUserId, byte userState)
    {
        using var stream = new MemoryStream(4);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(wireUserId);
        writer.Write(userState);
        return stream.ToArray();
    }

    public static byte[] EncodeUdpHostUnavailable() => [0x00];

    public static byte[] EncodeEnterSelectDungeon(ushort wireUserId, ushort towerFloor)
    {
        using var stream = new MemoryStream(19);
        using var writer = new BinaryWriter(stream);
        writer.Write(1);
        writer.Write((ushort)0);
        writer.Write((byte)1);
        writer.Write(wireUserId);
        writer.Write((byte)0);
        writer.Write(0);

        // The reference server notes the client reads this at body offset 14 for a solo entry, and
        // that each extra party member shifts it by three. Only the solo shape is produced here.
        writer.Write(towerFloor);
        writer.Write(new byte[3]);
        return stream.ToArray();
    }
}

internal sealed class UserAreaEncoder : GameNotiMessageEncoder<UserAreaNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(UserAreaNotification message) =>
        OnePayload(
            GameProtocol.UserAreaNotificationType,
            DungeonSelectionPayloadCodec.EncodeUserArea(
                message.WireUserId, message.Spawn, message.AreaId));
}

internal sealed class UserStateEncoder : GameNotiMessageEncoder<UserStateNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(UserStateNotification message) =>
        OnePayload(
            GameProtocol.UserStateNotificationType,
            DungeonSelectionPayloadCodec.EncodeUserState(message.WireUserId, message.UserState));
}

internal sealed class UdpHostEncoder : GameNotiMessageEncoder<UdpHostNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(UdpHostNotification message) =>
        OnePayload(
            GameProtocol.UdpHostNotificationType,
            DungeonSelectionPayloadCodec.EncodeUdpHostUnavailable());
}

internal sealed class EnterSelectDungeonEncoder
    : GameNotiMessageEncoder<EnterSelectDungeonNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        EnterSelectDungeonNotification message) =>
        OnePayload(
            GameProtocol.EnterSelectDungeonNotificationType,
            DungeonSelectionPayloadCodec.EncodeEnterSelectDungeon(
                message.WireUserId, message.TowerOfDespairFloor));
}

/// <summary>
/// Answers ENTER_SELECT_DUNGEON with the five-notification burst the reference server sends.
/// </summary>
/// <remarks>
/// The second of the five is USERINFO subtype 1 (NOTI 0x0002) -- the same stat block the entry
/// batch already delivered, sent again. A first attempt left it out on the reasoning that the
/// client had just received it; the client entered the town but stood there with every stat at
/// zero, and a capture proved the entry-batch copy had arrived intact and correct. So the client
/// does not carry those numbers across into the town actor: it rebuilds the actor when the
/// selection screen opens and repopulates it from this frame. Re-sending is not redundancy.
///
/// It is rebuilt from the prepared entry state rather than re-read from storage, so it is
/// byte-identical to the copy sent during selection. The reference server re-reads; nothing
/// between the two points can change a stat today, and matching what was already sent is the
/// property worth keeping while that is true.
///
/// No CMD 0x000F reply is sent, because the reference server sends none either -- its only CMD on
/// this path is an error body for a blocked mercenary. The client does register a receive handler
/// for CMD 0x000F, and entering the town without one shows it is not required on the success path.
/// </remarks>
public sealed class EnterSelectDungeonHandler(
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IGameMessageSender sender,
    ILogger<EnterSelectDungeonHandler> logger)
    : ClientMessageHandler<EnterSelectDungeonRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        EnterSelectDungeonRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null
            || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null)
        {
            // Reached before a character is published. Nothing can be answered from an unpublished
            // session, and inventing a spawn would put the client somewhere the server does not
            // believe it is.
            logger.LogWarning(
                "Enter select dungeon {ConnectionId}: no published session, ignoring selection {Selection}",
                context.Connection.ConnectionId,
                message.Selection);
            return;
        }

        var spawn = result.World.Channel.Spawn;
        logger.LogInformation(
            "Enter select dungeon {ConnectionId} user={WireUserId}: selection={Selection} "
                + "town={TownId} area={AreaId}->0xFF pos=({X},{Y})",
            context.Connection.ConnectionId,
            session.WireUserId,
            message.Selection,
            spawn.TownId,
            spawn.AreaId,
            spawn.X,
            spawn.Y);

        // Addressed to the connection the request arrived on. The session records the connection
        // it was published against, and while the two are the same today, a reply belongs to the
        // asker by construction. Both are logged so a divergence shows up as data rather than as
        // silence.
        if (session.ConnectionId != context.Connection.ConnectionId)
        {
            logger.LogWarning(
                "Enter select dungeon {ConnectionId}: session is bound to {SessionConnectionId}",
                context.Connection.ConnectionId,
                session.ConnectionId);
        }

        // Equipment is resolved the same way the selection batch resolves it, because the stat
        // frame carries the equipped items alongside the stats and the two copies have to agree.
        var equipment = result.CharacterInventory.Containers
            .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)
            ?.Items ?? [];

        var notifications = new IGameServerMessage[]
        {
            new UserAreaNotification(
                session.WireUserId, spawn, DungeonSelectionPayloadCodec.SelectionScreenAreaId),
            new ExtendedUserStateMessage(
                result.Character,
                result.CharacterEntry.Public,
                result.CharacterEntry.Skills,
                equipment,
                result.World.PreparedAt),
            new UserStateNotification(session.WireUserId, 0x01),
            new UdpHostNotification(),
            new EnterSelectDungeonNotification(
                session.WireUserId, DungeonSelectionPayloadCodec.DefaultTowerOfDespairFloor),
        };

        // Logged here rather than left to the dispatch layer. The first live run produced this
        // handler's own log line and no frames at all, with nothing reported anywhere -- the
        // dispatch-layer catch is known not to fire, so an exception raised past this point has no
        // reporting path unless the handler provides one.
        try
        {
            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(notifications, []),
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Enter select dungeon {ConnectionId}: {Count} notifications handed to the sender",
                context.Connection.ConnectionId,
                notifications.Length);
        }
        catch (Exception error)
        {
            logger.LogError(
                error,
                "Enter select dungeon {ConnectionId}: sending the notification burst failed",
                context.Connection.ConnectionId);
            throw;
        }
    }
}
