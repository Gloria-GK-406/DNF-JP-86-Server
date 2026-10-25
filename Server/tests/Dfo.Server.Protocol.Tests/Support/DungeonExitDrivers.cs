using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Tests.Support;

// Direct fixtures drive the production algorithm; only DungeonExitLifecycleTests establish actual request/scope behavior.
internal class DungeonExitDriver(ISelectedSessionDomainService selected, ISelectedCharacterCurrentViewDomainService views,
    IOnlineCharacterRuntimeDomainService positions, IDungeonRunDomainService runs, DungeonRunDeliveryRegistry registry,
    IGameMessageSender sender, TownAreaProjectionDelivery town, ILogger logger, ITownAreaDomainService? areas = null,
    IPartyService? parties = null, PartyNotificationDelivery? partyNotifications = null, IDungeonCardPresentationControl? cards = null,
    IDungeonEntryIntentDomainService? intents = null, IOutboundOrdering? ordering = null,
    BossTowerPartyCoordinator? bossParties = null, IllusionTowerPartyCoordinator? towerParties = null,
    Func<SelectedSessionIdentity, DungeonRunId, bool, CancellationToken, ValueTask>? altarExit = null,
    IBossTowerDomainService? boss = null, Func<BossTowerActionContext, byte, CancellationToken, ValueTask<BossTowerActionResult>>? choose = null)
    : GameDungeonExitCommandHandler(selected, views, positions, runs, registry, parties!, areas!, null!, null!,
        null!, null!, null!, null!, boss!, null!, null!, null!, new CardTestOutbox(sender), null!, cards!, intents!,
        null!, null!, null!, TimeProvider.System, new TestLogger<GameDungeonExitCommandHandler>(logger), ordering)
{
    protected override ValueTask<int> ReturnTownAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken ct) => town.EnterAsync(entries, ct);
    protected override ValueTask ProjectPartyAsync(PartyNotificationPlan plan, PartyNotificationRecipientPlan recipients, CancellationToken ct) =>
        partyNotifications?.DeliverAsync(plan, recipients, ct) ?? ValueTask.CompletedTask;
    protected override ValueTask SettleAltarExitAsync(CommandExecutionContext execution, SelectedSessionIdentity session, DungeonRunId run, bool onlyCaller, CancellationToken ct) =>
        altarExit?.Invoke(session, run, onlyCaller, ct) ?? ValueTask.CompletedTask;
    protected override async ValueTask<bool> TryGiveUpTowersAsync(CommandExecutionContext execution, SelectedSessionIdentity session, CancellationToken ct)
    {
        if (bossParties is not null && (await bossParties.TryLeaveAsync(session, true, new GiveUpGameResponse(), ct)).Handled) return true;
        return towerParties is not null && (await towerParties.TryLeaveAsync(session, true, new GiveUpGameResponse(), ct)).Handled;
    }
    protected override ValueTask<BossTowerActionResult> SelectBossResultAsync(CommandExecutionContext execution, BossTowerSnapshot expected, SelectedSessionIdentity session, byte option, CancellationToken ct) =>
        choose!(new(session, expected.RunId, expected.Room.Version), option, ct);
    public ValueTask HandleAsync(NetworkDispatchContext dispatch, EplpCommandRequest message, CancellationToken ct) => EplpAsync(Context(), dispatch, message, ct);
    public ValueTask HandleAsync(NetworkDispatchContext dispatch, GiveUpGameRequest message, CancellationToken ct) => GiveUpAsync(Context(), dispatch, message, ct);
    private static CommandExecutionContext Context() => new(Guid.NewGuid(), typeof(GameEplpCommand), typeof(GameDungeonExitCommandHandler), CommandExecutionKind.Direct);
}
internal sealed class EplpDriver(ISelectedSessionDomainService selectedSessions, ISelectedCharacterCurrentViewDomainService currentViews,
    IOnlineCharacterRuntimeDomainService onlineCharacters, IDungeonRunDomainService runs, DungeonRunDeliveryRegistry deliveries,
    IGameMessageSender sender, TownAreaProjectionDelivery townAreaProjection, ILogger<EplpCommandHandler> logger,
    IDungeonCardPresentationControl? cardPresentation = null, IPartyService? parties = null,
    IBossTowerDomainService? bossTower = null, IDungeonEntryIntentDomainService? entryIntents = null,
    ITownAreaDomainService? townAreas = null, IOutboundOrdering? ordering = null,
    Func<BossTowerActionContext, byte, CancellationToken, ValueTask<BossTowerActionResult>>? choose = null)
    : DungeonExitDriver(selectedSessions, currentViews, onlineCharacters, runs, deliveries, sender, townAreaProjection, logger,
        townAreas, parties, cards: cardPresentation, intents: entryIntents, ordering: ordering, boss: bossTower, choose: choose);
internal sealed class GiveUpDriver(ISelectedSessionDomainService selectedSessions, ISelectedCharacterCurrentViewDomainService currentViews,
    IOnlineCharacterRuntimeDomainService onlineCharacters, IDungeonRunDomainService runs, DungeonRunDeliveryRegistry deliveries,
    IGameMessageSender sender, TownAreaProjectionDelivery townAreaProjection, ILogger<GiveUpGameHandler> logger,
    ITownAreaDomainService? townAreas = null, IPartyService? parties = null, PartyNotificationDelivery? partyNotifications = null,
    IDungeonCardPresentationControl? cardPresentation = null,
    IllusionTowerPartyCoordinator? towerParties = null,
    Func<SelectedSessionIdentity, DungeonRunId, bool, CancellationToken, ValueTask>? altarExit = null,
    BossTowerPartyCoordinator? bossParties = null)
    : DungeonExitDriver(selectedSessions, currentViews, onlineCharacters, runs, deliveries, sender, townAreaProjection, logger,
        townAreas, parties, partyNotifications, cardPresentation, bossParties: bossParties, towerParties: towerParties, altarExit: altarExit);

internal sealed record LeaveDungeonAlgorithmCommand(SelectedSessionIdentity Session, IReadOnlyList<SelectedSessionIdentity> Sessions, DungeonRunId RunId)
    : ICommand<LeaveDungeonAlgorithmResult>, IMultiSelectedSessionCommand;
internal sealed record LeaveDungeonAlgorithmResult(IReadOnlyList<CharacterId> EndedCharacters);
internal sealed class LeaveDungeonAlgorithmHandler(IDungeonRunDomainService runs) : ICommandHandler<LeaveDungeonAlgorithmCommand, LeaveDungeonAlgorithmResult>
{
    public async ValueTask<LeaveDungeonAlgorithmResult> HandleAsync(CommandExecutionContext context, LeaveDungeonAlgorithmCommand command, CancellationToken ct) =>
        new(await GameDungeonExitCommandHandler.EndOrdinaryRunCoreAsync(runs, command.Sessions, command.RunId, ct));
}
