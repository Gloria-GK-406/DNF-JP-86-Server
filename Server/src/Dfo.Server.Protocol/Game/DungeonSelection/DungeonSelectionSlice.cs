using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.DungeonEntry;
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
    ushort Selection) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameEnterSelectDungeon;
}

internal sealed class EnterSelectDungeonDecoder : IGameMessageDecoder
{
    /// <remarks>
    /// The selection is a <c>u16</c>, not a <c>u32</c>: the client's own builder writes two bytes at
    /// offset 0, and the paired CMD 0x0010 has a closed layout that puts a difficulty byte at offset
    /// 2. Reading four bytes happened to yield the right number only because the observed dungeon is
    /// the tutorial at difficulty zero, which leaves bytes 2 and 3 clear. Those two bytes carry no
    /// evidenced meaning on this type and are not consumed.
    /// </remarks>
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < sizeof(ushort))
        {
            throw new InvalidDataException(
                $"ENTER_SELECT_DUNGEON requires at least two body bytes; received '{body.Length}'.");
        }

        return new EnterSelectDungeonRequest(
            metadata, BinaryPrimitives.ReadUInt16LittleEndian(body));
    }
}

/// <summary>CMD 0x000F: the single acceptance byte A21 answers ENTER_SELECT_DUNGEON with.</summary>
/// <remarks>
/// The comment on the handler below used to say no CMD reply was sent because the reference server
/// sends none. That was the A12 reference. A21's sends one after ENTER_SELECT_DUNGEON and a second
/// after SELECT_DUNGEON, both a single <c>01</c> -- its own builder calls it a success ack -- and
/// the second one comes immediately before NOTI 0x001B.
/// </remarks>
internal sealed record EnterSelectDungeonAcknowledgement : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEnterSelectDungeonAcknowledgement;
}

internal sealed class EnterSelectDungeonAcknowledgementEncoder
    : GameCmdMessageEncoder<EnterSelectDungeonAcknowledgement>
{
    private static readonly byte[] SuccessAck = [0x01];

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        EnterSelectDungeonAcknowledgement message) =>
        OnePayload(GameProtocol.EnterSelectDungeonType, SuccessAck);
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
/// <remarks>
/// Sent once per selection, and <b>which of the two moments it is sent at depends on the
/// tutorial</b>. Ordinarily it goes out with the rest of the screen's state, as the answer to
/// CMD 0x000F. The tutorial's own first entry is the exception: the reference server defers it,
/// and everything else the entry projects, until the client reports tutorial flag 30.
/// <para>
/// This server sent it only at the deferred moment until 2026-08-26, because the session it was
/// modelled on was a tutorial and that is where the frame appears on its wire. A player walking up
/// to a real dungeon gate therefore got the screen's other three frames and not this one -- see
/// <see cref="IDungeonVisitDomainService"/>.
/// </para>
/// </remarks>
internal sealed record EnterSelectDungeonNotification(
    ushort WireUserId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEnterSelectDungeonState;
}

/// <summary>
/// CMD 0x0084 <c>BACK_2_VILLAGE</c>: closing the dungeon-selection screen.
/// </summary>
/// <remarks>
/// An empty body, and <b>not</b> the same type as leaving a dungeon -- the reference server routes
/// both to one handler, which is what made it easy to assume one type covered both. Observed live
/// on 2026-08-26: the player opened the selection screen, pressed the button back to town, and
/// pressed it twice more because nothing happened. Neither captured session ever sent it.
/// <para>
/// The reference answers with a one-byte success, puts the character's state back to zero, and
/// projects the area it left from. That last part is why the button appears dead without it: the
/// client is waiting to be placed back in the town it is already standing in.
/// </para>
/// </remarks>
public sealed record BackToVillageRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameBackToVillage;
}

/// <summary>The CMD acknowledgement under the same type: the reference's one-byte success.</summary>
public sealed record BackToVillageResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameBackToVillage;
}

internal sealed class BackToVillageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new BackToVillageRequest(metadata);
}

internal sealed class BackToVillageResponseEncoder : GameCmdMessageEncoder<BackToVillageResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        BackToVillageResponse message) =>
        OnePayload(GameProtocol.BackToVillageType, new byte[] { 0x01 });
}

/// <summary>
/// Closes the selection screen and puts the character back where it opened it.
/// </summary>
/// <remarks>
/// Three frames in the reference's order: the success, the user state returning to zero, and the
/// area projection. The area comes from where the character is standing rather than from the
/// channel's spawn -- the player walked to the gate, and putting them back at the spawn would move
/// them across the town for pressing cancel.
/// <para>
/// Any run is ended as well. The reference routes this type and the one that leaves a dungeon to
/// the same handler, and a character who somehow reached here holding a run would otherwise keep
/// it for ever; see the same reasoning in <c>Game/DungeonClear</c>.
/// </para>
/// </remarks>
public sealed class BackToVillageHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    ICharacterPositionDomainService positions,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveries,
    IGameMessageSender sender,
    ILogger<BackToVillageHandler> logger) : ClientMessageHandler<BackToVillageRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        BackToVillageRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        // Where they are standing is asked of the service that owns it. It is recorded and dropped
        // with the published session, so this conjunct admits exactly the connections the two
        // before it do.
        if (session is null
            || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null
            || positions.Find(session.CharacterId) is not { } position)
        {
            logger.LogWarning(
                "Back to village {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        var currentRun = runs.Find(session.CharacterId);
        var ended = currentRun is not null
            && runs.End(session.CharacterId, currentRun.RunId);
        if (ended && currentRun is not null)
            deliveries.RemoveParticipant(currentRun.RunId, session.CharacterId);

        logger.LogInformation(
            "Back to village {ConnectionId} user={WireUserId}: {TownId}:{AreaId} pos=({X},{Y}) "
                + "runEnded={RunEnded}",
            context.Connection.ConnectionId,
            session.WireUserId,
            position.TownId,
            position.AreaId,
            position.X,
            position.Y,
            ended);

        // The same projection the dungeon's own exit sends, and deliberately not a second copy of
        // it: both mean "put this character back in the town they are standing in", and two copies
        // would drift into one route working and the other not.
        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [
                    new BackToVillageResponse(),
                    .. TownReturnProjection.Frames(session, result, position),
                ],
                []),
            cancellationToken).ConfigureAwait(false);
    }
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

    /// <summary>NOTI 0x001B as A21 builds it: 37 bytes for a party of one.</summary>
    /// <remarks>
    /// The nineteen-byte shape this replaces came from the older client's builder and lines up with
    /// A21's at no offset. A21's own builder writes, in order: two zero <c>i32</c>s, a count of
    /// hell-quest blocked slots, a zero byte, the user count, then <c>{u16 userId, u8 0}</c> per
    /// user, then a zero <c>i32</c>, a <c>u16</c> of one, a zero <c>u16</c>, a zero <c>i32</c>, two
    /// zero bytes, two zero <c>i32</c>s and a final zero byte.
    ///
    /// For one user that is 37 bytes with exactly three non-zero positions -- 10, 11 and 18 -- and
    /// the reference session's frame has non-zero bytes at 10, 11 and 18 and nowhere else. Those
    /// are the user count, the low byte of user id 1, and the constant one.
    ///
    /// The tower-of-despair floor this used to write has no place in A21's shape. It is not lost
    /// evidence: it was the older client's field, at an offset A21 does not use.
    /// </remarks>
    public static byte[] EncodeEnterSelectDungeon(ushort wireUserId)
    {
        using var stream = new MemoryStream(37);
        using var writer = new BinaryWriter(stream);
        writer.Write(0);
        writer.Write(0);
        writer.Write((byte)0);   // hell-quest blocked slot count
        writer.Write((byte)0);
        writer.Write((byte)1);   // user count
        writer.Write(wireUserId);
        writer.Write((byte)0);
        writer.Write(0);
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write(0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0);
        writer.Write(0);
        writer.Write((byte)0);
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
            DungeonSelectionPayloadCodec.EncodeEnterSelectDungeon(message.WireUserId));
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
/// Current character facts refresh the stat/skill projection before sending. Equipment uses the
/// resident inventory maintained by item moves. Equipment contributions are not a modeled part
/// of the base-stat rules.
///
/// A21 answers with CMD 0x000F carrying a single acceptance byte. This used to send no CMD reply,
/// on the grounds that the reference server sends none -- true of the A12 reference and not of the
/// A21 one, which sends it here and again after SELECT_DUNGEON.
/// </remarks>
public sealed class EnterSelectDungeonHandler(
    ICharacterStateProjectionDomainService currentState,
    ISelectedSessionDomainService selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IDungeonVisitDomainService visits,
    IDungeonEntryIntentDomainService entryIntents,
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
        var intent = message.Selection == 0
            ? DungeonEntryIntent.Interactive
            : DungeonEntryIntent.Directed(message.Selection);
        entryIntents.Record(session, intent);
        logger.LogInformation(
            "Enter select dungeon {ConnectionId} user={WireUserId}: selection={Selection} intent={Intent} "
                + "town={TownId} area={AreaId}->0xFF pos=({X},{Y})",
            context.Connection.ConnectionId,
            session.WireUserId,
            message.Selection,
            intent,
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

        // The resident inventory is refreshed after moves; the stat frame must use those items.
        var equipment = result.CharacterInventory.Containers
            .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)
            ?.Items ?? [];

        // A21's answer, in its order: the stat block, the acceptance, the user state, the UDP
        // offer. Two frames this used to send are gone from here:
        //
        //   NOTI 0x0017 USER_AREA -- the A21 reference sends it exactly twice in a whole session,
        //   both on the way back to town at the end, and never when the selection screen opens.
        //
        //   NOTI 0x001B -- it belongs to the SELECT_DUNGEON answer, where A21 sends it directly
        //   after the second acceptance byte.
        // The selection state closes the burst for everyone but the tutorial, whose first entry
        // defers it -- and every frame this server had been checked against came from a tutorial,
        // so it was only ever sent at the deferred moment. A player at a real dungeon gate got the
        // screen's other three frames and no state to draw it from (2026-08-26).
        result = await currentState.RefreshAsync(result, cancellationToken).ConfigureAwait(false);
        var defersState = visits.DefersSelectionState(result.Character.Progression.Level);
        List<IGameServerMessage> notifications =
        [
            new ExtendedUserStateMessage(
                result.Character,
                result.CharacterEntry.Public,
                result.CharacterEntry.Skills,
                equipment,
                result.World.PreparedAt),
            new EnterSelectDungeonAcknowledgement(),
            new UserStateNotification(session.WireUserId, 0x01),
            new UdpHostNotification(),
        ];
        if (!defersState)
        {
            notifications.Add(new EnterSelectDungeonNotification(session.WireUserId));
        }

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
                notifications.Count);
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
