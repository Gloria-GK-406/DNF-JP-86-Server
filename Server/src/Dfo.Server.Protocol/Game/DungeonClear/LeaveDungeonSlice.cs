using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DungeonClear;

/// <summary>
/// CMD 0x0048 <c>EPLP_COMMAND</c>: the button that ends a run and walks the character out.
/// </summary>
/// <remarks>
/// Two bytes, a state and an option, and this is how a finished dungeon is actually left. Not
/// <c>GIVEUP_GAME</c>: a complete reference session captured on 2026-08-27 -- boss killed, result
/// screen, card flipped, character back in town -- contains no <c>0x002A</c> anywhere. It contains
/// this, twice, and the town state follows the second one.
/// <para>
/// This server had no such type until 2026-08-29, which is what a player meant by reporting that
/// returning to town from a dungeon did not work: the button reached a server that never answered,
/// so nothing moved and the client eventually rebuilt its whole session from the login handshake.
/// </para>
/// </remarks>
public sealed record EplpCommandRequest(GameMessageMetadata Metadata, byte State, byte Option)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameEplpCommand;
}

/// <summary>
/// The acknowledgement under the same type: a success byte and the two the request carried.
/// </summary>
/// <remarks>
/// The shape is the client's own, not the reference's. Its handler for this reply reads the
/// envelope's first byte as the success flag and then two more with <c>ReadU8</c>, and only takes
/// the branch that leaves the dungeon when the first of those two is 1 -- so the state has to come
/// back, and it has to come back as the client sent it. Echoing is not a convention here; a
/// different value in that byte is a different instruction.
/// </remarks>
public sealed record EplpCommandResponse(byte State, byte Option) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEplpCommand;
}

internal sealed class EplpCommandDecoder : IGameMessageDecoder
{
    /// <summary>A state and an option; the client's builder writes exactly these two.</summary>
    public const int BodyLength = 2;

    /// <summary>
    /// Declines a body too short to carry both, rather than throwing.
    /// </summary>
    /// <remarks>
    /// Throwing is rethrown by <c>NetworkPlugins.Decode</c> and takes the connection with it. On
    /// this frame that would be the worst possible moment: the player has pressed the button that
    /// leaves a finished dungeon, and the answer to a malformed press must not be to drop them.
    /// </remarks>
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length < BodyLength ? null : new EplpCommandRequest(metadata, body[0], body[1]);
}

internal sealed class EplpCommandResponseEncoder : GameCmdMessageEncoder<EplpCommandResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        EplpCommandResponse message) =>
        OnePayload(
            GameProtocol.EplpCommandType,
            new byte[] { 0x01, message.State, message.Option });
}

/// <summary>
/// Answers the button, and when it is the one that leaves, puts the character back in town.
/// </summary>
/// <remarks>
/// Ordinary state 1 options 0/1/2 exit. Option 3 is a separate confirmation: the native story
/// button first sends an exit option, then this confirmation. Once the source run is gone the
/// confirmation must not acknowledge another choice or rebuild the town a second time.
/// Explicit option 2 retains its no-run town-return compatibility. Non-exit states only ACK.
/// Specialized tower and altar paths retain their own settlement semantics.
/// </remarks>
public sealed class EplpCommandHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterPositionDomainService positions,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveries,
    IGameMessageSender sender,
    TownAreaProjectionDelivery townAreaProjection,
    ILogger<EplpCommandHandler> logger,
    SelectedSessionTransitionGate transitionGate,
    DungeonCardPresentationCoordinator? cardPresentation = null,
    IPartyService? parties = null,
    IllusionTowerProjectionDelivery? illusionTower = null,
    Dfo.Server.Protocol.Game.Altar.AltarProtocolCoordinator? altars = null,
    DungeonProjectionGate? projectionGate = null, BossTowerProjectionDelivery? bossTower = null,
    IDungeonEntryIntentDomainService? entryIntents = null, ICommandBus? commands = null,
    ITownAreaDomainService? townAreas = null)
    : ClientMessageHandler<EplpCommandRequest>
{
    /// <summary>The state that means "take me out of here".</summary>
    public const byte LeaveState = 1;

    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        EplpCommandRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is not null && bossTower is not null
            && await bossTower.SelectReturnAsync(context.Connection.ConnectionId, message.Metadata,
                message.State, message.Option, cancellationToken).ConfigureAwait(false)) return;
        if (session is not null && illusionTower is not null
            && await illusionTower.SelectReturnAsync(
                context.Connection.ConnectionId, message.Metadata,
                message.State, message.Option, cancellationToken).ConfigureAwait(false))
            return;
        // Capture the source before loading any asynchronous character view.
        var currentRun = session is null ? null : runs.Find(session.CharacterId);
        // Where they are standing is asked of the service that owns it. It is recorded and dropped
        // with the published session, so this conjunct admits exactly the connections the two
        // before it do.
        var result = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || result is null
            || positions.Find(session.CharacterId) is not { } position)
        {
            logger.LogWarning(
                "Eplp command {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        var isAltar = currentRun?.Layout.Altar is not null;
        var leaving = message.State == LeaveState && (isAltar || message.Option <= 2);
        if (message.State == LeaveState && currentRun is null && message.Option != 2)
        {
            logger.LogDebug("Eplp ignored after exit {ConnectionId}: state={State} option={Option} sequence={Sequence} ordinal={Ordinal}",
                context.Connection.ConnectionId, message.State, message.Option, message.Metadata.Sequence, message.Metadata.InboundOrdinal);
            return;
        }
        var roster = currentRun is not null && parties is not null
            ? parties.FindParty(new PartyCharacterSnapshot(
                session.AccountId, session.CharacterId, session.WireUserId, null))
            : null;
        var leaderWireUserId = roster?.LeaderWireUserId ?? session.WireUserId;
        var currentRunId = currentRun?.RunId;
        var participants = roster is not null
            ? roster.Members.OrderBy(value => value.Slot)
                .Select(value => new DungeonRunParticipant(
                    value.Character.AccountId, value.CharacterId, value.Slot,
                    value.WireUserId == leaderWireUserId))
                .Where(value => runs.Find(value.CharacterId)?.RunId == currentRunId)
                .ToArray()
            : currentRun is { Participants.Count: > 0 }
                ? currentRun.Participants.OrderBy(value => value.Slot).ToArray()
                : [new DungeonRunParticipant(session.AccountId, session.CharacterId, 0, true)];
        var leader = participants.SingleOrDefault(value => value.IsLeader);
        if (leader is null || leader.CharacterId != session.CharacterId)
        {
            logger.LogWarning(
                "Eplp command {ConnectionId}: non-leader character={CharacterId} ignored",
                context.Connection.ConnectionId, session.CharacterId.Value);
            return;
        }

        var returning = new List<ReturnMember>();
        foreach (var participant in participants)
        {
            var memberSession = selectedSessions.FindByCharacter(participant.CharacterId);
            var memberRuntime = memberSession is null ? null
                : await currentViews.TryLoadAsync(memberSession, cancellationToken).ConfigureAwait(false);
            if (memberSession is null
                || memberSession.AccountId != participant.AccountId
                || memberRuntime is null
                || positions.Find(participant.CharacterId) is not { } memberPosition
                || (currentRun is not null
                    && runs.Find(participant.CharacterId)?.RunId != currentRun.RunId))
            {
                continue;
            }
            returning.Add(new ReturnMember(memberSession, memberRuntime, memberPosition));
        }
        if (!returning.Any(value => value.Session == session))
            return;

        await using var altarProjection = leaving && isAltar
            ? await (projectionGate ?? throw new InvalidOperationException("Altar projection gate is unavailable."))
                .AcquireAsync(currentRun!.RunId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false)
            : null;
        // Keep the same lifecycle boundary through ACK and all town frames. ENTER_SELECT and
        // the entry command participate in this boundary too.
        await using var transitions = message.State == LeaveState
            ? await transitionGate.AcquireManyExclusiveAsync(
                participants.Select(x => x.AccountId), cancellationToken).ConfigureAwait(false)
            : null;
        if (message.State == LeaveState)
        {
            if (selectedSessions.FindByConnection(context.Connection.ConnectionId) != session
                || runs.Find(session.CharacterId)?.RunId != currentRun?.RunId
                || returning.Any(x => selectedSessions.FindByCharacter(x.Session.CharacterId) != x.Session)
                || (!isAltar && returning.Any(x => entryIntents?.Find(x.Session) is not null)))
                return;
            if (roster is not null && parties is not null)
            {
                var latest = parties.FindParty(new PartyCharacterSnapshot(
                    session.AccountId, session.CharacterId, session.WireUserId, null));
                if (latest.Generation != roster.Generation || latest.Revision != roster.Revision
                    || returning.Count != roster.Members.Length)
                    return;
            }
            if (currentRun is null && townAreas is not null
                && (townAreas.Find(session.CharacterId) is not { } member || !session.Owns(member)))
                return;
        }

        var runEnded = false;
        HashSet<CharacterId> endedCharacters = [];
        if (leaving)
        {
            if (isAltar)
                await (altars ?? throw new InvalidOperationException("Altar coordinator is unavailable."))
                    .SettleBeforeExitUnderGatesAsync(session, currentRun!.RunId, cancellationToken).ConfigureAwait(false);

            if (currentRun is not null)
            {
                foreach (var member in returning)
                    cardPresentation?.Cancel(member.Session.CharacterId, currentRun.RunId);
                var command = new LeaveDungeonCommand(session,
                    returning.Select(x => x.Session).ToArray(), currentRun.RunId);
                var ended = commands is not null
                    ? await commands.SendAsync(command, cancellationToken).ConfigureAwait(false)
                    : await new LeaveDungeonCommandHandler(runs).HandleAsync(
                        new(Guid.NewGuid(), typeof(LeaveDungeonCommand), typeof(LeaveDungeonCommandHandler),
                            CommandExecutionKind.Direct), command, cancellationToken).ConfigureAwait(false);
                foreach (var character in ended.EndedCharacters)
                {
                    deliveries.RemoveParticipant(currentRun.RunId, character);
                    endedCharacters.Add(character);
                }
                runEnded = endedCharacters.Count > 0;
            }
            if (currentRun is not null && !runEnded)
                return;
            if (currentRun is not null)
            {
                returning.RemoveAll(value => !endedCharacters.Contains(value.Session.CharacterId));
                if (!returning.Any(value => value.Session == session))
                    return;
                // Native retry follows the exit with 000F(dungeonId). Retain the ordinary
                // maze partition rather than treating that nonzero target as a story request.
                if (message.Option == 0 && !isAltar
                    && currentRun.Layout is { Purpose.Kind: DungeonRunPurposeKind.Ordinary,
                        EntryKind: DungeonEntryKind.Normal, Mirror: null, Sao: null, Despair: null,
                        Tournament: null })
                    foreach (var member in returning)
                        entryIntents?.RestoreIfAbsent(member.Session,
                            DungeonEntryIntent.OrdinaryRechallenge(currentRun.Layout.DungeonId));
            }
        }

        logger.LogInformation(
            "Eplp command {ConnectionId} character={CharacterId}: state={State} option={Option}; "
                + "leaving={Leaving} runEnded={RunEnded} sourceRun={RunId} sequence={Sequence} ordinal={Ordinal}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            message.State,
            message.Option,
            leaving,
            runEnded, currentRun?.RunId, message.Metadata.Sequence, message.Metadata.InboundOrdinal);

        // Peers must see the decision before the leader's ACK can open another selection.
        foreach (var member in returning.Where(value => value.Session != session))
        {
            if (selectedSessions.FindByCharacter(member.Session.CharacterId) != member.Session)
                continue;
            try
            {
                await sender.SendExplicitAsync(
                    member.Session.ConnectionId,
                    new GameExplicitMessageDelivery(
                        [new EplpCommandResponse(message.State, message.Option)], []),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(error,
                    "Eplp command {ConnectionId}: peer decision delivery failed for character={CharacterId}",
                    context.Connection.ConnectionId, member.Session.CharacterId.Value);
            }
        }
        if (selectedSessions.FindByCharacter(session.CharacterId) != session)
            return;
        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new EplpCommandResponse(message.State, message.Option)], []),
            cancellationToken).ConfigureAwait(false);
        if (leaving)
        {
            await townAreaProjection.EnterAsync(
                returning.Where(value => selectedSessions.FindByCharacter(
                        value.Session.CharacterId) == value.Session)
                    .Select(value => new TownAreaEntry(
                        value.Session, value.Runtime, value.Position))
                    .ToArray(),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record ReturnMember(
        SelectedSessionIdentity Session,
        SelectCharacterResult Runtime,
        CharacterPositionState Position);
}
