using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Microsoft.Extensions.Logging;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.UsageQuotas;

namespace Dfo.Server.Protocol.Game.Altar;

public sealed record GameAltarPrepareCommand(NetworkDispatchContext Dispatch, AltarPrepareRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameAltarDeathBatchCommand(NetworkDispatchContext Dispatch, AltarDeathBatchRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameAltarDifficultyCommand(NetworkDispatchContext Dispatch, AltarDifficultyRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameAltarCommandHandler(IAltarDungeonDomainService altars, IDungeonRunDomainService runs,
    ISelectedSessionDomainService sessions, IDungeonRunProjectionDomainService registry,
    IMonsterDeathDomainService deaths, IOnlineUserQueryDomainService onlineUsers,
    ICommandWorkUnits units, IGameCommandOutbox outbox, ICommandProjections projections,
    TimeProvider clock, IAltarSchedulerWakeSignal wake, ILogger<GameAltarCommandHandler> logger)
    : ICommandHandler<GameAltarPrepareCommand, bool>, ICommandHandler<GameAltarDeathBatchCommand, bool>,
        ICommandHandler<GameAltarDifficultyCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameAltarPrepareCommand command, CancellationToken ct)
    { await RunAsync(execution, command.Dispatch, command.Input.Metadata, AltarOperation.Ready, 0, null, ct).ConfigureAwait(false); return true; }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameAltarDeathBatchCommand command, CancellationToken ct)
    { await RunAsync(execution, command.Dispatch, command.Input.Metadata, AltarOperation.Advance, 0, command.Input.Actors, ct).ConfigureAwait(false); return true; }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameAltarDifficultyCommand command, CancellationToken ct)
    { await RunAsync(execution, command.Dispatch, command.Input.Metadata, AltarOperation.SelectDifficulty, command.Input.Difficulty, null, ct).ConfigureAwait(false); return true; }

    private async ValueTask RunAsync(CommandExecutionContext execution, NetworkDispatchContext dispatch,
        GameMessageMetadata metadata, AltarOperation operation, byte difficulty, IReadOnlyList<ushort>? actors, CancellationToken ct)
    {
        var reporter = sessions.FindByConnection(dispatch.Connection.ConnectionId);
        var expected = reporter is null ? null : runs.Find(reporter.CharacterId);
        if (reporter is null || expected?.Layout.Altar is null) return;
        var cohort = AltarTransitionProjection.Participants(runs, sessions, registry, expected.RunId);
        if (!sessions.IsInWorld(reporter) || !cohort.Contains(reporter)) return;
        if (actors is not null)
        {
            var captured = Capture(cohort, reporter, metadata);
            var results = await units.WriteAsync(execution, new("altar-death-batch", captured,
                Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: cohort),
                (unit, token) => AltarCommandPhases.DeathBatchAsync(altars, deaths, reporter, cohort,
                    expected.RunId, expected.RoomVersion, actors, unit.Footprint, token), ct).ConfigureAwait(false);
            foreach (var result in results)
            {
                if (result.Death?.Change is not { } change) continue;
                var drops = change.AddedGroundItems.Select(x => x.ToMonsterDrop()).ToArray();
                var owners = drops.Where(x => x.Rolled.OwnerCharacterId.HasValue).ToDictionary(x => x.Number,
                    x => SelectedSessionIdentity.ToWireUserId(x.Rolled.OwnerCharacterId!.Value));
                var death = new MonsterDeathNotification(change.Actor.Sequence, drops, reporter.WireUserId,
                    GameDropIdentity.ForKill(change.Run, change.Actor.Sequence), owners);
                var targets = result.Participants.Where(x => sessions.IsInWorld(x.Session))
                    .Select(x => new GameExplicitMessageTarget(x.Session.ConnectionId, KillFrames(x, death))).ToArray();
                await outbox.RegisterExplicitAsync(reporter.ConnectionId, new(
                    targets.FirstOrDefault(x => x.ConnectionId == reporter.ConnectionId)?.OrderedMessages ?? [],
                    targets.Where(x => x.ConnectionId != reporter.ConnectionId).ToArray()), ct).ConfigureAwait(false);
            }
            wake.Wake();
            return;
        }
        var run = runs.Find(reporter.CharacterId); var state = altars.Find(expected.RunId);
        AltarActionResult? action = null;
        if (run?.RunId == expected.RunId && state is not null)
        {
            var actionCohort = AltarTransitionProjection.Participants(runs, sessions, registry, expected.RunId);
            if (actionCohort.Contains(reporter))
            {
                var at = clock.GetUtcNow();
                action = (await units.WriteAsync(execution, new("altar-action", Capture(actionCohort, reporter, metadata)),
                    async (_, token) => new AltarCommandResult(await AltarCommandPhases.ActionAsync(altars,
                        reporter, expected.RunId, run.RoomVersion, state.Generation, at,
                        operation, difficulty, token).ConfigureAwait(false)), ct).ConfigureAwait(false)).Action;
            }
        }
        if (operation == AltarOperation.SelectDifficulty)
            await outbox.RegisterExplicitAsync(reporter.ConnectionId, new([new AltarDifficultyResponse(action is not null, difficulty)], []), ct).ConfigureAwait(false);
        // The CMD ACK resolves a manual choice; only timeout publishes NOTI 0129.
        if (operation == AltarOperation.SelectDifficulty && action is not null)
            action = action with { DifficultyResolved = false };
        if (action is not null)
            await AltarTransitionProjection.ProjectAsync(projections, action,
                AltarTransitionProjection.Participants(runs, sessions, registry, action.State.RunId),
                clock, wake, logger, ct).ConfigureAwait(false);
        if (operation == AltarOperation.Ready)
            foreach (var member in cohort)
                _ = await projections.ProjectAsync(new NotificationAudience.TheseSessions([member]),
                    new UsageQuotaViewChanged(member.CharacterId, UsageQuotaFamily.AltarDungeon), ct).ConfigureAwait(false);
    }

    private IReadOnlyList<SelectedSessionRecipient> Capture(IReadOnlyList<SelectedSessionIdentity> cohort,
        SelectedSessionIdentity reporter, GameMessageMetadata metadata)
    {
        NetworkConnectionContext? physical = null;
        if (onlineUsers.TryGetByConnection(reporter.ConnectionId, out var user) && user is not null)
        {
            if (user.AccountId != reporter.AccountId || user.CurrentCharacterId != reporter.CharacterId)
                throw new InvalidOperationException("The recipient connection no longer owns the selected character.");
            physical = user.Connection;
        }
        return cohort.Select(member => new SelectedSessionRecipient(member, member == reporter ? physical : null, metadata)).ToArray();
    }
    private static IReadOnlyList<IGameServerMessage> KillFrames(MonsterDeathParticipantResult participant, MonsterDeathNotification death)
    {
        var award = participant.Effects.Award;
        var frames = new List<IGameServerMessage>
        {
            new ExperienceNotification(participant.Progress.Level, award?.NotificationTotal ?? participant.Progress.Experience,
                participant.SkillPoints, AccountHonorLevel.Initial, ExperienceBonusLines.Of(award)), death,
        };
        frames.AddRange(MonsterQuestFrames.Of(participant.Effects));
        if (participant.AvailableQuests is { } quests) frames.Add(new AvailableQuestListMessage(participant.Progress.Level, quests));
        return frames;
    }
}
