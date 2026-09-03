using System.Buffers.Binary;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.TownProjection;

/// <summary>
/// CMD 0x0024 <c>SET_USER_AREA</c>: the character walking from one town area into another, which is
/// also how it crosses between towns.
/// </summary>
/// <remarks>
/// This is the only command that moves a character between towns, so it is the only input that can
/// change the town the entry acknowledgement reports on the next login. The restore half of that
/// round trip was already complete -- EntryPlacementRuleService reads the stored town and falls back to
/// the default only when it is absent -- but nothing ever wrote a different value, so "resume in the
/// town you left" could not fail and could not work either.
///
/// The body's layout is closed at 15 bytes by the client's own builders; the reference server reads
/// the first six and ignores the rest. See protocol/game/client-to-server/0x0024.md.
/// </remarks>
public sealed record SetUserAreaRequest(
    GameMessageMetadata Metadata,
    byte TownId,
    byte AreaId,
    short X,
    short Y) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetUserArea;
}

/// <summary>
/// CMD 0x0023 <c>SET_USER_POSITION</c>: where the character now stands inside its current area.
/// </summary>
/// <remarks>
/// The body is seven bytes -- <c>DA01 EA00 05 68 00</c>: x 474, y 234, direction 5, then a
/// <c>u16</c> whose meaning is still unevidenced. The 2026-08-17 A12 session captured nine of these
/// byte-identical frames and nothing coming back, which is why this was recorded as report-only.
/// A21 does answer: its reference session replies to every 0x0023 with NOTI 0x0016 carrying the
/// wire user id followed by the same x, y, direction and trailing pair the report just supplied.
/// </remarks>
public sealed record SetUserPositionRequest(
    GameMessageMetadata Metadata,
    short X,
    short Y,
    byte Direction,
    ushort Trailer) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetUserPosition;
}

/// <summary>One character standing in an area, as NOTI 0x0018 reports it.</summary>
public sealed record AreaUserPresence(
    ushort WireUserId,
    short X,
    short Y,
    byte Direction,
    byte AreaState);

/// <summary>
/// NOTI 0x0018 <c>AREA_USERS</c>: everyone standing in the area, the mover included.
/// </summary>
/// <remarks>
/// Sent alongside 0x0017 on every area change, and required even when the mover is alone: the
/// 2026-08-17 session answered 0x0024 with 0x0017 only, and the client resent the same 0x0024
/// eight times before giving up and sending a bare 0x0060. The reference server builds its roster
/// as <c>{ self } + others</c>, so the solo case is a one-entry list rather than a special case.
/// </remarks>
internal sealed record AreaUsersNotification(
    byte TownId,
    byte AreaId,
    IReadOnlyList<AreaUserPresence> Users) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAreaUsers;
}

internal static class TownProjectionPayloadCodec
{
    /// <summary>
    /// <c>u8 town, u8 area, u16 count</c>, then <c>u16 user, i16 x, i16 y, u8 direction, u8 state</c>
    /// per character. The count is a real prefix, not a fixed one.
    /// </summary>
    public static byte[] EncodeAreaUsers(
        byte townId,
        byte areaId,
        IReadOnlyList<AreaUserPresence> users)
    {
        using var stream = new MemoryStream(4 + (users.Count * 8));
        using var writer = new BinaryWriter(stream);
        writer.Write(townId);
        writer.Write(areaId);
        writer.Write(checked((ushort)users.Count));
        foreach (var user in users)
        {
            writer.Write(user.WireUserId);
            writer.Write(user.X);
            writer.Write(user.Y);
            writer.Write(user.Direction);
            writer.Write(user.AreaState);
        }

        return stream.ToArray();
    }
}

/// <summary>NOTI 0x0016 <c>USER_POSITION</c>: where one character stands, echoed to the reporter.</summary>
internal sealed record UserPositionNotification(
    ushort WireUserId,
    short X,
    short Y,
    byte Direction,
    ushort Trailer) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameUserPosition;
}

internal sealed class UserPositionEncoder : GameNotiMessageEncoder<UserPositionNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(UserPositionNotification message)
    {
        var body = new byte[9];
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.WireUserId);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2), message.X);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(4), message.Y);
        body[6] = message.Direction;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(7), message.Trailer);
        return OnePayload(GameProtocol.UserPositionNotificationType, body);
    }
}

internal sealed class AreaUsersEncoder : GameNotiMessageEncoder<AreaUsersNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AreaUsersNotification message) =>
        OnePayload(
            GameProtocol.AreaUsersNotificationType,
            TownProjectionPayloadCodec.EncodeAreaUsers(
                message.TownId, message.AreaId, message.Users));
}

internal sealed class SetUserAreaDecoder : IGameMessageDecoder
{
    /// <summary>Six bytes are read of the fifteen the client sends; the rest carry no meaning yet.</summary>
    public const int MinimumBodyLength = 6;

    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"SET_USER_AREA requires at least six body bytes; received '{body.Length}'.");
        }

        return new SetUserAreaRequest(
            metadata,
            body[0],
            body[1],
            BinaryPrimitives.ReadInt16LittleEndian(body[2..]),
            BinaryPrimitives.ReadInt16LittleEndian(body[4..]));
    }
}

internal sealed class SetUserPositionDecoder : IGameMessageDecoder
{
    /// <summary>The captured body is seven bytes; x, y and direction occupy the first five.</summary>
    public const int MinimumBodyLength = 5;

    /// <summary>What the entry frame carries, used when a short body omits the trailing pair.</summary>
    private const ushort DefaultTrailer = 100;

    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"SET_USER_POSITION requires at least five body bytes; received '{body.Length}'.");
        }

        return new SetUserPositionRequest(
            metadata,
            BinaryPrimitives.ReadInt16LittleEndian(body),
            BinaryPrimitives.ReadInt16LittleEndian(body[2..]),
            body[4],
            // Echoed straight back in the answer. The captured value is 104 where the entry frame
            // carried 100, so it is the client's to state rather than a constant of ours.
            body.Length >= 7 ? BinaryPrimitives.ReadUInt16LittleEndian(body[5..]) : DefaultTrailer);
    }
}

/// <summary>
/// Moves the published session to the requested area and tells the client where it now is.
/// </summary>
/// <remarks>
/// The new position is written into the runtime session rather than to storage. Cleanup already
/// persists the runtime's last position when a session ends, so a town crossing survives logout
/// without a second write path -- and one write at the end beats one per step. The cost is a hard
/// client kill that never closes the socket: the crossing is then lost. The reference server buys
/// that back with a throttled write-through on every step, which is worth copying only once
/// something shows the loss happening.
///
/// Nothing is answered on the CMD side. The client does register a receive handler for this type
/// (RVA 0x008E37E0), but the reference server's success path pushes only NOTI 0x0017, and
/// registration is not an obligation to reply -- see protocol/patterns/game-receive.md.
/// </remarks>
public sealed class SetUserAreaHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IChannelEntryPolicy channels,
    ITownMovementDomainService towns,
    IGameMessageSender sender,
    ILogger<SetUserAreaHandler> logger) : ClientMessageHandler<SetUserAreaRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SetUserAreaRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null
            || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null)
        {
            logger.LogWarning(
                "Set user area {ConnectionId}: no published session, ignoring {TownId}:{AreaId}",
                context.Connection.ConnectionId,
                message.TownId,
                message.AreaId);
            return;
        }

        // Refused rather than clamped. The byte the entry acknowledgement carries is a lookup key
        // the client dereferences without a null check, so storing a town the content does not
        // declare would turn this step into a crash on the next login instead of here.
        if (!await towns.DeclaresAreaAsync(message.TownId, message.AreaId, cancellationToken)
                .ConfigureAwait(false))
        {
            logger.LogWarning(
                "Set user area {ConnectionId}: content declares no town {TownId} area {AreaId}, "
                    + "staying at {CurrentTown}:{CurrentArea}",
                context.Connection.ConnectionId,
                message.TownId,
                message.AreaId,
                result.Character.Position.TownId,
                result.Character.Position.AreaId);
            return;
        }

        var channel = await channels
            .ResolveAsync(context.Connection.ListenerPort, cancellationToken)
            .ConfigureAwait(false);
        if (!await towns.CanEnterTownAsync(channel, message.TownId, cancellationToken)
                .ConfigureAwait(false))
        {
            logger.LogWarning(
                "Set user area {ConnectionId}: channel refuses town {TownId}",
                context.Connection.ConnectionId,
                message.TownId);
            return;
        }

        var previous = result.Character.Position;
        var moved = previous with
        {
            TownId = message.TownId,
            AreaId = message.AreaId,
            X = message.X,
            Y = message.Y,
        };
        runtimeSessions.ReplaceExact(
            session,
            result with { Character = result.Character with { Position = moved } });

        logger.LogInformation(
            "Set user area {ConnectionId} user={WireUserId}: {FromTown}:{FromArea} -> "
                + "{ToTown}:{ToArea} pos=({X},{Y})",
            context.Connection.ConnectionId,
            session.WireUserId,
            previous.TownId,
            previous.AreaId,
            moved.TownId,
            moved.AreaId,
            moved.X,
            moved.Y);

        var spawn = result.World.Channel.Spawn with
        {
            TownId = moved.TownId,
            AreaId = moved.AreaId,
            X = moved.X,
            Y = moved.Y,
        };
        // Both frames, in this order. 0x0017 places the character; 0x0018 is the area's roster and
        // the client will not settle into the new area without it -- answering with 0x0017 alone made
        // it resend the same 0x0024 eight times.
        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [
                    new UserAreaNotification(session.WireUserId, spawn, moved.AreaId),
                    new AreaUsersNotification(
                        moved.TownId,
                        moved.AreaId,
                        [
                            new AreaUserPresence(
                                session.WireUserId, moved.X, moved.Y,
                                moved.Direction, spawn.AreaState),
                        ]),
                ],
                []),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Keeps the runtime session's position current so the write at cleanup records where the character
/// actually stood.
/// </summary>
/// <remarks>
/// The town and area are left alone: this command reports movement inside the area it is already in,
/// and taking an area from it would let a stale frame contradict the crossing that 0x0024 just
/// applied. A21 answers every report with NOTI 0x0016 carrying the position back; the solo case is
/// not an exception, because the reference session that shows it had one character in the town.
/// </remarks>
public sealed class SetUserPositionHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IGameMessageSender sender,
    ILogger<SetUserPositionHandler> logger) : ClientMessageHandler<SetUserPositionRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SetUserPositionRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null
            || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null)
        {
            logger.LogDebug(
                "Set user position {ConnectionId}: no published session, ignoring ({X},{Y})",
                context.Connection.ConnectionId,
                message.X,
                message.Y);
            return ValueTask.CompletedTask;
        }

        var moved = result.Character.Position with
        {
            X = message.X,
            Y = message.Y,
            Direction = message.Direction,
        };
        runtimeSessions.ReplaceExact(
            session,
            result with { Character = result.Character with { Position = moved } });
        return sender.SendNotiAsync(
            context.Connection.ConnectionId,
            new UserPositionNotification(
                session.WireUserId, message.X, message.Y, message.Direction, message.Trailer),
            cancellationToken);
    }
}
