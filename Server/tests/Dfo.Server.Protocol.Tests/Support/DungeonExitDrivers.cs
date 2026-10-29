using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Testing;
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
    BossTowerPartyDriver? bossParties = null, IllusionTowerPartyDriver? towerParties = null,
    IBossTowerDomainService? boss = null, Func<TowerActionContext, byte, CancellationToken, ValueTask<BossTowerActionResult>>? choose = null)
{
    private GamePartyTransitionCommandHandler? handler;
    private GamePartyTransitionCommandHandler Handler => handler ??= Build();
    private GamePartyTransitionCommandHandler Build()
    {
        var supplied = new List<object>();
        if (bossParties is not null) supplied.AddRange(bossParties.Dependencies);
        if (towerParties is not null) supplied.AddRange(towerParties.Dependencies);
        if (town is not null) supplied.AddRange(ProtocolAlgorithmFixture.TownDependencies(town));
        supplied.AddRange(new object?[] { selected, views, positions, runs, registry, parties, areas, cards, intents, ordering,
            sender, logger is null ? null : new TestLogger<GamePartyTransitionCommandHandler>(logger) }.OfType<object>());
        if (partyNotifications is not null)
            supplied.Add(ProtocolAlgorithmFixture.Field<ICommandProjections>(partyNotifications));
        if (boss is not null)
            supplied.Add(choose is null ? boss : ProtocolAlgorithmFixture.Decorate(boss, (method, args) =>
                method.Name == nameof(IBossTowerDomainService.SelectResultAsync)
                    ? (true, choose((TowerActionContext)args[0]!, (byte)args[1]!, (CancellationToken)args[2]!))
                    : (false, null)));
        return ProtocolAlgorithmFixture.Create<GamePartyTransitionCommandHandler>(supplied.ToArray());
    }
    public async ValueTask HandleAsync(NetworkDispatchContext dispatch, EplpCommandRequest message, CancellationToken ct)
    {
        var command = new GameEplpCommand(dispatch, message);
        _ = await Handler.HandleAsync(ComponentContext.For(command, Handler.GetType()), command, ct);
    }
    public async ValueTask HandleAsync(NetworkDispatchContext dispatch, GiveUpGameRequest message, CancellationToken ct)
    {
        var command = new GameGiveUpGameCommand(dispatch, message);
        _ = await Handler.HandleAsync(ComponentContext.For(command, Handler.GetType()), command, ct);
    }
}
internal sealed class EplpDriver(ISelectedSessionDomainService selectedSessions, ISelectedCharacterCurrentViewDomainService currentViews,
    IOnlineCharacterRuntimeDomainService onlineCharacters, IDungeonRunDomainService runs, DungeonRunDeliveryRegistry deliveries,
    IGameMessageSender sender, TownAreaProjectionDelivery townAreaProjection, ILogger<EplpCommandHandler> logger,
    IDungeonCardPresentationControl? cardPresentation = null, IPartyService? parties = null,
    IBossTowerDomainService? bossTower = null, IDungeonEntryIntentDomainService? entryIntents = null,
    ITownAreaDomainService? townAreas = null, IOutboundOrdering? ordering = null,
    Func<TowerActionContext, byte, CancellationToken, ValueTask<BossTowerActionResult>>? choose = null)
    : DungeonExitDriver(selectedSessions, currentViews, onlineCharacters, runs, deliveries, sender, townAreaProjection, logger,
        townAreas, parties, cards: cardPresentation, intents: entryIntents, ordering: ordering, boss: bossTower, choose: choose);
internal sealed class GiveUpDriver(ISelectedSessionDomainService selectedSessions, ISelectedCharacterCurrentViewDomainService currentViews,
    IOnlineCharacterRuntimeDomainService onlineCharacters, IDungeonRunDomainService runs, DungeonRunDeliveryRegistry deliveries,
    IGameMessageSender sender, TownAreaProjectionDelivery townAreaProjection, ILogger<GiveUpGameHandler> logger,
    ITownAreaDomainService? townAreas = null, IPartyService? parties = null, PartyNotificationDelivery? partyNotifications = null,
    IDungeonCardPresentationControl? cardPresentation = null,
    IllusionTowerPartyDriver? towerParties = null,
    BossTowerPartyDriver? bossParties = null)
    : DungeonExitDriver(selectedSessions, currentViews, onlineCharacters, runs, deliveries, sender, townAreaProjection, logger,
        townAreas, parties, partyNotifications, cardPresentation, bossParties: bossParties, towerParties: towerParties);

internal sealed record LeaveDungeonAlgorithmCommand(SelectedSessionIdentity Session, IReadOnlyList<SelectedSessionIdentity> Sessions, DungeonRunId RunId)
    : ICommand<LeaveDungeonAlgorithmResult>;
internal sealed record LeaveDungeonAlgorithmResult(IReadOnlyList<CharacterId> EndedCharacters);
internal sealed class LeaveDungeonAlgorithmHandler(IDungeonRunDomainService runs) : ICommandHandler<LeaveDungeonAlgorithmCommand, LeaveDungeonAlgorithmResult>
{
    public async ValueTask<LeaveDungeonAlgorithmResult> HandleAsync(CommandExecutionContext context, LeaveDungeonAlgorithmCommand command, CancellationToken ct) =>
        new(await GamePartyTransitionCommandHandler.EndOrdinaryRunCoreAsync(runs, command.RunId, ct));
}
