using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
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
/// A21 entry carries 15 bytes. Offset5 is the entry-mode enum (1 requests manual hell);
/// offset6 controls practice, independently of the server-selected hell difficulty.
/// </remarks>
public sealed record SelectDungeonRequest(
    GameMessageMetadata Metadata,
    int DungeonId,
    DungeonDifficulty Difficulty,
    byte RequestMode,
    byte PracticeMode,
    int? MirrorQuestId = null) : IClientNetworkMessage
{
    public ClientMessageType MessageType => MirrorQuestId is null ? ClientMessageType.GameSelectDungeon : ClientMessageType.GameMirrorDungeonEntry;
}

/// <summary>
/// The CMD refusal under the same type: why the character is not going in.
/// </summary>
/// <remarks>
/// Three bytes: zero, error code, context. Most codes retain reference-server names.
/// A21 distinguishes ordinary missing materials (0x11) from hell tickets (0x1B);
/// the latter also updates the hell selection control. See protocol/game/client-to-server/0x0010.md.
/// <para>
/// Before 2026-08-26 a selection this server could not answer was answered with nothing at all,
/// which reaches the player as a button that does nothing. A probe against dungeon 5000 -- a
/// training room the archive declares no maze for -- produced exactly that silence.
/// </para>
/// </remarks>
public sealed record SelectDungeonRefusal(byte ErrorCode, byte Context, bool Mirror = false) : IGameCmdMessage
{
    /// <summary>The archive declares no enterable dungeon under that identifier.</summary>
    public const byte DungeonNotFound = 0x15;

    /// <summary>The dungeon or requested mode is currently unavailable.</summary>
    public const byte DungeonUnavailable = 0x01;

    /// <summary>The selection screen is not open, or is open on something else.</summary>
    public const byte InvalidSelectionState = 0x13;

    /// <summary>An entry item the dungeon requires is not held.</summary>
    public const byte MissingRequiredItem = 0x11;

    /// <summary>A participant lacks the hell entry ticket.</summary>
    public const byte MissingHellTicket = 0x1B;

    /// <summary>Not enough fatigue left to enter.</summary>
    public const byte InsufficientFatigue = 0x16;

    /// <summary>Only the party leader may take a party in.</summary>
    public const byte NotPartyLeader = 0x08;

    /// <summary>Today's entries are used up.</summary>
    public const byte DailyEntryLimitReached = 0xF6;
    public const byte MemberEntryLimitReached = 0xAD;
    public const byte LevelTooLow = 0xAC;
    public const byte HellLevelTooLow = 0x6F;
    // A21's ordinary-entry switch maps 0x07 to upgrade-room broadcasting, not a
    // missing prerequisite. Its 0x13 branch reports an unavailable selection state.
    public const byte MissingPrerequisite = InvalidSelectionState;
    // A21 uses this branch for an invalid dungeon or difficulty, not an entry quota.
    public const byte InvalidDungeonOrDifficulty = 0x15;

    /// <summary>The current selection cohort cannot enter. This is not a quota failure.</summary>
    public const byte SelectionCohortUnavailable = InvalidSelectionState;

    public ServerMessageType MessageType => Mirror ? ServerMessageType.GameMirrorDungeonEntry : ServerMessageType.GameSelectDungeon;

    /// <summary>The refusals that are about the character rather than about a party member.</summary>
    public static SelectDungeonRefusal For(byte errorCode) => new(errorCode, 0);
}

/// <summary>NOTI 0x001C <c>DUNGEON_INFO</c>: which dungeon is being run, and its shape.</summary>
internal sealed record DungeonInfoNotification(DungeonLayout Layout, DungeonDifficulty Difficulty)
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
/// <param name="Actors">
/// What stands in the room, in the order the client is to build it: the map's monsters and then
/// its AI characters.
/// </param>
/// <param name="FirstActorSequence">
/// The running number the room's first actor is given. Every later actor takes the next one, and
/// the client names an actor by that number when it reports one dead.
/// </param>
internal sealed record StartMapNotification(
    DungeonRunState Run,
    bool FirstEntry,
    IReadOnlyList<RoomActorDescription> Actors,
    ushort FirstActorSequence,
    IReadOnlyList<FloorPiece> Drops) : IGameNotiMessage
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
    /// <summary>NOTI 0x001C as A21 builds it: 32 bytes.</summary>
    /// <remarks>
    /// Three things separate this from the thirty-byte shape it replaces, and all three come from
    /// A21's own builder read against the reference session's frame:
    ///
    ///   * the dungeon id is an <c>i32</c>, not an <c>i16</c>. That alone is the two-byte
    ///     difference, and it is what puts the trailing <c>FFFFFFFF</c> sentinel at offset 18 where
    ///     the reference has it rather than at 16;
    ///   * offsets 8/9 are two independent native bytes (RVA 00D523AE/BC). Upstream uses
    ///     the selected hell cell X/Y; ordinary A21 vectors retain zero/zero. Active-hell
    ///     coordinate behavior is reference-backed and awaits real-client acceptance;
    ///   * a constant one follows the minimap group count. A21's builder writes it outside the
    ///     dynamic group list and its own comment declines to read it as a switch, so it is copied
    ///     rather than interpreted.
    ///
    /// The generated field table records six bytes for this type; that is where static analysis
    /// stopped, not the body's length.
    /// </remarks>
    public static byte[] EncodeDungeonInfo(DungeonLayout layout, DungeonDifficulty difficulty)
    {
        using var stream = new MemoryStream(32);
        using var writer = new BinaryWriter(stream);
        writer.Write(layout.DungeonId);
        writer.Write((byte)difficulty);
        writer.Write(checked((byte)layout.MazeIndex));
        writer.Write(layout.BossCell.X);
        writer.Write(layout.BossCell.Y);
        writer.Write(layout.Hell?.Cell.X ?? (byte)0);
        writer.Write(layout.Hell?.Cell.Y ?? (byte)0);
        writer.Write((byte)0); // practice mode
        writer.Write((byte)0); // no minimap groups
        // A21 DUNGEON_INFO reads this u16 into scene +0x43c. The despair banner
        // renders that value; the ordinary reference's 01 00 was floor 1, not a fixed flag.
        writer.Write(checked((ushort)(layout.Despair?.Floor ?? 1)));
        writer.Write(new byte[4]);
        writer.Write(unchecked((int)0xFFFFFFFF)); // builder default packet seed
        writer.Write(new byte[10]);
        return stream.ToArray();
    }

    /// <summary>
    /// The room the run opens in, and what stands in it.
    /// </summary>
    /// <remarks>
    /// The actor list was sent empty until 2026-08-26, on the reasoning that the frame's shape
    /// should be shown to be right before anything was put in it. That was answered -- and then
    /// left standing, because the session it was checked against ran a maze whose first two rooms
    /// declare no monsters at all, so an empty list matched it byte for byte. A room that does
    /// declare monsters reaches the client as one holding nothing: it draws them from its own copy
    /// of the map, but they are scenery. The player walks through them, swings through them, and
    /// they never swing back.
    ///
    /// Each actor also carries its place within its own kind, and every one of them carried zero
    /// until 2026-08-27. The tutorial's first room holds one monster and one AI character, which
    /// are the first of their two kinds, so zero was right there by accident and the second room
    /// was where it showed: four actors all claiming the same place, of which the client kept one.
    /// The player walked into a room whose two monsters were not there and whose shoulder piece was
    /// already lying on the floor: what reveals a declared drop is the client's own script, and the
    /// step it waits on -- those monsters dying -- had nothing left to wait for.
    ///
    /// The seed is the room's, not the run's. The reference server draws a fresh one for every
    /// room it builds and clears one bit of it; this derives the room's from the run's so that a
    /// re-described room describes itself the same way, and clears the same bit. What that bit
    /// means to the client has not been read out of it -- only that the reference will not send it
    /// set.
    /// </remarks>
    public static byte[] EncodeStartMap(
        DungeonRunState run,
        bool firstEntry,
        IReadOnlyList<RoomActorDescription> actors,
        ushort firstActorSequence,
        IReadOnlyList<FloorPiece> drops)
    {
        var layout = run.Layout;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(run.CurrentCell.X);
        writer.Write(run.CurrentCell.Y);
        writer.Write((byte)0); // not a layered room
        writer.Write(RoomSeed(run));
        // A21's builder defaults this to two, not zero, and the reference session carries two here
        // on both ordinary START_MAP frames. Hell difficulty values follow upstream; native
        // A21 reads this byte, but active-hell behavior still needs real-client acceptance.
        writer.Write(run.Hell?.Difficulty ?? (byte)2);
        writer.Write((byte)0); // unnamed byte the builder writes after it
        writer.Write(1); // room state value, the builder's default
        writer.Write(firstEntry ? (byte)1 : (byte)0);
        // The room's map identifier, not the maze index. The reference type calls this field
        // Index on a per-room record, which reads like a maze number and is not one: its blood
        // altar path passes the resolved map id into the same slot, and its abyss logging prints
        // it beside another map id. Sending the maze index here told the client to load map 0,
        // which it answered with a black screen and a crash a few seconds later (2026-08-18).
        // An i32 on A21, not a u16 -- the other half of this frame's two-byte growth. The value is
        // unchanged; only its width was wrong.
        writer.Write(run.CurrentRoomMapId);

        if (actors.Count > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"A room declares {actors.Count} actors and the frame counts them in one byte.");
        }

        writer.Write((byte)actors.Count);
        var monstersWritten = 0;
        var aiCharactersWritten = 0;
        for (var index = 0; index < actors.Count; index++)
        {
            // Twenty-one bytes each. The template order is the room's own bookkeeping and a run of
            // one has nothing to say in it; the sequence is what the client names the actor by
            // afterwards; the packet index is the actor's place within its own kind.
            var actor = actors[index].Actor;
            var templateOwner = actors[index].ObjectTemplateOwner;
            var packetIndex = templateOwner.HasValue ? actors[index].ObjectTemplateIndex
                : actors[index].IsAiCharacter ? aiCharactersWritten++ : monstersWritten++;

            writer.Write(templateOwner.HasValue ? (ushort)templateOwner.Value : actors[index].HellOrder);
            writer.Write(packetIndex);
            writer.Write(unchecked((ushort)(firstActorSequence + index)));
            writer.Write(actor.Code);
            // Room creation has resolved the map's absolute/relative level fields against this
            // dungeon. LevelOf remains the fallback for any actor with no declared level.
            writer.Write(layout.LevelOf(actor.Level));
            writer.Write(actor.ActorType);
            writer.Write(templateOwner.HasValue || actors[index].IsHellTemplate ? (byte)1 : (byte)0);
            writer.Write(templateOwner ?? (actors[index].IsHellTemplate ? (byte)0xFF : (byte)0));
            writer.Write(0); // extra state
            writer.Write((byte)0); // A21 actor record extension
        }

        // What the room's breakable objects hold, worked out before the room opens rather than when
        // one breaks: the client is told up front, and the tutorial's second room will not let the
        // player past that step until it has been. What is passed here is what the room still
        // holds -- a piece already picked up is gone from the list but has not renumbered the
        // rest, so a re-described room names everything the way it first did.
        if (drops.Count > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"A room declares {drops.Count} drops and the frame counts them in one byte.");
        }

        writer.Write((byte)drops.Count);
        for (var index = 0; index < drops.Count; index++)
        {
            // Nineteen bytes each, and not the forty-eight a kill's drop takes -- the two frames
            // describe the same thing in different shapes.
            writer.Write(drops[index].Drop.ObjectIndex);
            writer.Write(drops[index].Number); // what the client names it by when it picks it up
            writer.Write(checked((uint)drops[index].Drop.ItemId));
            // Count or discriminator, and which is decided before the frame is built rather than
            // here: the reading depends on what the item is, which is an archive question. See
            // FloorPieceValues.
            writer.Write(drops[index].WireValue);
            writer.Write(drops[index].Drop.Durability);
            writer.Write((byte)0); // amplify type
            writer.Write((ushort)0); // amplify value
            writer.Write((ushort)0);
            writer.Write((byte)0);
        }

        writer.Write(run.Hell is not null ? (byte)1 : (byte)0); // abyss fog
        writer.Write((byte)0); // no ridable object groups
        // The client binds its local dungeon actor and party HUD to this frozen entry slot. 0xFF is
        // only the solo/default sentinel; sending it to a party participant leaves the shared run
        // alive on the server while the client constructs a one-player room.
        writer.Write(run.Participant?.Slot ?? (byte)0xFF);
        return stream.ToArray();
    }

    /// <summary>
    /// The value a dropped item carries as its own, derived the same way the actor numbers are.
    /// </summary>
    /// <remarks>
    /// A working A21 session draws it, and what the item ends up carrying once picked up is the
    /// same value -- the reference session's dropped shoulder and the equipped one share it. So it
    /// identifies the instance rather than counting anything, and what it must be is distinct
    /// within a room and stable across a re-send of that room.
    ///
    /// Kept inside a signed thirty-two bits, and away from zero. The picked-up piece is stored
    /// carrying this same value, and the column it is stored in is signed: a value that only fits
    /// unsigned would reach the wire as one number and the database as another.
    /// </remarks>
    /// <summary>
    /// The seed one room is described with.
    /// </summary>
    /// <remarks>
    /// The reference server draws a fresh value per room and clears bit 18 of it before sending.
    /// Neither the draw nor the mask is explained there, and the client has not been read for what
    /// it does with either; what can be said is that the reference never sends that bit set, and
    /// that every room of a run gets a different value.
    /// <para>
    /// This derives the value from the run's seed and the cell rather than drawing it, so that a
    /// room re-described after a revisit describes itself the same way. The mask is copied.
    /// </para>
    /// </remarks>
    internal static int RoomSeed(DungeonRunState run)
    {
        var mixed = (uint)run.Layout.Seed;
        mixed ^= ((uint)run.CurrentCell.X << 8) | run.CurrentCell.Y;
        mixed *= 2654435761u;
        mixed ^= mixed >> 15;
        mixed *= 2246822519u;
        mixed ^= mixed >> 13;
        return (int)(mixed & 0x7FFFFFFFu & ~ReservedSeedBit);
    }

    /// <summary>The one bit of the room seed the reference server always clears.</summary>
    private const uint ReservedSeedBit = 0x40000u;

    /// <summary>
    /// An empty support-character roster: a record count of zero and nothing after it.
    /// </summary>
    public static byte[] EncodeSupportTag() => [0x00, 0x00];
}

internal sealed class SelectDungeonRefusalEncoder : GameCmdMessageEncoder<SelectDungeonRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SelectDungeonRefusal message) =>
        OnePayload(
            message.Mirror ? (ushort)0x02B6 : GameProtocol.SelectDungeonType,
            new byte[] { 0x00, message.ErrorCode, message.Context });
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
            DungeonEntryPayloadCodec.EncodeStartMap(
                message.Run, message.FirstEntry, message.Actors, message.FirstActorSequence,
                message.Drops));
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
    /// <summary>The confirmed A21 ordinary and tutorial requests carry 15 bytes.</summary>
    public const int MinimumBodyLength = 15;

    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"SELECT_DUNGEON requires at least fifteen body bytes; received '{body.Length}'.");
        }

        var dungeonId = BinaryPrimitives.ReadUInt32LittleEndian(body);
        var difficulty = (DungeonDifficulty)body[4];
        if (dungeonId is 0 or > int.MaxValue || !Enum.IsDefined(difficulty))
            throw new InvalidDataException("SELECT_DUNGEON has an invalid dungeon or difficulty.");
        return new SelectDungeonRequest(metadata, (int)dungeonId, difficulty, body[5], body[6]);
    }
}

/// <summary>
/// Answers a dungeon selection with the run's shape and the room it opens in.
/// </summary>
/// <remarks>
/// The visit domain checks party fatigue and commits the initial room plus its charge.
/// This handler only delivers the resulting run and authoritative balance.
///
/// No CMD reply. The reference server's success path produces none -- it delegates straight to the
/// run's lifecycle and pushes notifications -- and registration is not an obligation to reply; see
/// protocol/patterns/game-receive.md.
/// </remarks>
public sealed class SelectDungeonHandler(ICommandBus commands) : ClientMessageHandler<SelectDungeonRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameSelectDungeonCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SelectDungeonRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameSelectDungeonCommand(context, message), cancellationToken).ConfigureAwait(false);
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
public sealed class MoveMapHandler(Dfo.Server.Application.Commands.ICommandBus commands) : ClientMessageHandler<MoveMapRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<Dfo.Server.Protocol.Game.DungeonEntry.GameMoveMapCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, MoveMapRequest message, CancellationToken token) =>
        _ = await commands.SendAsync(new Dfo.Server.Protocol.Game.DungeonEntry.GameMoveMapCommand(context, message), token).ConfigureAwait(false);
}
