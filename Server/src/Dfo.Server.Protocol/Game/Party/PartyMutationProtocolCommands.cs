using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Party;

internal sealed record GameLeavePartyCommand(NetworkDispatchContext Dispatch, LeavePartyRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed record GameRemovePartyMemberCommand(NetworkDispatchContext Dispatch, RemovePartyMemberRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed record GameChangePartyLeaderCommand(NetworkDispatchContext Dispatch, ChangePartyLeaderRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed partial class GamePartyMutationCommandHandler :
    ICommandHandler<GameLeavePartyCommand, bool>, ICommandHandler<GameRemovePartyMemberCommand, bool>,
    ICommandHandler<GameChangePartyLeaderCommand, bool>
{
    private readonly ISelectedSessionDomainService sessions;
    private readonly ITownAreaDomainService townAreas;
    private readonly ICommandProjections projections;
    private readonly IPartyProjectionStateDomainService projectionState;
    private readonly ILogger logger;
    private readonly ISelectedCharacterCurrentViewDomainService currentViews;
    private readonly ICharacterStateProjectionDomainService currentState;
    private readonly ICharacterDomainService characters;
    private readonly IOnlineCharacterRuntimeDomainService onlineCharacters;
    private readonly ITownMovementDomainService towns;
    private readonly IMarriageHousingQueryDomainService housing;
    private readonly IBossTowerDomainService bossTowers;
    private readonly IIllusionTowerDomainService illusionTowers;
    private readonly TimeProvider clock;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandConnectionClosures closures;
    private readonly IWeaknessDomainService weaknessState;
    private readonly ICommandWorkUnits workUnits;
    private readonly IOnlineUserQueryDomainService onlineUsers;
    private readonly IDungeonRunDomainService dungeonRuns;
    private readonly IDungeonRunProjectionDomainService departureSink;
    private readonly IPartyService parties;
    private readonly IOutboundOrdering ordering;
    private const bool projectWeakness = true;

    public GamePartyMutationCommandHandler(ISelectedSessionDomainService sessions, IPartyService parties,
        ITownAreaDomainService townAreas, ICommandProjections projections, IPartyProjectionStateDomainService projectionState,
        ILogger<GamePartyMutationCommandHandler> logger, IOutboundOrdering ordering,
        ISelectedCharacterCurrentViewDomainService currentViews, ICharacterStateProjectionDomainService currentState,
        ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService onlineCharacters, ITownMovementDomainService towns,
        IMarriageHousingQueryDomainService housing, IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        TimeProvider clock, IGameCommandOutbox outbox, ICommandConnectionClosures closures,
        IOnlineUserQueryDomainService onlineUsers, IWeaknessDomainService weakness, ICommandWorkUnits workUnits,
        IDungeonRunDomainService runs, IDungeonRunProjectionDomainService departureSink)

    {
        this.sessions = sessions; this.townAreas = townAreas; this.projections = projections; this.projectionState = projectionState; this.logger = logger; this.currentViews = currentViews; this.currentState = currentState; this.characters = characters; this.onlineCharacters = onlineCharacters; this.towns = towns; this.housing = housing; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; this.clock = clock; this.outbox = outbox; this.closures = closures; this.weaknessState = weakness; this.workUnits = workUnits; this.onlineUsers = onlineUsers; this.dungeonRuns = runs; this.departureSink = departureSink; this.parties = parties; this.ordering = ordering;
    }

    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameLeavePartyCommand command, CancellationToken token) =>
        HandleMutationAsync(execution, command.Dispatch, command.Input.Metadata, PartyChange.Leave, null, token);
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameRemovePartyMemberCommand command, CancellationToken token) =>
        HandleMutationAsync(execution, command.Dispatch, command.Input.Metadata, PartyChange.Remove, command.Input.TargetSlot, token);
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameChangePartyLeaderCommand command, CancellationToken token) =>
        HandleMutationAsync(execution, command.Dispatch, command.Input.Metadata, PartyChange.Transfer, command.Input.TargetSlot, token);
}
