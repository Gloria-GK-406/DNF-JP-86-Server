using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Abstractions.Content;
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

/// <summary>One character being put where the tutorial says it should be by now.</summary>
public sealed record GrantTutorialLevelCommand(CharacterId CharacterId, byte CurrentLevel)
    : ICommand<TutorialLevelUpResult>;

/// <summary>
/// Sets the character to the level the tutorial ends at, and the experience that level begins with.
/// </summary>
/// <remarks>
/// A set, not a gain. The reference server writes the target level and the threshold that level
/// starts at rather than adding anything, so a character who reaches this step having earned
/// nothing and one who reaches it having earned something both leave it standing in the same
/// place -- which is what makes the tutorial's ending the same for everybody.
/// <para>
/// The threshold comes from the archive's own <c>character/ExpTable.tbl</c>, the same table the
/// dungeon settlement reads. Entry <c>n-1</c> is what a character must hold to be level <c>n</c>,
/// so reaching level two means holding the first entry.
/// </para>
/// </remarks>
public sealed class GrantTutorialLevelCommandHandler(ICharacterExperienceDomainService grant)
    : ICommandHandler<GrantTutorialLevelCommand, TutorialLevelUpResult>
{
    /// <summary>Where the tutorial leaves a character, as the reference server names it.</summary>
    public const byte TargetLevel = 2;

    public async ValueTask<TutorialLevelUpResult> HandleAsync(
        CommandExecutionContext context,
        GrantTutorialLevelCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CurrentLevel >= TargetLevel)
            return TutorialLevelUpResult.Refused(TutorialLevelUpStatus.AlreadyPast);

        if (grant.ThresholdForLevel(TargetLevel) is not { } target)
            return TutorialLevelUpResult.Refused(TutorialLevelUpStatus.NoThreshold);
        if (!await grant.TrySetProgressAsync(
                command.CharacterId, TargetLevel, target, cancellationToken).ConfigureAwait(false))
        {
            return TutorialLevelUpResult.Refused(TutorialLevelUpStatus.NotPersisted);
        }

        return new TutorialLevelUpResult(TutorialLevelUpStatus.Granted, TargetLevel, target);
    }
}

/// <summary>
/// Grants the tutorial's level and tells the client what the character now stands at.
/// </summary>
/// <remarks>
/// Three frames, in the reference server's order: the experience frame carrying the new level and
/// total, then the acknowledgement. The reference sends two more between them -- the quest list for
/// the new level and the character's own state -- and this does not: both are re-projections of the
/// whole published entry state, and the session's snapshot has no partial-update path yet. What
/// is sent carries the level, so the client learns it; what is not sent is written down in
/// <c>Docs/ProtocolScaffoldDissonances.md</c> rather than left to be rediscovered.
/// <para>
/// The published session is corrected before the frames go out. It is where every later frame
/// reads the character's level from -- the kill acknowledgement among them -- so a session left
/// saying level one would go on reporting level one for the rest of the run.
/// </para>
/// </remarks>
public sealed class TutorialLevelUpHandler(
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IDungeonRunDomainService runs,
    ICommandBus commandBus,
    IGameMessageSender sender,
    IQuestOfferRefreshDomainService offers,
    ILogger<TutorialLevelUpHandler> logger) : ClientMessageHandler<TutorialLevelUpRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        TutorialLevelUpRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null
            || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null)
        {
            logger.LogWarning(
                "Tutorial level up {ConnectionId}: no published session, refusing",
                context.Connection.ConnectionId);
            await this.RefuseAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        // In a dungeon, as the reference server requires. The tutorial asks for this from inside
        // its own run, and a request from town is the two sides disagreeing about where the
        // character is.
        if (runs.Find(session.CharacterId) is null)
        {
            logger.LogInformation(
                "Tutorial level up {ConnectionId} character={CharacterId}: not in a run, refusing",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            await this.RefuseAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        var granted = await commandBus.SendAsync(
            new GrantTutorialLevelCommand(
                session.CharacterId, result.Character.Progression.Level),
            cancellationToken).ConfigureAwait(false);

        if (granted.Status is not TutorialLevelUpStatus.Granted)
        {
            logger.LogInformation(
                "Tutorial level up {ConnectionId} character={CharacterId}: refused as {Status}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                granted.Status);
            await this.RefuseAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        var updated = result with
        {
            Character = result.Character with
            {
                Progression = new CharacterProgressionState(granted.Level, granted.Experience),
            },
            CharacterEntry = result.CharacterEntry with
            {
                Public = result.CharacterEntry.Public with
                {
                    Extras = result.CharacterEntry.Public.Extras with
                    {
                        CharacterExperience = granted.Experience,
                    },
                },
            },
        };
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
        runtimeSessions.ReplaceExact(session, updated);

        logger.LogInformation(
            "Tutorial level up {ConnectionId} character={CharacterId}: now level {Level} at {Exp}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            granted.Level,
            granted.Experience);

        // The reference server's own order, from its 2026-08-23 capture of the tutorial: the
        // experience frame, the quest list for the new level, then the acknowledgement. It sends
        // one more between the list and the acknowledgement -- NOTI 0x0002, the character's whole
        // state -- and this still does not: that is a re-projection of the published entry and the
        // session has no partial-update path for it. Recorded in Docs/ProtocolScaffoldDissonances.md.
        var mirror = updated.CharacterEntry.Skills.ProtocolMirror;
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

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
    }

    private ValueTask RefuseAsync(
        NetworkDispatchContext context, CancellationToken cancellationToken) =>
        sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new TutorialLevelUpResponse(TutorialLevelUpResponse.Refused)], []),
            cancellationToken);
}
