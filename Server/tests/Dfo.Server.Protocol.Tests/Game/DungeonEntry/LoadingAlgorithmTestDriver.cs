using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Testing;
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
internal sealed class MoveMapAlgorithmDriver(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
    IDungeonVisitDomainService visits, DungeonRunDeliveryRegistry registry, IGameMessageSender sender,
    ILogger logger, DungeonPartyRecipients? partyRecipients = null)
{
    public async ValueTask HandleAsync(NetworkDispatchContext dispatch, MoveMapRequest input, CancellationToken token)
    {
        var actor = sessions.FindByConnection(dispatch.Connection.ConnectionId);
        var run = actor is null ? null : runs.Find(actor.CharacterId);
        var parties = partyRecipients is not null ? ProtocolAlgorithmFixture.Field<IPartyService>(partyRecipients)
            : run is not null ? new Dfo.Server.Protocol.Tests.Support.DungeonProjectionParty(run)
            : ProtocolAlgorithmFixture.Default<IPartyService>();
        var handler = ProtocolAlgorithmFixture.Create<GameMapLoadingCommandHandler>(sessions, runs, visits, registry,
            parties, new CurrentFatigue(), new LoadingOutbox(sender), new ForwardLogger(logger));
        var command = new GameMoveMapCommand(dispatch, input);
        _ = await handler.HandleAsync(Dfo.Server.Protocol.Tests.Support.ComponentContext.For(command, handler.GetType()), command, token);
    }
    private sealed class CurrentFatigue : ICharacterFatigueDomainService
    {
        public ValueTask<CharacterFatigueBalance> BalanceOfAsync(Dfo.Server.Data.Characters.CharacterId id, CancellationToken ct) =>
            ValueTask.FromResult(new CharacterFatigueBalance(156, 17) { ConsumedToday = 23 });
        public ValueTask SpendAsync(Dfo.Server.Data.Characters.CharacterId id, ushort points, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask RefundAsync(Dfo.Server.Data.Characters.CharacterId id, ushort points, CancellationToken ct) => throw new NotSupportedException();
    }
}
internal interface ILoadingAlgorithm
{ ValueTask<bool> CompleteLoadingAsync(NetworkConnectionId id, Func<CancellationToken, ValueTask> release, CancellationToken token); }
internal sealed class DungeonLoadingAlgorithmDriver : ILoadingAlgorithm
{
    private readonly GameMapLoadingCommandHandler command;
    private readonly ReleaseOutbox outbox;
    public DungeonLoadingAlgorithmDriver(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
        DungeonRunDeliveryRegistry registry, WeaknessProjectionDelivery weaknessProjection,
        UsageQuotaProjectionDelivery? quotaProjection = null)
    {
        outbox = new ReleaseOutbox(ProtocolAlgorithmFixture.Field<IGameMessageSender>(weaknessProjection));
        command = ProtocolAlgorithmFixture.Create<GameMapLoadingCommandHandler>(sessions, runs, registry, outbox,
            ProtocolAlgorithmFixture.Field<IWeaknessDomainService>(weaknessProjection),
            quotaProjection is null ? ProtocolAlgorithmFixture.Default<ICommandProjections>()
                : new LegacyCommandProjections(ProtocolAlgorithmFixture.Field<IEventService>(quotaProjection)));
    }
    public ValueTask<bool> CompleteLoadingAsync(NetworkConnectionId id, Func<CancellationToken, ValueTask> release, CancellationToken token)
    {
        outbox.Release = release;
        return command.CompleteOrdinaryLoadingAsync(id, token);
    }
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

internal sealed class ReleaseOutbox(IGameMessageSender sender) : IGameCommandOutbox
{
    internal Func<CancellationToken, ValueTask> Release { get; set; } = _ => throw new InvalidOperationException("No fixture response observer.");
    public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage m, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage m, CancellationToken ct) => sender.SendNotiAsync(id, m, ct);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken ct) => throw new NotSupportedException();
    public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery d,CancellationToken ct) =>
        d.RequiredDirectMessages is [LoadingCompleteNotification] && d.BestEffortTargets.Count == 0
            ? Release(ct) : throw new NotSupportedException("Only the loading-release observation is supplied.");
}
