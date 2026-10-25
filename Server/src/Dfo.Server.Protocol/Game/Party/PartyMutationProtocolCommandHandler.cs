using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Party;

internal abstract partial class PartyMutationProtocolCommandHandler : PartyProtocolCommandProjection
{
    protected readonly ISelectedSessionDomainService sessions;
    protected readonly ITownAreaDomainService townAreas;
    protected readonly ICommandProjections projections;
    protected readonly Func<long, IPartyDeliveryStream> streams;
    protected readonly ILogger logger;
    protected readonly ISelectedCharacterCurrentViewDomainService currentViews;
    protected readonly ICharacterStateProjectionDomainService currentState;
    protected readonly ICharacterDomainService characters;
    protected readonly IOnlineCharacterRuntimeDomainService onlineCharacters;
    protected readonly ITownMovementDomainService towns;
    protected readonly IMarriageHousingQueryDomainService? housing;
    protected readonly IBossTowerDomainService bossTowers;
    protected readonly IIllusionTowerDomainService illusionTowers;
    protected readonly TimeProvider clock;
    protected readonly Func<NetworkConnectionId, GameExplicitMessageDelivery, CancellationToken, ValueTask> register;
    protected readonly Func<SelectedSessionIdentity, ValueTask> close;
    protected readonly Func<SelectedSessionIdentity, CancellationToken, ValueTask>? weakness;
    private static readonly IInventorySlotMappingRuleService SlotMappings = new InventorySlotMappingRuleService();

    protected PartyMutationProtocolCommandHandler(
    ISelectedSessionDomainService sessions, IPartyService? parties, ITownAreaDomainService? townAreas,
    ICommandProjections projections, Func<long, IPartyDeliveryStream> streams, IOutboundOrdering? ordering,
    ILogger logger, ISelectedCharacterCurrentViewDomainService? currentViews = null,
    ICharacterStateProjectionDomainService? currentState = null, ICharacterDomainService? characters = null,
    IOnlineCharacterRuntimeDomainService? onlineCharacters = null, ITownMovementDomainService? towns = null,
    IMarriageHousingQueryDomainService? housing = null, IBossTowerDomainService? bossTowers = null,
    IIllusionTowerDomainService? illusionTowers = null, TimeProvider? clock = null,
    Func<NetworkConnectionId, GameExplicitMessageDelivery, CancellationToken, ValueTask>? register = null,
    Func<SelectedSessionIdentity, ValueTask>? close = null,
    Func<SelectedSessionIdentity, CancellationToken, ValueTask>? weakness = null,
    ICommandWorkUnits? workUnits = null, IOnlineUserQueryDomainService? onlineUsers = null,
    IDungeonRunDomainService? dungeonRuns = null, IDungeonRunParticipantDepartureSink? departureSink = null)
        : base(projections, streams, parties, ordering)
    {
        this.sessions = sessions!;
        this.townAreas = townAreas!;
        this.projections = projections!;
        this.streams = streams!;
        this.logger = logger!;
        this.currentViews = currentViews!;
        this.currentState = currentState!;
        this.characters = characters!;
        this.onlineCharacters = onlineCharacters!;
        this.towns = towns!;
        this.housing = housing!;
        this.bossTowers = bossTowers!;
        this.illusionTowers = illusionTowers!;
        this.clock = clock ?? TimeProvider.System;
        this.register = register!;
        this.close = close!;
        this.weakness = weakness!;
        this.workUnits = workUnits;
        this.onlineUsers = onlineUsers;
        this.dungeonRuns = dungeonRuns;
        this.departureSink = departureSink;
    }
}
