using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game;
internal sealed partial class GameMonsterDeathCommandHandler : ICommandHandler<GameMonsterDeathCommand, bool>
{
    private readonly ISelectedSessionDomainService sessions;
    private readonly ILogger logger;
    private readonly ICharacterDomainService characters;
    private readonly IBossTowerDomainService bossTowers;
    private readonly IIllusionTowerDomainService illusionTowers;
    private readonly ITowerRunKind bossKind;
    private readonly ITowerRunKind illusionKind;
    private readonly IDungeonParticipationDomainService participation;
    private readonly TimeProvider clock;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandWorkUnits workUnits;
    private readonly IOnlineUserQueryDomainService onlineUsers;
    private readonly IPartyService parties;

    private readonly IDungeonRunQueryDomainService runs;
    private readonly IDungeonRunProjectionDomainService registry;
    private readonly IDungeonClearDomainService clearEvaluator;
    private readonly IMonsterDeathDomainService deaths;
    private readonly ICreatureRuntimeDomainService creatures;
    private readonly IQuestQueryDomainService quests;
    private readonly ICommandConnectionClosures closures;
    private readonly IInventorySlotProjectionDomainService slots;
    private readonly IGuardianAltarDomainService guardian;
    public GameMonsterDeathCommandHandler(ISelectedSessionDomainService sessions, IDungeonRunQueryDomainService runs,
        IDungeonRunProjectionDomainService registry, IPartyService parties, IDungeonClearDomainService clearEvaluator,
        IMonsterDeathDomainService deaths, ICreatureRuntimeDomainService creatures, IQuestQueryDomainService quests,
        IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        IDungeonParticipationDomainService participation,
        ICharacterDomainService characters, IOnlineUserQueryDomainService onlineUsers,
        ICommandWorkUnits units, IGameCommandOutbox outbox, ICommandConnectionClosures closures,
        TimeProvider clock, IInventorySlotProjectionDomainService slots, ILogger<GameMonsterDeathCommandHandler> logger,
        IGuardianAltarDomainService guardian)

    {
        this.sessions = sessions; this.logger = logger; this.characters = characters; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; bossKind = new BossTowerRunKind(bossTowers); illusionKind = new IllusionTowerRunKind(illusionTowers); this.participation = participation; this.clock = clock; this.outbox = outbox; this.workUnits = units; this.onlineUsers = onlineUsers; this.parties = parties;
        this.runs=runs; this.registry=registry; this.clearEvaluator=clearEvaluator; this.deaths=deaths;
        this.creatures=creatures; this.quests=quests;
        this.closures=closures; this.slots=slots;
        this.guardian = guardian;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameMonsterDeathCommand command, CancellationToken ct)
    { await RunAsync(execution, command.Dispatch, command.Input, command.Input.LabelProbe, ct).ConfigureAwait(false); return true; }
    private async ValueTask RunAsync(CommandExecutionContext execution, NetworkDispatchContext context, MonsterDeathRequest message, bool labelProbe, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(message.Metadata)) return;
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Monster death received connection={ConnectionId} sequence={Sequence} ordinal={Ordinal} actor={Actor} object={ObjectCode}",
                context.Connection.ConnectionId, message.Metadata.Sequence, message.Metadata.InboundOrdinal, message.ActorSequence, message.ObjectCode);
        var reporter = sessions.FindByConnection(context.Connection.ConnectionId);
        if (reporter is null)
        {
            logger.LogDebug("Monster death ignored connection={ConnectionId} reason=NoSession", context.Connection.ConnectionId);
            return;
        }
        switch (DungeonLocator.Locate(reporter, participation, bossTowers, illusionTowers))
        {
            // A Boss tower ignores object destruction reports.
            case DungeonPlace.InBossTower boss:
                if (message.ObjectCode is null)
                    await KillBossAsync(execution, boss, message.Metadata, message.ActorSequence, message.WireUserId, ct).ConfigureAwait(false);
                return;
            // Object destruction in an illusion tower is answered by the ordinary flow, as in town.
            case DungeonPlace.InIllusionTower illusion when message.ObjectCode is null:
                await KillIllusionAsync(execution, illusion, message.ActorSequence, message.WireUserId, ct).ConfigureAwait(false);
                return;
            default:
                await ReportOrdinaryAsync(execution, context, message, labelProbe, reporter, ct).ConfigureAwait(false);
                return;
        }
    }

    private async ValueTask ReportOrdinaryAsync(CommandExecutionContext execution, NetworkDispatchContext context, MonsterDeathRequest message,
        bool labelProbe, SelectedSessionIdentity reporter, CancellationToken ct)
    {
        var run = runs.Find(reporter.CharacterId);
        if (run is null)
        {
            logger.LogDebug("Monster death ignored connection={ConnectionId} actor={Actor} reason=NoRun", reporter.ConnectionId, message.ActorSequence);
            await outbox.RegisterExplicitAsync(reporter.ConnectionId,
                new GameExplicitMessageDelivery([MonsterDeathNotification.Empty(message.ActorSequence)], []), ct).ConfigureAwait(false);
            return;
        }
        if (run.Layout.Mode is DungeonRunMode.Guardian)
        {
            if (message.ObjectCode is null)
                _ = await workUnits.WriteAsync(execution, new("guardian-altar.death", CaptureDeathRecipients([reporter], context, message.Metadata)),
                    (_, token) => guardian.ReportDeathAsync(reporter, run.RunId, run.RoomVersion, message.ActorSequence, token), ct).ConfigureAwait(false);
            await outbox.RegisterExplicitAsync(reporter.ConnectionId, new([MonsterDeathNotification.Empty(message.ActorSequence)], []), ct).ConfigureAwait(false);
            return;
        }
        var participants = (run.PartyMemberCount > 1
                ? FindRecipients(reporter, run.RunId) : [reporter])
            .Where(x => sessions.IsInWorld(x)).DistinctBy(x => x.CharacterId).ToArray();
        if (!participants.Contains(reporter))
        {
            logger.LogDebug("Monster death ignored connection={ConnectionId} run={RunId} actor={Actor} reason=NotCurrentParticipant",
                reporter.ConnectionId, run.RunId, message.ActorSequence);
            return;
        }
        var captured = CaptureDeathRecipients(participants, context, message.Metadata);
        if (message.ObjectCode is { } code)
        {
            var objects = await DestroyObjectAsync(execution, reporter, run.RunId, run.RoomVersion, code, participants, ct).ConfigureAwait(false);
            if (objects.Rewards is null) return;
            var targets = objects.Rewards.Where(x => sessions.IsInWorld(x.Session))
                .Select(x => new GameExplicitMessageTarget(x.Session.ConnectionId, MonsterQuestFrames.Of(x.Effects))).ToArray();
            await DeliverAsync(reporter, targets, ct).ConfigureAwait(false);
            return;
        }
        var settled = await SettleDeathAsync(execution, new(reporter, participants,
            run.RunId, run.RoomVersion, message.ActorSequence), captured, ct).ConfigureAwait(false);
        if (settled.Death?.Change is not { } change)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Monster death not committed connection={ConnectionId} run={RunId} room={RoomId} version={RoomVersion} actor={Actor}; sending empty acknowledgement",
                    reporter.ConnectionId, run.RunId, run.CurrentRoomMapId, run.RoomVersion, message.ActorSequence);
            await outbox.RegisterExplicitAsync(reporter.ConnectionId,
                new GameExplicitMessageDelivery([MonsterDeathNotification.Empty(message.ActorSequence)], []), ct).ConfigureAwait(false);
            return;
        }
        try
        {
            if (change.Run.Layout.ConsumesFatigue && change.IsCellCleared)
                foreach (var participant in settled.Participants)
                    creatures.QueueRoomExperience(participant.Session, change.Run.RunId, change.Run.CurrentCell, 1);
            var clear = await clearEvaluator.TryOpenClearAsync(reporter.AccountId, reporter.CharacterId,
                change.Run.RunId, change.Run.RoomVersion, ct).ConfigureAwait(false);
            var drops = change.AddedGroundItems.Select(x => x.ToMonsterDrop()).ToArray();
            var owners = drops.Where(x => x.Rolled.OwnerCharacterId.HasValue)
                .ToDictionary(x => x.Number, x => SelectedSessionIdentity.ToWireUserId(x.Rolled.OwnerCharacterId!.Value));
            var death = new MonsterDeathNotification(message.ActorSequence, drops, reporter.WireUserId,
                GroundDropInstance.ForKill(change.Run, message.ActorSequence), owners);
            var targets = settled.Participants.Where(x => sessions.IsInWorld(x.Session))
                .Select(x => new GameExplicitMessageTarget(x.Session.ConnectionId, Frames(x, death, change.AutoKilledActorSequences,
                    settled.Tournament, labelProbe, change.LostLandFeverPercent))).ToArray();
            await DeliverAsync(reporter, targets, ct).ConfigureAwait(false);
            // The native LostLand summary collects announced drops before GET_ITEM. Keep the
            // announced identities so a summary can be reconciled with pickup logs independently.
            if (change.Run.Layout.Mode is DungeonRunMode.LostLand && logger.IsEnabled(LogLevel.Debug))
                foreach (var drop in change.AddedGroundItems)
                    logger.LogDebug("LostLand drop registered connection={ConnectionId} run={RunId} room={RoomId} version={RoomVersion} actor={Actor} number={Number} item={ItemId} count={ItemCount} equipment={IsEquipment} owner={Owner}",
                        reporter.ConnectionId, change.Run.RunId, change.Run.CurrentRoomMapId, change.Run.RoomVersion,
                        message.ActorSequence, drop.Number, drop.ItemId, drop.Count, drop.IsEquipment,
                        death.DropOwners?.GetValueOrDefault(drop.Number) ?? death.Owner);
            if (clear is not null) await ProjectClearAsync(reporter, clear, ct).ConfigureAwait(false);
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Monster death delivered connection={ConnectionId} sequence={Sequence} run={RunId} dungeon={DungeonId} room={RoomId} version={RoomVersion} actor={Actor} participants={Count} drops={Drops} cellCleared={CellCleared} boss={IsBossRoom} clearProjected={ClearProjected} aliveBlockers={AliveBlockers} mechanismBlocks={MechanismBlocks}",
                    reporter.ConnectionId, message.Metadata.Sequence, run.RunId, run.Layout.DungeonId, run.CurrentRoomMapId,
                    run.RoomVersion, message.ActorSequence, settled.Participants.Count, drops.Length, change.IsCellCleared,
                    change.Run.IsBossRoom, clear is not null,
                    change.Run.CurrentCellState.Actors.Count(x => x.BlocksClear && !x.IsDead), change.Run.CurrentCellState.MechanismBlocksClear);
            if (logger.IsEnabled(LogLevel.Trace))
                logger.LogTrace("Dungeon remaining actors run={RunId} room={RoomId} version={RoomVersion} actors={AliveActors}",
                    run.RunId, run.CurrentRoomMapId, run.RoomVersion,
                    string.Join(',', change.Run.CurrentCellState.Actors.Where(x => x.BlocksClear && !x.IsDead).Select(x => x.Sequence)));
        }
        catch (Exception failure)
        {
            var uncertain = new Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException(
                Dfo.Server.Abstractions.Transactions.TransactionUncertaintyReason.PostCommitPublicationFailure,
                "Monster death committed but client publication failed.", failure);
            throw uncertain;
        }
    }

    private ValueTask DeliverAsync(SelectedSessionIdentity reporter, IReadOnlyList<GameExplicitMessageTarget> targets, CancellationToken ct) =>
        outbox.RegisterExplicitAsync(reporter.ConnectionId, new GameExplicitMessageDelivery(
            targets.FirstOrDefault(x => x.ConnectionId == reporter.ConnectionId)?.OrderedMessages ?? [],
            targets.Where(x => x.ConnectionId != reporter.ConnectionId).ToArray()), ct);

    private IReadOnlyList<IGameServerMessage> Frames(MonsterDeathParticipantResult participant,
        MonsterDeathNotification death, IReadOnlyList<ushort> autoKilled, TournamentActionResult? tournament = null,
        bool labelProbe = false, byte? feverPercent = null)
    {
        var award = participant.Effects.Award;
        var total = award?.NotificationTotal ?? participant.Progress.Experience;
        var frames = new List<IGameServerMessage> { new ExperienceNotification(participant.Progress.Level,
            labelProbe ? checked(total + ExperienceDiagnostics.ProbeTotal) : total, participant.SkillPoints,
            AccountHonorLevel.Initial, ExperienceBonusLines.Of(award), labelProbe), death };
        frames.AddRange(autoKilled.Select(MonsterDeathNotification.Empty));
        if (feverPercent is { } percent) frames.Add(new DungeonFeverGaugeMessage(percent));
        frames.AddRange(MonsterQuestFrames.Of(participant.Effects));
        if (participant.AvailableQuests is { } quests) frames.Add(new AvailableQuestListMessage(participant.Progress.Level, quests));
        if (tournament is not null)
            frames.AddRange(TournamentFrames(tournament));
        return frames;
    }

    private IReadOnlyList<SelectedSessionRecipient> CaptureDeathRecipients(IReadOnlyList<SelectedSessionIdentity> participants, NetworkDispatchContext dispatch, GameMessageMetadata metadata) =>
        participants.Select(x => CaptureParticipant(x) with { Metadata = metadata }).ToArray();
    private IReadOnlyList<SelectedSessionIdentity> FindRecipients(SelectedSessionIdentity reporter, DungeonRunId id) =>
        DungeonPartyRecipients.FindCurrent(parties, sessions, runs, registry, reporter, id);
    private ValueTask<MonsterDeathSettlementResult> SettleDeathAsync(CommandExecutionContext execution,
        MonsterDeathSettlementRequest request, IReadOnlyList<SelectedSessionRecipient> captured, CancellationToken ct) =>
        workUnits.WriteAsync(execution, new("settle-monster-death", captured,
            Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: request.Participants),
            (unit, token) => deaths.SettleAsync(request, unit.Footprint, token), ct);
    private ValueTask<int> ProjectClearAsync(SelectedSessionIdentity reporter, DungeonClearChange clear, CancellationToken ct) =>
        DungeonClearCommandProjection.DeliverAsync(sessions, runs, FindRecipients(reporter, clear.RunId), quests, outbox, reporter, clear, ct);
    private IReadOnlyList<IGameServerMessage> TournamentFrames(TournamentActionResult result) =>
        TournamentCompletionFrames.CompletionFrames(result, slots, clock);
}
