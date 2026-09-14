using System.Buffers.Binary;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
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

/// <summary>
/// The CMD refusal under the same type: why the character is not going in.
/// </summary>
/// <remarks>
/// Three bytes -- a zero, a code, and a context byte that only the party-shaped refusals use. The
/// codes are the reference server's own
/// <c>DungeonAdmissionRejectBuilder</c> table; <b>what the client draws for each of them has not
/// been read out of its string table</b>, unlike the move and use refusals, so they are named for
/// what the server decided rather than for what the player is told.
/// <para>
/// Before 2026-08-26 a selection this server could not answer was answered with nothing at all,
/// which reaches the player as a button that does nothing. A probe against dungeon 5000 -- a
/// training room the archive declares no maze for -- produced exactly that silence.
/// </para>
/// </remarks>
public sealed record SelectDungeonRefusal(byte ErrorCode, byte Context) : IGameCmdMessage
{
    /// <summary>The archive declares no enterable dungeon under that identifier.</summary>
    public const byte DungeonNotFound = 0x15;

    /// <summary>
    /// The dungeon exists and this character may not enter it.
    /// </summary>
    /// <remarks>
    /// The reference server's catch-all, and what its minimum-level gate answers with. Nothing
    /// here sends it yet: the gate needs the archive's <c>[minimum required level]</c> and the
    /// story-dungeon exemption that goes with it, and a gate without the exemption would refuse
    /// dungeons the client legitimately offers. See <c>Docs/ProtocolScaffoldDissonances.md</c>.
    /// </remarks>
    public const byte DungeonUnavailable = 0x01;

    /// <summary>The selection screen is not open, or is open on something else.</summary>
    public const byte InvalidSelectionState = 0x13;

    /// <summary>An entry item the dungeon requires is not held.</summary>
    public const byte MissingRequiredItem = 0x11;

    /// <summary>Not enough fatigue left to enter.</summary>
    /// <remarks>
    /// Nothing sends it. Entry reports what it costs to <see cref="ICharacterFatigueDomainService"/>
    /// but nobody keeps that account and so nobody can find it short, which is a different state of
    /// affairs from <see cref="DungeonUnavailable"/>: there the rule is known and unwritten, here
    /// the balance this would be measured against does not exist yet.
    /// </remarks>
    public const byte InsufficientFatigue = 0x16;

    /// <summary>Only the party leader may take a party in.</summary>
    public const byte NotPartyLeader = 0x08;

    /// <summary>Today's entries are used up.</summary>
    public const byte DailyEntryLimitReached = 0xF6;

    /// <summary>A member has no entries left, or no permission. The context names which.</summary>
    public const byte MemberBlocked = 0xAD;

    public ServerMessageType MessageType => ServerMessageType.GameSelectDungeon;

    /// <summary>The refusals that are about the character rather than about a party member.</summary>
    public static SelectDungeonRefusal For(byte errorCode) => new(errorCode, 0);
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
    ///   * there is no absent-abyss cell pair. Those two bytes are a hell-party marker and a
    ///     hell-party enabled flag, both zero when neither applies -- the older client's 0xFF/0xFF
    ///     sentinel is not what A21 writes;
    ///   * a constant one follows the minimap group count. A21's builder writes it outside the
    ///     dynamic group list and its own comment declines to read it as a switch, so it is copied
    ///     rather than interpreted.
    ///
    /// The generated field table records six bytes for this type; that is where static analysis
    /// stopped, not the body's length.
    /// </remarks>
    public static byte[] EncodeDungeonInfo(DungeonLayout layout, byte difficulty)
    {
        using var stream = new MemoryStream(32);
        using var writer = new BinaryWriter(stream);
        writer.Write(layout.DungeonId);
        writer.Write(difficulty);
        writer.Write(checked((byte)layout.MazeIndex));
        writer.Write(layout.BossCell.X);
        writer.Write(layout.BossCell.Y);
        writer.Write((byte)0); // hell-party marker
        writer.Write((byte)0); // hell party not enabled
        writer.Write((byte)0);
        writer.Write((byte)0); // no minimap groups
        writer.Write((byte)1); // the constant after the group list
        writer.Write(new byte[5]);
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
        // on both of its START_MAP frames. It is the ordinary-dungeon marker; the older client's
        // zero was an abyss-mode reading that A21 does not share.
        writer.Write((byte)2);
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
            var packetIndex = actors[index].IsAiCharacter
                ? aiCharactersWritten++
                : monstersWritten++;

            writer.Write((ushort)0); // template order
            writer.Write(packetIndex);
            writer.Write(unchecked((ushort)(firstActorSequence + index)));
            writer.Write(actor.Code);
            // A map declaring level zero is declining to set one, not setting zero. What the
            // monster is then worth is the dungeon's own [basis level], which the map cannot carry
            // because it is shared between dungeons. Dungeon 144 declares zero on every monster it
            // has, so reading the zero literally made a whole dungeon's monsters level one.
            writer.Write(layout.LevelOf(actor.Level));
            writer.Write(actor.ActorType);
            writer.Write((byte)0); // first flag
            writer.Write((byte)0); // second flag
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

        writer.Write((byte)0); // abyss fog
        writer.Write((byte)0); // no ridable object groups
        writer.Write((byte)0xFF); // party member index, the builder's default for a run of one
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
            GameProtocol.SelectDungeonType,
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
/// run aggregate and the party that owns entry arrive after that answer. So does the charging of
/// what an entry costs: the cost is reported here, to a fatigue seam that keeps no account of it.
///
/// No CMD reply. The reference server's success path produces none -- it delegates straight to the
/// run's lifecycle and pushes notifications -- and registration is not an obligation to reply; see
/// protocol/patterns/game-receive.md.
/// </remarks>
public sealed class SelectDungeonHandler(
    ICharacterDomainService characters,
    ICharacterStateProjectionDomainService currentState,
    ISelectedSessionDomainService selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IPartyService parties,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveryRegistry,
    IDungeonEntryIntentDomainService entryIntents,
    IDungeonVisitDomainService visits,
    ICharacterQuestLogDomainService questLogs,
    ICharacterFatigueDomainService fatigue,
    IGameMessageSender sender,
    ILogger<SelectDungeonHandler> logger) : ClientMessageHandler<SelectDungeonRequest>
{
    /// <summary>What one entry reports as spent.</summary>
    /// <remarks>
    /// <b>No dungeon declares a cost, so this is not read off one.</b> The archive says only
    /// whether a dungeon charges fatigue at all -- its <c>[no fatigue]</c> tag, which reaches this
    /// server as <see cref="DungeonDefinition.ConsumesFatigue"/> and which nothing in the server
    /// reads: the only reader is the test pinning the parse. Neither
    /// <see cref="DungeonDefinition"/> nor <see cref="DungeonLayout"/> carries a number of points,
    /// and no other content or table holds one either.
    /// <para>
    /// What there is, is the charge itself: a point for each newly entered room that holds
    /// monsters (<c>Docs/DungeonEntryNotes.md</c> section 8, dictated 2026-08-27). An entry walks
    /// into exactly one room, the run's first, so an entry's cost is that one point.
    /// </para>
    /// <para>
    /// Three things that would change it are deliberately not asked here, because each belongs to
    /// the fatigue system rather than to this frame: whether that first room holds any monsters,
    /// whether the character has stood in it before, and whether this dungeon charges at all. Until
    /// then this is the ordinary case stated plainly rather than a computation, and it is a
    /// placeholder in the same sense as the seam it is reported to -- what changes when fatigue is
    /// built is what this number is worked out from, not that a report happens here.
    /// </para>
    /// </remarks>
    public const ushort FatigueAnEntryCosts = 1;

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

        var intent = entryIntents.Consume(session);
        if (intent is null
            || (intent.Value.Kind == DungeonEntryIntentKind.Directed
                && intent.Value.DirectedDungeonId != message.DungeonId))
        {
            logger.LogWarning(
                "Select dungeon {ConnectionId}: invalid selection context intent={Intent} dungeon={DungeonId}",
                context.Connection.ConnectionId,
                intent,
                message.DungeonId);
            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [SelectDungeonRefusal.For(SelectDungeonRefusal.InvalidSelectionState)], []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        // Gathered here and handed over, so the rule stays a function of what it is given. They
        // are asked of the character's own log rather than read out of the session, because the
        // session's copy is whatever the entry batch put there and nothing refreshes it: a quest
        // taken on or handed in since the character entered the world would leave the maze chosen
        // against the state the player has already moved on from. Which quests they are is still
        // decided once, here, and the layout stays fixed to the run it opens.
        var questLog = questLogs.LogOf(session.AccountId, session.CharacterId);
        var carried = await questLog.CarriedAsync(cancellationToken).ConfigureAwait(false);
        var cleared = await questLog.ClearedAsync(cancellationToken).ConfigureAwait(false);
        var facts = new DungeonEntryFacts(
            message.Difficulty,
            carried.Select(quest => quest.QuestId).ToArray(),
            cleared.Select(quest => quest.QuestId).ToArray(),
            intent.Value);

        var party = intent.Value.Kind == DungeonEntryIntentKind.Directed
            ? parties.FindParty(session.CharacterId)
            : null;
        if (party is not null && party.LeaderWireUserId != session.WireUserId)
        {
            await RefuseAsync(
                context.Connection.ConnectionId,
                SelectDungeonRefusal.NotPartyLeader,
                context: 0,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var attachments = ResolveAttachments(session, result, party);
        if (attachments.BlockedSlot is { } blockedSlot)
        {
            await RefuseAsync(
                context.Connection.ConnectionId,
                SelectDungeonRefusal.MemberBlocked,
                blockedSlot,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var participants = attachments.Members
            .Select(value => new DungeonRunParticipant(
                value.Session.AccountId,
                value.Session.CharacterId,
                value.Session.WireUserId,
                value.Slot,
                value.Session.WireUserId == session.WireUserId))
            .ToArray();
        var previousRuns = participants.ToDictionary(
            participant => participant.CharacterId,
            participant => runs.Find(participant.CharacterId));
        var entered = await visits.EnterPartyAsync(
            participants, message.DungeonId, message.Difficulty, facts, cancellationToken)
            .ConfigureAwait(false);
        if (entered is null)
        {
            logger.LogWarning(
                "Select dungeon {ConnectionId}: the archive declares no enterable dungeon "
                    + "{DungeonId}, refusing",
                context.Connection.ConnectionId,
                message.DungeonId);
            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [SelectDungeonRefusal.For(SelectDungeonRefusal.DungeonNotFound)], []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var layout = entered.Layout;
        var enteredRunId = entered.Visits[session.CharacterId].Run.RunId;
        foreach (var participant in participants)
        {
            if (previousRuns[participant.CharacterId] is { } previous
                && previous.RunId != enteredRunId)
            {
                deliveryRegistry.RemoveParticipant(previous.RunId, participant.CharacterId);
            }
        }

        // The directory is generation-safe but mutable. Recheck after the domain operation and
        // before registering or sending anything; if one member was replaced during layout
        // resolution, tear the whole run back down rather than leaving a split entry behind.
        if (attachments.Members.Any(value =>
                selectedSessions.FindByCharacter(value.Session.CharacterId) != value.Session))
        {
            RollbackEntry(entered.Visits[session.CharacterId].Run.RunId, participants);
            if (selectedSessions.FindByCharacter(session.CharacterId) == session)
            {
                await RefuseAsync(
                    context.Connection.ConnectionId,
                    SelectDungeonRefusal.MemberBlocked,
                    context: 0,
                    cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        var leaderVisit = entered.Visits[session.CharacterId];
        try
        {
            deliveryRegistry.Register(
                leaderVisit.Run.RunId,
                attachments.Members.Select(value => value.Session).ToArray());

            // Reported once the entry has actually happened, so that a refused selection reports
            // nothing. What the seam does with it is nothing at all today -- see
            // ICharacterFatigueDomainService -- which is why this is the report and not a check:
            // there is no balance to be short of.
            foreach (var attachment in attachments.Members)
            {
                await fatigue
                    .SpendAsync(
                        attachment.Session.CharacterId,
                        FatigueAnEntryCosts,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            logger.LogInformation(
                "Select dungeon {ConnectionId} user={WireUserId}: run={RunId} party={PartyMemberCount} "
                    + "dungeon={DungeonId} difficulty={Difficulty} intent={Intent} maze={MazeIndex} "
                    + "purpose={Purpose} start=({StartX},{StartY}) "
                    + "room={RoomId} boss=({BossX},{BossY}) seed={Seed}",
                context.Connection.ConnectionId,
                session.WireUserId,
                leaderVisit.Run.RunId.Value,
                leaderVisit.Run.PartyMemberCount,
                layout.DungeonId,
                message.Difficulty,
                intent.Value,
                layout.MazeIndex,
                layout.Purpose,
                layout.StartCell.X,
                layout.StartCell.Y,
                layout.StartRoomMapId,
                layout.BossCell.X,
                layout.BossCell.Y,
                layout.Seed);

            // A21's order, which is not the one this used to send. Its answer opens with a second
            // acceptance byte on CMD 0x000F, then the selection state that used to be sent when the
            // screen opened, then the support roster, and only then the run's shape and the room.
            // The older order -- describe the run, place the character, close with the roster --
            // was the A12 reference's. The selection state is sent once. For everyone but the
            // tutorial it went out when the screen opened; the tutorial's first entry defers it to
            // here, which is where the captured session shows it and why this used to send it
            // unconditionally.
            var deliveries = new List<(
                SelectedSessionIdentity Session,
                IReadOnlyList<IGameServerMessage> Frames)>();
            foreach (var attachment in attachments.Members)
            {
                var memberSession = attachment.Session;
                var visit = entered.Visits[memberSession.CharacterId];
                var frames = new List<IGameServerMessage>();
                if (memberSession != session && entryIntents.Consume(memberSession) is null)
                {
                    var refreshed = await currentState
                        .RefreshAsync(attachment.Runtime, cancellationToken)
                        .ConfigureAwait(false);
                    frames.AddRange(SelectionPrelude(memberSession, refreshed));
                }

                frames.Add(new EnterSelectDungeonAcknowledgement());
                var currentProgress = await characters
                    .Get(memberSession.CharacterId)
                    .ProgressAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (currentProgress is null)
                {
                    RollbackEntry(leaderVisit.Run.RunId, participants);
                    return;
                }
                if (visits.DefersSelectionState(currentProgress.Level))
                    frames.Add(new EnterSelectDungeonNotification(memberSession.WireUserId));

                frames.Add(new SupportTagNotification());
                frames.Add(new DungeonInfoNotification(layout, message.Difficulty));
                frames.Add(new StartMapNotification(
                    visit.Run,
                    FirstEntry: true,
                    visit.Actors,
                    visit.FirstActorSequence,
                    FloorPieceValues.Describe(visit.Run, visit.FloorDrops)));
                deliveries.Add((memberSession, frames));
            }

            // Every awaited projection above is allowed to run user code and database I/O. The
            // frozen generations therefore need one final validation at the actual delivery
            // boundary, not merely after the run was created. A replacement receives no frame
            // from this run and makes the all-or-nothing entry roll back.
            if (attachments.Members.Any(value =>
                    selectedSessions.FindByCharacter(value.Session.CharacterId) != value.Session))
            {
                RollbackEntry(leaderVisit.Run.RunId, participants);
                if (selectedSessions.FindByCharacter(session.CharacterId) == session)
                {
                    await RefuseAsync(
                        context.Connection.ConnectionId,
                        SelectDungeonRefusal.MemberBlocked,
                        context: 0,
                        cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            var direct = deliveries.Single(value => value.Session == session);
            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    direct.Frames,
                    [.. deliveries
                        .Where(value => value.Session != session)
                        .Select(value => new GameExplicitMessageTarget(
                            value.Session.ConnectionId,
                            value.Frames))]),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RollbackEntry(leaderVisit.Run.RunId, participants);
            throw;
        }
    }

    private void RollbackEntry(
        DungeonRunId runId,
        IReadOnlyList<DungeonRunParticipant> participants)
    {
        foreach (var participant in participants)
        {
            runs.End(participant.CharacterId, runId);
            deliveryRegistry.RemoveParticipant(runId, participant.CharacterId);
        }
    }

    private EntryAttachments ResolveAttachments(
        SelectedSessionIdentity requester,
        SelectCharacterResult requesterRuntime,
        PartySnapshot? party)
    {
        if (party is null)
            return new EntryAttachments([(requester, requesterRuntime, (byte)0)], null);

        var members = new List<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)>();
        foreach (var member in party.Members.OrderBy(value => value.Slot))
        {
            var current = selectedSessions.FindByCharacter(member.CharacterId);
            if (current is null
                || current.AccountId != member.Character.AccountId
                || current.WireUserId != member.WireUserId
                || !runtimeSessions.TryGet(current, out var runtime, out _)
                || runtime is null)
            {
                return new EntryAttachments([], member.Slot);
            }

            if (members.Any(value =>
                    value.Session.CharacterId == current.CharacterId
                    || value.Session.WireUserId == current.WireUserId
                    || value.Session.ConnectionId == current.ConnectionId))
            {
                return new EntryAttachments([], member.Slot);
            }

            members.Add((current, runtime, member.Slot));
        }

        if (!members.Any(value => value.Session == requester))
            return new EntryAttachments([], 0);
        return new EntryAttachments(members, null);
    }

    private IReadOnlyList<IGameServerMessage> SelectionPrelude(
        SelectedSessionIdentity session,
        SelectCharacterResult result)
    {
        var equipment = result.CharacterInventory.Containers
            .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)
            ?.Items ?? [];
        List<IGameServerMessage> frames =
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
        if (!visits.DefersSelectionState(result.Character.Progression.Level))
            frames.Add(new EnterSelectDungeonNotification(session.WireUserId));
        return frames;
    }

    private ValueTask RefuseAsync(
        NetworkConnectionId connectionId,
        byte errorCode,
        byte context,
        CancellationToken cancellationToken) =>
        sender.SendExplicitAsync(
            connectionId,
            new GameExplicitMessageDelivery([new SelectDungeonRefusal(errorCode, context)], []),
            cancellationToken);

    private sealed record EntryAttachments(
        IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)> Members,
        byte? BlockedSlot);
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
    ISelectedSessionDomainService selectedSessions,
    IDungeonRunDomainService runs,
    IDungeonVisitDomainService visits,
    DungeonRunDeliveryRegistry deliveryRegistry,
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

        // The room decides, not the run. A boss room has no way back out of it whatever state the
        // run is in; see DungeonRoomKind.
        if (!current.MayLeaveCurrentRoom)
        {
            logger.LogWarning(
                "Move map {ConnectionId}: the {Kind} room at ({FromX},{FromY}) may not be left, "
                    + "refusing ({X},{Y})",
                context.Connection.ConnectionId,
                current.CurrentRoomKind,
                current.CurrentCell.X,
                current.CurrentCell.Y,
                message.NextX,
                message.NextY);
            return;
        }

        var target = new MazeCell(message.NextX, message.NextY);
        // FindByConnection identifies who sent this frame; it does not prove that generation is
        // still the character's published generation. Validate at the mutation boundary so a
        // superseded connection cannot advance the shared aggregate.
        if (selectedSessions.FindByCharacter(session.CharacterId) != session)
            return;

        var visit = await visits
            .MoveAsync(session.CharacterId, current, target, cancellationToken)
            .ConfigureAwait(false);
        if (visit is null)
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
            visit.Run.CurrentCell.X,
            visit.Run.CurrentCell.Y,
            visit.Run.CurrentRoomMapId,
            visit.FirstEntry,
            message.PathPositionX,
            message.PathPositionY);

        // A room walked into a second time is described as empty. The reference server has a
        // separate revisit builder that writes no actors and no drops at all, and it is right to:
        // the client already killed what was in there and still holds those identities. Sending
        // the list again resurrects them under the very same numbers -- which is what a live
        // session saw on 2026-08-26 when it walked back out of the boss room, five monsters and
        // four monsters standing up again with the numbers they had died under.
        var room = new StartMapNotification(
            visit.Run,
            visit.FirstEntry,
            visit.Actors,
            visit.FirstActorSequence,
            FloorPieceValues.Describe(visit.Run, visit.FloorDrops));
        var frozen = deliveryRegistry.Find(visit.Run.RunId);
        if (frozen.Count == 0)
            frozen = [session];
        var currentRecipients = frozen
            .Where(value => selectedSessions.FindByCharacter(value.CharacterId) == value)
            .ToArray();
        if (currentRecipients.Length == 0)
            return;

        // If the requester was replaced while room projection was awaited, the room has already
        // advanced. Do not send to either requester generation, but do keep the remaining frozen
        // participants in sync with the committed shared room.
        var required = currentRecipients.Contains(session)
            ? session
            : currentRecipients[0];

        await sender.SendExplicitAsync(
            required.ConnectionId,
            new GameExplicitMessageDelivery(
                [room],
                [.. currentRecipients
                    .Where(value => value != required)
                    .Select(value => new GameExplicitMessageTarget(
                        value.ConnectionId,
                        [room]))]),
            cancellationToken).ConfigureAwait(false);
    }
}
