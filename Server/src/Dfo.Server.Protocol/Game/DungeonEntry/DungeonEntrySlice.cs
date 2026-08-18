using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DungeonEntry;

/// <summary>
/// CMD 0x0010 <c>SELECT_DUNGEON</c>: the character committing to a dungeon and asking to be put
/// into its first room.
/// </summary>
/// <remarks>
/// The client sends this unprompted on a level-1 character, immediately after 0x000F and without
/// waiting for any reply -- the two are one burst rather than a request and its follow-up. Until
/// this handler existed the burst went unanswered and the tutorial could not be finished; it was
/// the only unhandled game frame. See protocol/patterns/flow-dungeon-entry.md.
///
/// The body is nine bytes and closed: identifier, difficulty, and two flags that only mean
/// anything for abyss dungeons. The trailing u32 has a determined boundary and no evidenced
/// meaning, so it is read past rather than interpreted.
/// </remarks>
public sealed record SelectDungeonRequest(
    GameMessageMetadata Metadata,
    ushort DungeonId,
    byte Difficulty,
    byte HellPartyRequested,
    byte HellPartyDifficulty) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSelectDungeon;
}

/// <summary>NOTI 0x001C <c>DUNGEON_INFO</c>: which dungeon is being run, and its shape.</summary>
internal sealed record DungeonInfoNotification(DungeonLayout Layout, byte Difficulty)
    : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDungeonInfo;
}

/// <summary>
/// NOTI 0x001D <c>START_MAP</c>: the frame that actually puts the character into a room.
/// </summary>
/// <param name="FirstEntry">
/// False when the character has stood in this room before. The reference builder has a separate
/// revisit path that differs in this flag alone for an ordinary room.
/// </param>
internal sealed record StartMapNotification(
    DungeonRunState Run, bool FirstEntry) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameStartMap;
}

/// <summary>NOTI 0x019F: the support characters standing in for absent party members.</summary>
internal sealed record SupportTagNotification : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSupportTag;
}

internal static class DungeonEntryPayloadCodec
{
    /// <summary>A maze cell coordinate the content never declares, used where a room is absent.</summary>
    private const byte NoCell = 0xFF;

    /// <summary>
    /// The reference builder's field order, with every value it defaults still written.
    /// </summary>
    /// <remarks>
    /// The generated field table for this type records six bytes; that is the lower bound static
    /// analysis reached before the client's handler stepped through an indirect call, not the
    /// body's length. The reference builder writes the rest, so it is followed here and the frame
    /// is thirty bytes. The values with no meaning to us are written as the builder's own
    /// defaults rather than as zero, because a default that the reference server chose is evidence
    /// and a zero we chose is not.
    /// </remarks>
    public static byte[] EncodeDungeonInfo(DungeonLayout layout, byte difficulty)
    {
        using var stream = new MemoryStream(30);
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((short)layout.DungeonId));
        writer.Write(difficulty);
        writer.Write(checked((byte)layout.MazeIndex));
        writer.Write(layout.BossCell.X);
        writer.Write(layout.BossCell.Y);
        // Not an abyss run: the reference server marks the absent abyss room rather than pointing
        // at cell (0,0), which is a cell an ordinary maze can occupy.
        writer.Write(NoCell);
        writer.Write(NoCell);
        writer.Write((byte)0); // dungeon mode
        writer.Write((byte)0); // no extra pair groups
        writer.Write((ushort)0); // abyss not enabled
        writer.Write((ushort)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(unchecked((int)0xFFFFFFFF)); // builder default packet seed
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0);
        return stream.ToArray();
    }

    /// <summary>
    /// The room the run opens in, and what stands in it.
    /// </summary>
    /// <remarks>
    /// The monster list is sent empty. Every field in this frame rests on transcription from the
    /// reference server -- the client's own handler yields only "at least one byte" -- so the first
    /// thing worth learning is whether the client accepts the frame's shape at all. An empty room
    /// answers that for a fraction of the work, and monsters can be filled in once the shape is
    /// known to be right rather than assumed to be.
    ///
    /// The seed is the layout's, not a fresh one: the client draws on the same value.
    /// </remarks>
    public static byte[] EncodeStartMap(DungeonRunState run, bool firstEntry)
    {
        var layout = run.Layout;
        using var stream = new MemoryStream(21);
        using var writer = new BinaryWriter(stream);
        writer.Write(run.CurrentCell.X);
        writer.Write(run.CurrentCell.Y);
        writer.Write((byte)0); // not a layered room
        writer.Write(layout.Seed);
        writer.Write((byte)0); // abyss mode
        writer.Write((byte)0); // unnamed byte the builder writes after it
        writer.Write(1); // room state value, the builder's default
        writer.Write(firstEntry ? (byte)1 : (byte)0);
        // The room's map identifier, not the maze index. The reference type calls this field
        // Index on a per-room record, which reads like a maze number and is not one: its blood
        // altar path passes the resolved map id into the same slot, and its abyss logging prints
        // it beside another map id. Sending the maze index here told the client to load map 0,
        // which it answered with a black screen and a crash a few seconds later (2026-08-18).
        writer.Write(checked((ushort)run.CurrentRoomMapId));
        writer.Write((byte)0); // monsters
        writer.Write((byte)0); // pre-generated drops
        writer.Write((byte)0); // abyss fog
        writer.Write((byte)0); // no ridable object groups
        writer.Write((byte)0xFF); // party member index, the builder's default for a run of one
        return stream.ToArray();
    }

    /// <summary>
    /// An empty support-character roster: a record count of zero and nothing after it.
    /// </summary>
    public static byte[] EncodeSupportTag() => [0x00, 0x00];
}

internal sealed class DungeonInfoEncoder : GameNotiMessageEncoder<DungeonInfoNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DungeonInfoNotification message) =>
        OnePayload(
            GameProtocol.DungeonInfoNotificationType,
            DungeonEntryPayloadCodec.EncodeDungeonInfo(message.Layout, message.Difficulty));
}

internal sealed class StartMapEncoder : GameNotiMessageEncoder<StartMapNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(StartMapNotification message) =>
        OnePayload(
            GameProtocol.StartMapNotificationType,
            DungeonEntryPayloadCodec.EncodeStartMap(message.Run, message.FirstEntry));
}

internal sealed class SupportTagEncoder : GameNotiMessageEncoder<SupportTagNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SupportTagNotification message) =>
        OnePayload(
            GameProtocol.SupportTagNotificationType,
            DungeonEntryPayloadCodec.EncodeSupportTag());
}

internal sealed class SelectDungeonDecoder : IGameMessageDecoder
{
    /// <summary>The body is closed at nine bytes; the first five carry every evidenced field.</summary>
    public const int MinimumBodyLength = 5;

    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"SELECT_DUNGEON requires at least five body bytes; received '{body.Length}'.");
        }

        return new SelectDungeonRequest(
            metadata,
            BinaryPrimitives.ReadUInt16LittleEndian(body),
            body[2],
            body[3],
            body[4]);
    }
}

/// <summary>
/// Answers a dungeon selection with the run's shape and the room it opens in.
/// </summary>
/// <remarks>
/// Nothing is committed on the character yet, and nothing is remembered about the run. This slice
/// exists to learn whether the client accepts these three frames, which is the question every
/// later piece depends on and none of which can be settled by reading the reference server. The
/// run aggregate, the party that owns entry, and the cost a run charges all arrive after that
/// answer.
///
/// No CMD reply. The reference server's success path produces none -- it delegates straight to the
/// run's lifecycle and pushes notifications -- and registration is not an obligation to reply; see
/// protocol/patterns/game-receive.md.
/// </remarks>
public sealed class SelectDungeonHandler(
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IDungeonLayoutRuleService layouts,
    IDungeonRunDomainService runs,
    IGameMessageSender sender,
    ILogger<SelectDungeonHandler> logger) : ClientMessageHandler<SelectDungeonRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SelectDungeonRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null
            || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null)
        {
            logger.LogWarning(
                "Select dungeon {ConnectionId}: no published session, ignoring dungeon {DungeonId}",
                context.Connection.ConnectionId,
                message.DungeonId);
            return;
        }

        // Gathered here and handed over, so the rule stays a function of what it is given. Cleared
        // quests come from the entry batch the session already carries rather than a fresh read,
        // which keeps the facts consistent with the state the client was told about.
        var quests = result.CharacterEntry.Quests;
        var facts = new DungeonEntryFacts(
            message.Difficulty,
            quests.ActiveQuests.Select(value => value.QuestId).ToArray(),
            quests.ClearedQuestIds);

        // One seed per run, chosen here and then carried: the rule never invents one, because a
        // layout has to be reproducible from what was recorded about the run.
        var seed = Random.Shared.Next();
        var layout = await layouts
            .ResolveAsync(message.DungeonId, facts, seed, cancellationToken)
            .ConfigureAwait(false);
        if (layout is null)
        {
            logger.LogWarning(
                "Select dungeon {ConnectionId}: the archive declares no enterable dungeon "
                    + "{DungeonId}, refusing",
                context.Connection.ConnectionId,
                message.DungeonId);
            return;
        }

        // Remembered from here on. The layout was chosen from quest state as it stood at this
        // moment, so it cannot be recomputed later and still describe the run the client is in.
        var run = runs.Begin(session.CharacterId, layout);

        logger.LogInformation(
            "Select dungeon {ConnectionId} user={WireUserId}: dungeon={DungeonId} difficulty={Difficulty} "
                + "maze={MazeIndex} start=({StartX},{StartY}) room={RoomId} boss=({BossX},{BossY}) seed={Seed}",
            context.Connection.ConnectionId,
            session.WireUserId,
            layout.DungeonId,
            message.Difficulty,
            layout.MazeIndex,
            layout.StartCell.X,
            layout.StartCell.Y,
            layout.StartRoomMapId,
            layout.BossCell.X,
            layout.BossCell.Y,
            layout.Seed);

        // This order, and all three. The reference server describes the run before it places the
        // character in it, and closes with the support roster even when it is empty.
        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [
                    new DungeonInfoNotification(layout, message.Difficulty),
                    new StartMapNotification(run, FirstEntry: true),
                    new SupportTagNotification(),
                ],
                []),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// CMD 0x002D <c>MOVE_MAP</c>: the character walking through a door into an adjoining room.
/// </summary>
/// <remarks>
/// Observed live on 2026-08-18: with the room entered and its doors shown as usable, walking into
/// one produced this frame and, because nothing answered it, no movement at all -- no indicator
/// over the character, no transition, and no disconnection either. A door that does nothing is
/// what an unanswered MOVE_MAP looks like.
///
/// The captured body was a hundred bytes, of which the first ten are evidenced and the rest sit
/// behind three runtime-counted repeat groups the static census could not expand. The evidenced
/// ten are enough: the target cell, and where in it the character lands.
/// </remarks>
public sealed record MoveMapRequest(
    GameMessageMetadata Metadata,
    byte NextX,
    byte NextY,
    uint PathPositionX,
    uint PathPositionY) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameMoveMap;
}

internal sealed class MoveMapDecoder : IGameMessageDecoder
{
    /// <summary>Ten bytes are read of the hundred observed; the rest have no evidenced meaning.</summary>
    public const int MinimumBodyLength = 10;

    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"MOVE_MAP requires at least ten body bytes; received '{body.Length}'.");
        }

        return new MoveMapRequest(
            metadata,
            body[0],
            body[1],
            BinaryPrimitives.ReadUInt32LittleEndian(body[2..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[6..]));
    }
}

/// <summary>
/// Moves the run into the requested room and describes it.
/// </summary>
/// <remarks>
/// The answer is another START_MAP. The reference server resolves the target against the maze and
/// then calls the same send path it used for entry, so a room change and an entry are the same
/// frame differing in one flag.
///
/// A cell the maze declares no room for is refused in silence, as the reference server refuses it.
/// The client only offers doors it can see, so a request for a cell we have no room for means the
/// two sides disagree about the maze; answering with some other room would bury that disagreement
/// somewhere harder to find.
/// </remarks>
public sealed class MoveMapHandler(
    ISelectedSessionDirectory selectedSessions,
    IDungeonRunDomainService runs,
    IGameMessageSender sender,
    ILogger<MoveMapHandler> logger) : ClientMessageHandler<MoveMapRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        MoveMapRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Move map {ConnectionId}: no published session, ignoring ({X},{Y})",
                context.Connection.ConnectionId, message.NextX, message.NextY);
            return;
        }

        var current = runs.Find(session.CharacterId);
        if (current is null)
        {
            logger.LogWarning(
                "Move map {ConnectionId}: no run in progress, ignoring ({X},{Y})",
                context.Connection.ConnectionId, message.NextX, message.NextY);
            return;
        }

        var target = new MazeCell(message.NextX, message.NextY);
        var firstEntry = !current.VisitedCells.Contains(target);
        var moved = runs.TryMove(session.CharacterId, target);
        if (moved is null)
        {
            logger.LogWarning(
                "Move map {ConnectionId}: maze {MazeIndex} of dungeon {DungeonId} declares no room "
                    + "at ({X},{Y}), staying in ({FromX},{FromY})",
                context.Connection.ConnectionId,
                current.Layout.MazeIndex,
                current.Layout.DungeonId,
                message.NextX,
                message.NextY,
                current.CurrentCell.X,
                current.CurrentCell.Y);
            return;
        }

        logger.LogInformation(
            "Move map {ConnectionId} user={WireUserId}: ({FromX},{FromY}) -> ({ToX},{ToY}) "
                + "room={RoomId} first={FirstEntry} path=({PathX},{PathY})",
            context.Connection.ConnectionId,
            session.WireUserId,
            current.CurrentCell.X,
            current.CurrentCell.Y,
            moved.CurrentCell.X,
            moved.CurrentCell.Y,
            moved.CurrentRoomMapId,
            firstEntry,
            message.PathPositionX,
            message.PathPositionY);

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([new StartMapNotification(moved, firstEntry)], []),
            cancellationToken).ConfigureAwait(false);
    }
}
