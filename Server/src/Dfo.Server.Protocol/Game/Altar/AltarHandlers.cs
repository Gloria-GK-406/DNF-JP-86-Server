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

public sealed class AltarProtocolCoordinator(IDungeonRunDomainService runs,
    ISelectedSessionDomainService sessions,
    AltarProjectionDelivery delivery, ICommandBus commands, IGameMessageSender sender, UsageQuotaProjectionDelivery quota)
{
    internal async ValueTask HandleAsync(NetworkConnectionId connection, AltarOperation operation, byte difficulty,
        IReadOnlyList<ushort>? actors, CancellationToken ct)
    {
        var reporter = sessions.FindByConnection(connection); var expected = reporter is null ? null : runs.Find(reporter.CharacterId);
        if (reporter is null || expected?.Layout.Altar is null) return;
        var cohort = delivery.Participants(expected.RunId);
        if (!sessions.IsInWorld(reporter) || !cohort.Contains(reporter)) return;
        if (actors is not null)
        {
            var results = await commands.SendAsync(new AltarDeathBatchCommand(reporter, cohort, expected.RunId, expected.RoomVersion, actors), ct).ConfigureAwait(false);
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
                await sender.SendExplicitAsync(reporter.ConnectionId, new(
                    targets.FirstOrDefault(x => x.ConnectionId == reporter.ConnectionId)?.OrderedMessages ?? [],
                    targets.Where(x => x.ConnectionId != reporter.ConnectionId).ToArray()), ct).ConfigureAwait(false);
            }
            delivery.Wake();
            return;
        }
        var action = await delivery.ExecuteAsync(reporter, expected.RunId, operation, difficulty, ct).ConfigureAwait(false);
        if (operation == AltarOperation.SelectDifficulty)
            await sender.SendExplicitAsync(reporter.ConnectionId, new([new AltarDifficultyResponse(action is not null, difficulty)], []), ct).ConfigureAwait(false);
        // The CMD ACK already resolves a manual choice; only timeout needs NOTI 0129.
        if (operation == AltarOperation.SelectDifficulty && action is not null)
            action = action with { DifficultyResolved = false };
        await delivery.ProjectAsync(action, ct).ConfigureAwait(false);
        if (operation == AltarOperation.Ready)
            foreach (var member in cohort) _ = await quota.SendAsync(member, UsageQuotaFamily.AltarDungeon, ct).ConfigureAwait(false);
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
public sealed class AltarPrepareHandler(AltarProtocolCoordinator coordinator) : ClientMessageHandler<AltarPrepareRequest>
{
    protected override ValueTask HandleMessageAsync(NetworkDispatchContext context, AltarPrepareRequest message, CancellationToken ct) =>
        coordinator.HandleAsync(context.Connection.ConnectionId, AltarOperation.Ready, 0, null, ct);
}
public sealed class AltarDeathBatchHandler(AltarProtocolCoordinator coordinator) : ClientMessageHandler<AltarDeathBatchRequest>
{
    protected override ValueTask HandleMessageAsync(NetworkDispatchContext context, AltarDeathBatchRequest message, CancellationToken ct) =>
        coordinator.HandleAsync(context.Connection.ConnectionId, AltarOperation.Advance, 0, message.Actors, ct);
}
public sealed class AltarDifficultyHandler(AltarProtocolCoordinator coordinator) : ClientMessageHandler<AltarDifficultyRequest>
{
    protected override ValueTask HandleMessageAsync(NetworkDispatchContext context, AltarDifficultyRequest message, CancellationToken ct) =>
        coordinator.HandleAsync(context.Connection.ConnectionId, AltarOperation.SelectDifficulty, message.Difficulty, null, ct);
}
