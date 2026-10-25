using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;
namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;
internal static class TowerProjectionTestFactory
{
    internal static BossTowerProjectionDriver BossLegacy(
    IBossTowerDomainService towers,
    ICommandBus commands,
    ISelectedSessionDomainService sessions,
    IPartyService parties,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterStateProjectionDomainService currentState,
    ICharacterDomainService characters,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    IDungeonEntryIntentDomainService entryIntents,
    IGameMessageSender sender,
    TownAreaProjectionDelivery townProjection,
    TimeProvider clock,
    INetworkConnectionCloser connections,
    ILogger<BossTowerProjectionDriver> logger,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null,
    IOutboundOrdering? ordering = null) => new(towers,commands as ITowerAlgorithmMutations,sessions,currentViews,currentState,characters,onlineCharacters,sender,townProjection,clock,connections,logger);
    internal static BossEntry Boss(
    IBossTowerDomainService towers,
    ICommandBus commands,
    ISelectedSessionDomainService sessions,
    IPartyService parties,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterStateProjectionDomainService currentState,
    ICharacterDomainService characters,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    IDungeonEntryIntentDomainService entryIntents,
    IGameMessageSender sender,
    TownAreaProjectionDelivery townProjection,
    TimeProvider clock,
    INetworkConnectionCloser connections,
    ILogger<BossTowerProjectionDriver> logger,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null,
    IOutboundOrdering? ordering = null) => new(new EntryHandlerDriver(characters,currentState,sessions,currentViews,parties,null!,new DungeonRunDeliveryRegistry(),entryIntents,null!,null!,null!,sender,connections,townProjection,commands,
        null,towers,ordering:ordering,housing:housing));
    internal sealed class BossEntry(EntryHandlerDriver handler)
    {
        public ValueTask<bool> EnterAsync(NetworkConnectionId id,SelectedSessionIdentity requester,PartySnapshot roster,DungeonEntryIntent intent,Action restore,CancellationToken token, byte requestMode=0, byte difficulty=0, byte practiceMode=0) => handler.EnterBoss(id,requester,roster,intent,restore,token,requestMode,difficulty,practiceMode);
    }
    internal static IllusionTowerProjectionDriver IllusionLegacy(
    IIllusionTowerDomainService towers,
    ICommandBus commands,
    ISelectedSessionDomainService sessions,
    IPartyService parties,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterStateProjectionDomainService currentState,
    ICharacterDomainService characters,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    IDungeonEntryIntentDomainService entryIntents,
    IGameMessageSender sender,
    TownAreaProjectionDelivery townProjection,
    TimeProvider clock,
    INetworkConnectionCloser connections,
    ILogger<IllusionTowerProjectionDriver> logger,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null,
    IOutboundOrdering? ordering = null) => new(towers,commands as ITowerAlgorithmMutations,sessions,currentViews,currentState,characters,onlineCharacters,sender,townProjection,clock,connections,logger);
    internal static IllusionEntry Illusion(
    IIllusionTowerDomainService towers,
    ICommandBus commands,
    ISelectedSessionDomainService sessions,
    IPartyService parties,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterStateProjectionDomainService currentState,
    ICharacterDomainService characters,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    IDungeonEntryIntentDomainService entryIntents,
    IGameMessageSender sender,
    TownAreaProjectionDelivery townProjection,
    TimeProvider clock,
    INetworkConnectionCloser connections,
    ILogger<IllusionTowerProjectionDriver> logger,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null,
    IOutboundOrdering? ordering = null) => new(new EntryHandlerDriver(characters,currentState,sessions,currentViews,parties,null!,new DungeonRunDeliveryRegistry(),entryIntents,null!,null!,null!,sender,connections,townProjection,commands,
        towers,null,ordering:ordering,housing:housing));
    internal sealed class IllusionEntry(EntryHandlerDriver handler)
    {
        public ValueTask<bool> EnterAsync(NetworkConnectionId id,SelectedSessionIdentity requester,PartySnapshot roster,DungeonEntryIntent intent,Action restore,CancellationToken token, int dungeonId=11001) => handler.EnterIllusion(id,requester,roster,intent,restore,token,dungeonId);
    }
}
