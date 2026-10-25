using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;
// These minimal drivers preserve existing algorithm/byte assertions. They do not exercise
// production dispatcher, scope disposal or transaction units; separate wire oracles do that.
internal sealed class MoveMapAlgorithmDriver : GameMapLoadingCommandHandler
{
        private readonly IDungeonVisitDomainService visits;
    private readonly DungeonRunDeliveryRegistry registry;
    private readonly DungeonPartyRecipients? recipients;
    private readonly Dfo.Server.Protocol.Game.Fatigue.FatigueProjectionDelivery? fatigue;
    public MoveMapAlgorithmDriver(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
        IDungeonVisitDomainService visits, DungeonRunDeliveryRegistry registry, IGameMessageSender sender,
        ILogger logger, DungeonPartyRecipients? partyRecipients = null,
        Dfo.Server.Protocol.Game.Fatigue.FatigueProjectionDelivery? fatigueProjection = null)
        : base(sessions, runs, visits, registry, null!, null!, null!, null!, null!, null!, new LoadingOutbox(sender), null!, null!, null!, new ForwardLogger(logger))
    { this.visits = visits; this.registry = registry; recipients = partyRecipients; fatigue = fatigueProjection; }
    public ValueTask HandleAsync(NetworkDispatchContext dispatch, MoveMapRequest request, CancellationToken token) => MoveOrdinaryAsync(null!, dispatch, request, token);
    protected override IReadOnlyList<SelectedSessionIdentity> FindMoveRecipients(SelectedSessionIdentity actor, DungeonRunId id)
    { var found = recipients?.Find(actor, id) ?? registry.Find(id); return found.Count == 0 && recipients is null ? [actor] : found; }
    protected override ValueTask<DungeonMoveAttempt> MoveRoomAsync(CommandExecutionContext execution, SelectedSessionIdentity session, DungeonRunState current, MazeCell target, CancellationToken token) => visits.TryMoveAsync(session.CharacterId, current, target, token);
    protected override ValueTask MoveFatigueAsync(SelectedSessionIdentity session, CancellationToken token) => fatigue?.SendAsync(session, token) ?? ValueTask.CompletedTask;
}
internal interface ILoadingAlgorithm
{ ValueTask<bool> CompleteLoadingAsync(NetworkConnectionId id, Func<CancellationToken, ValueTask> release, CancellationToken token); }
internal sealed class DungeonLoadingAlgorithmDriver : GameMapLoadingCommandHandler, ILoadingAlgorithm
{
    private readonly Func<Dfo.Server.Data.Characters.CharacterId, DungeonRunId, long, CancellationToken, ValueTask> ready;
    private readonly WeaknessProjectionDelivery? weaknessProjectionDriver;
    private readonly UsageQuotaProjectionDelivery? quota;
    public DungeonLoadingAlgorithmDriver(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs, DungeonRunDeliveryRegistry registry,
        Func<Dfo.Server.Data.Characters.CharacterId, DungeonRunId, long, CancellationToken, ValueTask> ready,
        WeaknessProjectionDelivery? weaknessProjection = null, UsageQuotaProjectionDelivery? quotaProjection = null)
        : base(sessions, runs, null!, registry, null!, null!, null!, null!, null!, null!, new LoadingOutbox(null!), null!, null!, null!, NullLogger<GameMapLoadingCommandHandler>.Instance)
    { this.ready = ready; weaknessProjectionDriver = weaknessProjection; quota = quotaProjection; }
    public ValueTask<bool> CompleteLoadingAsync(NetworkConnectionId id, Func<CancellationToken, ValueTask> release, CancellationToken token) => CompleteOrdinaryLoadingAsync(id, release, token);
    protected override ValueTask LoadingReadyAsync(SelectedSessionIdentity session, DungeonRunState expected, CancellationToken token) => ready(session.CharacterId, expected.RunId, expected.RoomVersion, token);
    protected override ValueTask LoadingWeaknessAsync(SelectedSessionIdentity session, CancellationToken token) => weaknessProjectionDriver?.SendCurrentAsync(session, token) ?? ValueTask.CompletedTask;
    protected override ValueTask LoadingMechanismsAsync(SelectedSessionIdentity session, DungeonRunState current, CancellationToken token) => ValueTask.CompletedTask;
    protected override ValueTask<Dfo.Server.Abstractions.Notifications.NotificationDelivery> LoadingQuotaAsync(SelectedSessionIdentity session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily family, CancellationToken token) => quota!.SendAsync(session, family, token);
}
internal sealed class FinishLoadingAlgorithmDriver(IGameMessageSender sender, ILoadingAlgorithm projection)
    : ClientMessageHandler<FinishLoadingRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, FinishLoadingRequest message, CancellationToken token) =>
        _ = await projection.CompleteLoadingAsync(context.Connection.ConnectionId, ct => sender.SendExplicitAsync(context.Connection.ConnectionId, new([new LoadingCompleteNotification()], []), ct), token);
}
internal sealed class LoadingOutbox(IGameMessageSender sender) : IGameCommandOutbox
{
    public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage m, CancellationToken ct) => sender.SendCmdAsync(id,m,ct);
    public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage m, CancellationToken ct) => sender.SendNotiAsync(id,m,ct);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken ct) => sender.SendBatchAsync(c,m,ct);
    public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery d,CancellationToken ct) => sender.SendExplicitAsync(id,d,ct);
}
internal sealed class ForwardLogger(ILogger logger) : ILogger<GameMapLoadingCommandHandler>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => logger.BeginScope(state);
    public bool IsEnabled(LogLevel level) => logger.IsEnabled(level);
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? e, Func<TState,Exception?,string> formatter) => logger.Log(level,id,state,e,formatter);
}
