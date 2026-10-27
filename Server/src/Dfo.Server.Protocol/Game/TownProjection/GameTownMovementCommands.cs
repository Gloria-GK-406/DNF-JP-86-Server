using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.TownProjection;

public sealed record GameSetUserAreaCommand(NetworkDispatchContext Dispatch, SetUserAreaRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameSetUserPositionCommand(NetworkDispatchContext Dispatch, SetUserPositionRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed partial class GameTownMovementCommandHandler :
    ICommandHandler<GameSetUserAreaCommand, bool>, ICommandHandler<GameSetUserPositionCommand, bool>
{
    private readonly ISelectedSessionDomainService sessions;
    private readonly ITownAreaDomainService townAreas;
    private readonly ICommandProjections projections;
    private readonly IPartyProjectionStateDomainService projectionState;
    private readonly ILogger logger;
    private readonly ISelectedCharacterCurrentViewDomainService currentViews;
    private readonly IOnlineCharacterRuntimeDomainService onlineCharacters;
    private readonly ITownMovementDomainService towns;
    private readonly IMarriageHousingQueryDomainService housing;
    private readonly IPartyService parties;

    private readonly IChannelEntryDomainService channels;
    private readonly ICharacterLocationDomainService persistence;
    private readonly IMarriageQueryDomainService marriage;
    private readonly IMarriageHousingSessionDomainService housingSessions;
    private readonly ICommandWorkUnits units;
    private readonly IGameCommandOutbox outbox;
    public GameTownMovementCommandHandler(ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService currentViews,
        IOnlineCharacterRuntimeDomainService onlineCharacters, ITownAreaDomainService townAreas, IChannelEntryDomainService channels,
        ITownMovementDomainService towns, ICharacterLocationDomainService persistence, ICommandWorkUnits units,
        ICommandProjections projections, IGameCommandOutbox outbox, IPartyService parties,
        IPartyProjectionStateDomainService projectionState, ILogger<GameTownMovementCommandHandler> logger,
        IMarriageQueryDomainService marriage, IMarriageHousingQueryDomainService housing,
        IMarriageHousingSessionDomainService housingSessions)

    {
        this.sessions = sessions; this.townAreas = townAreas; this.projections = projections; this.projectionState = projectionState; this.logger = logger; this.currentViews = currentViews; this.onlineCharacters = onlineCharacters; this.towns = towns; this.housing = housing; this.parties = parties; this.channels = channels; this.persistence = persistence; this.units = units; this.outbox = outbox; this.marriage = marriage; this.housingSessions = housingSessions; }
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSetUserAreaCommand command, CancellationToken cancellationToken) =>
        AreaAsync(execution, command.Dispatch, command.Input, cancellationToken);
    private async ValueTask<bool> AreaAsync(CommandExecutionContext execution, NetworkDispatchContext context, SetUserAreaRequest message, CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Set user area {ConnectionId}: no selected session, ignoring {TownId}:{AreaId}",
                context.Connection.ConnectionId,
                message.TownId,
                message.AreaId);
            return false;
        }

        // The position is asked for here alongside the published session because the refusal below
        // reports where the character stays. The two arrive and leave together -- publication
        // records both and teardown drops both -- so this conjunct admits and refuses exactly the
        // connections the two before it do.
        var result = await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (!sessions.IsInWorld(session)
            || result is null
            || onlineCharacters.PositionOf(session) is not { } standing
            || townAreas.Find(session.CharacterId) is not { } standingMember
            || !session.Owns(standingMember))
        {
            logger.LogWarning(
                "Set user area {ConnectionId}: no published session, ignoring {TownId}:{AreaId}",
                context.Connection.ConnectionId,
                message.TownId,
                message.AreaId);
            return false;
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
                standing.TownId,
                standing.AreaId);
            return false;
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
            await TownMovementProjection.TownRefuseTownChangeAsync(outbox,
                context.Connection.ConnectionId, session, result, standing,
                channel.Kind is Dfo.Server.Data.Channel.ChannelEntryKind.SaintHorn
                    ? "请切换频道离开圣者之鸣号。"
                    : channel.IsTransient
                        ? "请切换频道离开当前专属区域。"
                        : message.TownId == 17
                            ? "请切换到圣者之鸣号频道后进入。"
                            : "请切换到对应的专属频道后进入。", cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (message.TownId == 101)
        {
            if (message.AreaId != 0 || standing.TownId != 101 && !housing.IsEntrySource(standing.TownId, standing.AreaId)
                || await housing.InstanceAsync(session, cancellationToken).ConfigureAwait(false) is not > 0)
            {
                await TownMovementProjection.TownRefuseTownChangeAsync(outbox, context.Connection.ConnectionId, session, result, standing,
                    "请由已婚角色通过赛丽亚房间的婚房入口进入。", cancellationToken).ConfigureAwait(false); return false;
            }
        }
        if (standing.TownId == 101 && message.TownId != 101)
        {
            var home = housingSessions.ReturnFor(session);
            if (home is null || home.TownId != message.TownId || home.AreaId != message.AreaId)
            {
                await TownMovementProjection.TownRefuseTownChangeAsync(outbox, context.Connection.ConnectionId, session, result, standing,
                    "请从婚房出口返回原来的城镇。", cancellationToken).ConfigureAwait(false); return false;
            }
        }
        var sharesTownPresence = message.TownId == 101 || await towns
            .SharesTownPresenceAsync(message.TownId, message.AreaId, cancellationToken)
            .ConfigureAwait(false);
        if (!await marriage.CanCrossAsync(session, channel.ChannelId,
                message.TownId, message.AreaId, cancellationToken).ConfigureAwait(false))
        {
            await TownMovementProjection.TownRefuseTownChangeAsync(outbox, context.Connection.ConnectionId, session, result, standing,
                "请通过自己的婚礼预约进入教堂。", cancellationToken).ConfigureAwait(false);
            return false;
        }
        if (!await marriage.CanLeaveAsync(session, message.TownId, message.AreaId, cancellationToken).ConfigureAwait(false))
        {
            await TownMovementProjection.TownRefuseTownChangeAsync(outbox, context.Connection.ConnectionId, session, result, standing,
                "婚礼即将开始，请留在礼堂等待仪式。", cancellationToken).ConfigureAwait(false);
            return false;
        }
        // Persist accepted area crossings before publishing memory state or client projections.
        // A failed write leaves the old town intact; transient channels retain the home position.
        if (!channel.IsTransient && (standing.TownId != message.TownId || standing.AreaId != message.AreaId))
        {
            var destination = standing with
            {
                TownId = message.TownId, AreaId = message.AreaId, X = message.X, Y = message.Y,
            };
            if (message.TownId == 101)
            {
                await PersistAsync(execution, session, standing, cancellationToken).ConfigureAwait(false);
                housingSessions.RememberReturn(session, standing);
            }
            if (destination.TownId != 101)
                await PersistAsync(execution, session, destination, cancellationToken).ConfigureAwait(false);
        }
        var weddingAppearance = marriage is null ? null : await marriage.AppearanceAsync(session, cancellationToken).ConfigureAwait(false);
        var moved = onlineCharacters.CrossInto(
            session, message.TownId, message.AreaId, message.X, message.Y);
        if (!await TownMovementProjection.TownTransferAsync(sessions, townAreas, projections, new PartyTownProjectionDelivery(parties, townAreas, sessions, projections, projectionState), currentViews, housing, outbox, housingSessions,
                context.Connection.ConnectionId,
                session,
                result,
                channel.ChannelId,
                moved,
                standingMember,
                sharesTownPresence,
                cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning(
                "Set user area {ConnectionId}: area membership generation changed while moving",
                context.Connection.ConnectionId);
            return false;
        }

        if (weddingAppearance is not null
            && (message.TownId != weddingAppearance.Room.TownId || message.AreaId != weddingAppearance.Room.AreaId)
            && await marriage!.WeddingPhaseAsync(weddingAppearance.Room.CeremonyId, cancellationToken).ConfigureAwait(false)
                != Dfo.Server.Data.Marriage.MarriageWeddingPhase.Preparing)
            await outbox.RegisterExplicitAsync(session.ConnectionId, new([new Dfo.Server.Protocol.Game.MarriageProposal.WeddingAvatarNotification(weddingAppearance, true)], []), cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Set user area {ConnectionId} user={WireUserId}: {FromTown}:{FromArea} -> "
                + "{ToTown}:{ToArea} pos=({X},{Y})",
            context.Connection.ConnectionId,
            session.WireUserId,
            standing.TownId,
            standing.AreaId,
            moved.TownId,
            moved.AreaId,
            moved.X,
            moved.Y);

        return true;
    }
    private ValueTask<bool> PersistAsync(CommandExecutionContext context, SelectedSessionIdentity session, CharacterPositionState position, CancellationToken token) =>
        units.WriteAsync(context, new("SetUserArea.Position", []), async (_, ct) =>
        { await persistence.PersistAsync(session.AccountId, session.CharacterId, position, ct).ConfigureAwait(false); return true; }, token);
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSetUserPositionCommand command, CancellationToken token)
    {
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || !sessions.IsInWorld(session) || townAreas.Find(session.CharacterId) is not { } member || !session.Owns(member))
        {
            logger.LogDebug("Set user position {ConnectionId}: no published session, ignoring ({X},{Y})",
                command.Dispatch.Connection.ConnectionId, command.Input.X, command.Input.Y);
            return false;
        }
        var input = command.Input;
        var moved = onlineCharacters.StepTo(session, input.X, input.Y, input.Direction);
        return await TownMovementProjection.TownMoveAsync(sessions, townAreas, projections, outbox, command.Dispatch.Connection.ConnectionId, session, moved, input.Trailer, token).ConfigureAwait(false);
    }
}
