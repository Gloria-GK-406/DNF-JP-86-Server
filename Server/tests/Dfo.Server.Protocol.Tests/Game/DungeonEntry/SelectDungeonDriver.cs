using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;
using Dfo.Server.Abstractions.Execution;
namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;
internal static class SelectDungeonDriver
{
    internal static EntryHandlerDriver Create(
    ICharacterDomainService characters,
    ICharacterStateProjectionDomainService currentState,
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    IPartyService parties,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveryRegistry,
    IDungeonEntryIntentDomainService entryIntents,
    IDungeonVisitDomainService visits,
    ICharacterQuestLogDomainService questLogs,
    ICharacterFatigueDomainService fatigue,
    IGameMessageSender sender,
    INetworkConnectionCloser connectionCloser,
    TownAreaProjectionDelivery townAreaProjection,
    ILogger<SelectDungeonHandler> logger,
    EntryProjectionOptions? entryProjection = null,
    ILogger<EntryProjectionOptions>? entryProjectionLogger = null,
    IIllusionTowerDomainService? illusionTower = null,
    IllusionTowerProjectionDriver? illusionTowerProjection = null, ISaoTowerDomainService? sao = null,
    Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService? bossTower = null, BossTowerProjectionDriver? bossProjection = null) => new(characters,currentState,selectedSessions,currentViews,parties,runs,deliveryRegistry,entryIntents,visits,questLogs,fatigue,sender,connectionCloser,townAreaProjection,
        entryProjection?.Commands, illusionTower,bossTower,sao,entryProjection?.Ordering,entryProjection?.Inventory);
}
internal sealed class EntryProjectionOptions
{
    internal ICommandBus? Commands { get; }
    internal IOutboundOrdering? Ordering { get; }
    internal IInventorySlotProjectionDomainService? Inventory { get; }
    public EntryProjectionOptions(
    ICharacterDomainService characters,
    ICharacterStateProjectionDomainService currentState,
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveryRegistry,
    IDungeonEntryIntentDomainService entryIntents,
    IDungeonVisitDomainService visits,
    ICharacterFatigueDomainService fatigue,
    IGameMessageSender sender,
    INetworkConnectionCloser connectionCloser,
    TownAreaProjectionDelivery townAreaProjection,
    ILogger<EntryProjectionOptions> logger,
    ICommandBus? commands = null,
    IInventorySlotProjectionDomainService? inventoryProjection = null,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null,
    IOutboundOrdering? ordering = null) { Commands=commands; Ordering=ordering; Inventory=inventoryProjection; }
}
