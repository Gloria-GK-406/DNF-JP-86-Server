using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.TutorialLevelUp;

/// <summary>
/// CMD 0x01E4 <c>TUTORIAL_LEVEL_UP</c>: the tutorial asking for the level it promised.
/// </summary>
/// <remarks>
/// The body is empty. Everything the decision needs -- who is asking, what level they are, whether
/// they are in a dungeon -- the server already knows, so the frame carries nothing but the ask.
/// <para>
/// Observed live on 2026-08-26, in the boss room, and answered with nothing: the server did not
/// know this type and logged an unhandled frame. This is the one place in the tutorial where a
/// character actually gains a level; the kills before it report the character's standing without
/// changing it.
/// </para>
/// </remarks>
public sealed record TutorialLevelUpRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameTutorialLevelUp;
}

/// <summary>The CMD acknowledgement under the same type: one byte, granted or refused.</summary>
public sealed record TutorialLevelUpResponse(byte Result) : IGameCmdMessage
{
    /// <summary>Granted.</summary>
    public const byte Granted = 0x01;

    /// <summary>
    /// Refused, which the reference server answers for every reason it has.
    /// </summary>
    /// <remarks>
    /// It does not distinguish "already past level one" from "not in a dungeon" on the wire, and
    /// neither does this: the tutorial asks once, and a second ask means the two sides disagree
    /// about where the character is rather than that the player did something.
    /// </remarks>
    public const byte Refused = 0x13;

    public ServerMessageType MessageType => ServerMessageType.GameTutorialLevelUp;
}

internal sealed class TutorialLevelUpDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new TutorialLevelUpRequest(metadata);
}

internal sealed class TutorialLevelUpResponseEncoder : GameCmdMessageEncoder<TutorialLevelUpResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        TutorialLevelUpResponse message) =>
        OnePayload(GameProtocol.TutorialLevelUpType, new byte[] { message.Result });
}

/// <summary>Why the level was not granted, in the server's own terms.</summary>
public enum TutorialLevelUpStatus
{
    Granted = 0,

    /// <summary>The character is already past the level the tutorial hands out.</summary>
    AlreadyPast,

    /// <summary>The character is not in a dungeon run.</summary>
    NotInARun,

    /// <summary>No published session for the connection that asked.</summary>
    NoSession,

    /// <summary>The archive's experience table has no threshold for the level below the target.</summary>
    NoThreshold,

    /// <summary>The character's row did not take the new level.</summary>
    NotPersisted,
}

/// <summary>What the character stands at afterwards.</summary>
public sealed record TutorialLevelUpResult(
    TutorialLevelUpStatus Status, byte Level, uint Experience)
{
    public static TutorialLevelUpResult Refused(TutorialLevelUpStatus status) =>
        new(status, 0, 0);
}

/// <summary>
/// Grants the tutorial's level and tells the client what the character now stands at.
/// </summary>
/// <remarks>
/// Three frames, in the reference server's order: the experience frame carrying the new level and
/// total, then the acknowledgement. The reference sends two more between them -- the quest list for
/// the new level and the character's own state -- and this does not yet send the latter. The
/// omission is a protocol gap rather than a reason to retain a mutable session snapshot. What
/// is sent carries the level, so the client learns it; what is not sent is written down in
/// <c>Docs/ProtocolScaffoldDissonances.md</c> rather than left to be rediscovered.
/// </remarks>
public sealed class GameTutorialLevelUpCommandHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterDomainService characters,
    IDungeonRunQueryDomainService runs,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    IQuestOfferRefreshDomainService offers,
    ILogger<GameTutorialLevelUpCommandHandler> logger) : ICommandHandler<GameTutorialLevelUpCommand,bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context,
        GameTutorialLevelUpCommand command,
        CancellationToken cancellationToken)
    {
        var dispatch=command.Dispatch;
        var session = selectedSessions.FindByConnection(dispatch.Connection.ConnectionId);
        var result = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || result is null)
        {
            logger.LogWarning(
                "Tutorial level up {ConnectionId}: no published session, refusing",
                dispatch.Connection.ConnectionId);
            await this.RefuseAsync(dispatch, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // In a dungeon, as the reference server requires. The tutorial asks for this from inside
        // its own run, and a request from town is the two sides disagreeing about where the
        // character is.
        if (runs.Find(session.CharacterId) is null)
        {
            logger.LogInformation(
                "Tutorial level up {ConnectionId} character={CharacterId}: not in a run, refusing",
                dispatch.Connection.ConnectionId,
                session.CharacterId.Value);
            await this.RefuseAsync(dispatch, cancellationToken).ConfigureAwait(false);
            return true;
        }

        var granted = await units.WriteAsync(context,new("TutorialLevelUp",[]),
            (_,token)=>GrantAsync(characters,session.CharacterId,token),cancellationToken).ConfigureAwait(false);

        if (granted.Status is not TutorialLevelUpStatus.Granted)
        {
            logger.LogInformation(
                "Tutorial level up {ConnectionId} character={CharacterId}: refused as {Status}",
                dispatch.Connection.ConnectionId,
                session.CharacterId.Value,
                granted.Status);
            await this.RefuseAsync(dispatch, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // Read again now the grant has committed, rather than edited into what it should be.
        var updated = await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Character left before the tutorial level could be projected.");
        // The level is one of the offer list's inputs -- a quest whose band the character has just
        // entered becomes offerable at this moment -- and the list's own first byte IS the level,
        // so a session that skips this goes on drawing the old one. Worked out again rather than
        // edited, for the same reason the two quest paths do it: a level can only ADD offers.
        updated = await offers.RefreshAsync(
            updated,
            session.AccountId,
            session.CharacterId,
            granted.Level,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Tutorial level up {ConnectionId} character={CharacterId}: now level {Level} at {Exp}",
            dispatch.Connection.ConnectionId,
            session.CharacterId.Value,
            granted.Level,
            granted.Experience);

        // The reference server's own order, from its 2026-08-23 capture of the tutorial: the
        // experience frame, the quest list for the new level, then the acknowledgement. It sends
        // one more between the list and the acknowledgement -- NOTI 0x0002, the character's whole
        // state -- and this still does not. Recorded in Docs/ProtocolScaffoldDissonances.md.
        // Asked of the character, after the grant has been written. This is the one place in the
        // tutorial where a level is actually gained, so it is the frame that most needs the points
        // worked out again from the character's current facts, never an entry-time projection.
        var mirror = await characters.Get(session.CharacterId)
            .RemainingSkillPointsAsync(cancellationToken).ConfigureAwait(false);
        var frames = new List<IGameServerMessage>();
        if (mirror is not null)
        {
            frames.Add(new ExperienceNotification(
                granted.Level, granted.Experience, mirror, AccountHonorLevel.Initial,
                // The tutorial grant is a flat figure with nothing added to it, so no line is
                // declared.
                ExperienceBonusLines.None));
        }

        frames.Add(new AvailableQuestListMessage(
            granted.Level, updated.World.Content.AvailableQuests));
        frames.Add(new TutorialLevelUpResponse(TutorialLevelUpResponse.Granted));

        await outbox.RegisterExplicitAsync(
            dispatch.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private ValueTask RefuseAsync(
        NetworkDispatchContext dispatch, CancellationToken cancellationToken) =>
        outbox.RegisterExplicitAsync(
            dispatch.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new TutorialLevelUpResponse(TutorialLevelUpResponse.Refused)], []),
            cancellationToken);

    // Shared with direct algorithm fixtures; production calls it only inside the declared write.
    internal static async ValueTask<TutorialLevelUpResult> GrantAsync(ICharacterDomainService characters,CharacterId characterId,CancellationToken ct)
    {
        if(await characters.Get(characterId).ProgressAsync(ct).ConfigureAwait(false) is not {} progress)
            return TutorialLevelUpResult.Refused(TutorialLevelUpStatus.NotPersisted);
        if(progress.Level>=2) return TutorialLevelUpResult.Refused(TutorialLevelUpStatus.AlreadyPast);
        var next=await characters.Get(characterId).SetLevelAsync(2,ct).ConfigureAwait(false);
        return new(TutorialLevelUpStatus.Granted,next.Level,next.Experience);
    }
}
public sealed record GameTutorialLevelUpCommand(NetworkDispatchContext Dispatch,TutorialLevelUpRequest Input):IProtocolCommand<bool>
{
    public IClientNetworkMessage Message=>Input;
    public ProtocolFrameMetadata Frame=>new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class TutorialLevelUpHandler(ICommandBus commands):ClientMessageHandler<TutorialLevelUpRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameTutorialLevelUpCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,TutorialLevelUpRequest message,CancellationToken ct)=>
        _ = await commands.SendAsync(new GameTutorialLevelUpCommand(context,message),ct).ConfigureAwait(false);
}
