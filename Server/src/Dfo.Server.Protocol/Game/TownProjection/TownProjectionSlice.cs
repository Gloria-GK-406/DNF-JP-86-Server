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
/// round trip was already complete -- EntryPlacementService reads the stored town and falls back to
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
/// Report-only -- the client registers no receive handler for this type. Nine frames were captured
/// on 2026-08-17 and all nine were byte-identical (<c>DA01 EA00 05 68 00</c>: x 474, y 234,
/// direction 5), because the character never moved off the arrival gate. The reference server reads
/// only x and y; the three trailing bytes have bounded width and no evidenced meaning, and
/// direction is inferred from the value matching the arrival direction the same server writes.
/// </remarks>
public sealed record SetUserPositionRequest(
    GameMessageMetadata Metadata,
    short X,
    short Y,
    byte Direction) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetUserPosition;
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
            body[4]);
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
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IChannelEntryPolicy channels,
    IEntryPlacementService placement,
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
        if (!await placement.DeclaresAreaAsync(message.TownId, message.AreaId, cancellationToken)
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
        if (!await placement.CanEnterTownAsync(channel, message.TownId, cancellationToken)
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
        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new UserAreaNotification(session.WireUserId, spawn, moved.AreaId)],
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
/// applied. Nothing is sent back -- the reference server only forwards it to other characters
/// standing in the same area, which needs a second character to matter.
/// </remarks>
public sealed class SetUserPositionHandler(
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
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
        return ValueTask.CompletedTask;
    }
}
