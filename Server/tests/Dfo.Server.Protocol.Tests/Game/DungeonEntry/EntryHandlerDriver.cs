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
// Minimal algorithm driver retains the old characterization fixtures. Actual transaction,
// cleanup, physical sender and registration behavior is separately covered by Task43 wire oracles.
internal sealed record EntryRouteProbe(DungeonEntryContext Entry) : ICommand<DungeonEntryAttempt>;
internal sealed record BossEntryRouteProbe(IReadOnlyList<TowerEntryMember> Members, SelectedSessionIdentity Leader, uint Seed, byte RequestMode, byte Difficulty, byte PracticeMode) : ICommand<BossTowerEntryResult>;
internal sealed record IllusionEntryRouteProbe(IReadOnlyList<TowerEntryMember> Members, SelectedSessionIdentity Leader, uint Seed, int DungeonId = 11001) : ICommand<TowerEntryResult>;
internal class EntryHandlerDriver(
    ICharacterDomainService characters, ICharacterStateProjectionDomainService current, ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService views, IPartyService parties, IDungeonRunDomainService runs, DungeonRunDeliveryRegistry registry,
    IDungeonEntryIntentDomainService intents, IDungeonVisitDomainService visits, ICharacterQuestLogDomainService logs,
    ICharacterFatigueDomainService fatigue, IGameMessageSender sender, INetworkConnectionCloser closer, TownAreaProjectionDelivery town,
    ICommandBus? admission = null, IIllusionTowerDomainService? illusion = null, IBossTowerDomainService? boss = null,
    ISaoTowerDomainService? sao = null, IOutboundOrdering? ordering = null, IInventorySlotProjectionDomainService? inventory = null, IMarriageHousingQueryDomainService? housing = null)
    : GameSelectDungeonCommandHandler(sessions, views, null!, null!, parties, new PartyProjectionStreamRegistry(), current, characters, null!, registry, null!, new EntryOutbox(sender), new EntryUnits(), null!, null!, NullLogger<GameSelectDungeonCommandHandler>.Instance, boss!, illusion!, housing!, TimeProvider.System, visits, runs, intents, logs, fatigue, inventory!, ordering!, new Dfo.Server.Application.World.DungeonEntryDecisionDomainService(), null!, null!, null!, null!, null!, null!, null!, sao!, null!, null!, null!, null!, null!, null!)
{
    private CommandExecutionContext Context(NetworkDispatchContext dispatch, SelectDungeonRequest input) => new(Guid.NewGuid(), typeof(GameSelectDungeonCommand), GetType(), CommandExecutionKind.Direct) { Request = new CommandRequestContext.ProtocolNonEffect(dispatch,input,new ProtocolFrameMetadata.Game(input.Metadata),()=>true) };
    public ValueTask HandleAsync(NetworkDispatchContext dispatch, SelectDungeonRequest input, CancellationToken token) => SelectDungeonAsync(Context(dispatch,input),dispatch,input,token);
    public ValueTask<bool> EnterBoss(NetworkConnectionId id, SelectedSessionIdentity requester, PartySnapshot roster, DungeonEntryIntent intent, Action restore, CancellationToken token, byte requestMode=0,byte difficulty=0,byte practiceMode=0) => EnterBossAsync(null!,id,requester,roster,intent,restore,token,requestMode,difficulty,practiceMode);
    public ValueTask<bool> EnterIllusion(NetworkConnectionId id, SelectedSessionIdentity requester, PartySnapshot roster, DungeonEntryIntent intent, Action restore, CancellationToken token,int dungeonId=11001) => EnterIllusionAsync(null!,id,requester,roster,intent,restore,token,dungeonId);
    protected override ValueTask<DungeonEntryAttempt> RouteEntryAsync(DungeonEntryContext entry, CancellationToken token) => admission is null ? base.RouteEntryAsync(entry,token) : admission.SendAsync(new EntryRouteProbe(entry),token);
    protected override ValueTask<BossTowerEntryResult> AdmitBossAsync(CommandExecutionContext e,IReadOnlyList<TowerEntryMember> m,SelectedSessionIdentity q,uint seed,CancellationToken ct,byte mode,byte difficulty,byte practice) => admission!.SendAsync(new BossEntryRouteProbe(m,q,seed,mode,difficulty,practice),ct);
    protected override ValueTask<TowerEntryResult> AdmitIllusionAsync(CommandExecutionContext e,IReadOnlyList<TowerEntryMember> m,SelectedSessionIdentity q,uint seed,CancellationToken ct,int dungeon) => admission!.SendAsync(new IllusionEntryRouteProbe(m,q,seed,dungeon),ct);
    protected override ValueTask<IReadOnlyList<TownAreaMembershipChange>> TownLeaveManyAsync(IReadOnlyList<SelectedSessionIdentity> members,TownAreaDepartureReason reason,CancellationToken ct)=>town.LeaveManyAsync(members,reason,ct);
    protected override ValueTask CloseEntryAsync(SelectedSessionIdentity member, NetworkConnectionContext? captured)=>closer.RequestCloseAsync(member.ConnectionId,CancellationToken.None);
}
internal sealed class EntryUnits : ICommandWorkUnits
{
    public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>a(null!,ct);
    public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
    public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
}
internal sealed class EntryOutbox(IGameMessageSender sender):IGameCommandOutbox
{public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage m,CancellationToken ct)=>sender.SendCmdAsync(id,m,ct);public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage m,CancellationToken ct)=>sender.SendNotiAsync(id,m,ct);public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken ct)=>sender.SendBatchAsync(c,m,ct);public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery d,CancellationToken ct)=>sender.SendExplicitAsync(id,d,ct);}
