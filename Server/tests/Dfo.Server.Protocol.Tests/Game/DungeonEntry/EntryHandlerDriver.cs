
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

{
    private Action? restoreObserved;
    private GameSelectDungeonCommandHandler? cached;
    private GameSelectDungeonCommandHandler handler => cached ??= new GameSelectDungeonCommandHandler(sessions,views,ProtocolAlgorithmFixture.Default<IOnlineCharacterRuntimeDomainService>(),ProtocolAlgorithmFixture.Field<ITownAreaDomainService>(town),parties,new PartyProjectionStreamRegistry(),current,characters,ProtocolAlgorithmFixture.Field<ITownMovementDomainService>(town),registry,new Dfo.Server.Protocol.Game.LegacyCommandProjections(ProtocolAlgorithmFixture.Field<Dfo.Server.Abstractions.DomainEvents.IEventService>(town)),new EntryOutbox(sender),new EntryUnits(),new global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Closures(closer),global::Dfo.Server.Testing.ProtocolAlgorithmFixture.QueryFor(sessions),NullLogger<GameSelectDungeonCommandHandler>.Instance,BossAdmission(boss ?? ProtocolAlgorithmFixture.Default<IBossTowerDomainService>(), admission),IllusionAdmission(illusion ?? ProtocolAlgorithmFixture.Default<IIllusionTowerDomainService>(), admission),(housing ?? global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Field<global::Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService>(town)),TimeProvider.System,visits,runs,ProtocolAlgorithmFixture.Decorate(intents, (method, args) => { if (method.Name == nameof(IDungeonEntryIntentDomainService.RestoreIfAbsent)) restoreObserved?.Invoke(); return (false, null); }),logs,fatigue,(inventory ?? ProtocolAlgorithmFixture.Default<IInventorySlotProjectionDomainService>()),(ordering ?? ProtocolAlgorithmFixture.Default<IOutboundOrdering>()),new Dfo.Server.Application.World.DungeonEntryDecisionDomainService(),RoutedAdmission(ProtocolAlgorithmFixture.Default<IHellDungeonEntryDomainService>(), admission),ProtocolAlgorithmFixture.Default<ICycleDungeonEntryDomainService>(),RoutedAdmission(ProtocolAlgorithmFixture.Default<IMirrorDungeonEntryDomainService>(), admission),OrdinaryAdmission(visits, admission),ProtocolAlgorithmFixture.Default<IDespairTowerDomainService>(),ProtocolAlgorithmFixture.Default<ITournamentDungeonDomainService>(),ProtocolAlgorithmFixture.Default<IAltarDungeonDomainService>(),(sao ?? ProtocolAlgorithmFixture.Default<ISaoTowerDomainService>()),ProtocolAlgorithmFixture.Default<ITauKingdomDomainService>(),ProtocolAlgorithmFixture.Default<IHerasDungeonDomainService>(),ProtocolAlgorithmFixture.Default<ICoupleDungeonDomainService>(),ProtocolAlgorithmFixture.Default<ISouthernDaleDomainService>(),ProtocolAlgorithmFixture.Default<ISealedTreasureDomainService>(),ProtocolAlgorithmFixture.Default<IGoldSilverHornDomainService>());
    private CommandExecutionContext Context(NetworkDispatchContext dispatch, SelectDungeonRequest input) => new(Guid.NewGuid(), typeof(GameSelectDungeonCommand), GetType()) { Request = new CommandRequestContext.ProtocolNonEffect(dispatch,input,new ProtocolFrameMetadata.Game(input.Metadata),()=>true) };
    public ValueTask HandleAsync(NetworkDispatchContext dispatch, SelectDungeonRequest input, CancellationToken token) => handler.SelectDungeonAsync(Context(dispatch,input),dispatch,input,token);
    public ValueTask<bool> EnterBoss(NetworkConnectionId id, SelectedSessionIdentity requester, PartySnapshot roster, DungeonEntryIntent intent, Action restore, CancellationToken token, byte requestMode=0,byte difficulty=0,byte practiceMode=0) { restoreObserved = restore; return handler.EnterBossAsync(null!,id,requester,roster,intent,token,requestMode,difficulty,practiceMode); }
    public ValueTask<bool> EnterIllusion(NetworkConnectionId id, SelectedSessionIdentity requester, PartySnapshot roster, DungeonEntryIntent intent, Action restore, CancellationToken token,int dungeonId=11001) { restoreObserved = restore; return handler.EnterIllusionAsync(null!,id,requester,roster,intent,token,dungeonId); }
    private static T Argument<T>(object?[] args, int index) => args[index] is T value ? value
        : throw new InvalidOperationException($"Expected {typeof(T).Name} at domain argument {index}.");

    private static T RoutedAdmission<T>(T actual, ICommandBus? admission) where T : class =>
        admission is null ? actual : ProtocolAlgorithmFixture.Decorate(actual, (method, args) =>
            method.Name is "EnterAsync" or "EnterManualAsync" or "EnterRandomAsync"
                ? (true, admission.SendAsync(new EntryRouteProbe(Argument<DungeonEntryContext>(args, 0)), Argument<CancellationToken>(args, 1)))
                : (false, null));

    private static IDimensionDungeonDomainService OrdinaryAdmission(IDungeonVisitDomainService visits, ICommandBus? admission)
    {
        var actual = ProtocolAlgorithmFixture.NoDimension(visits);
        return admission is null ? actual : ProtocolAlgorithmFixture.Decorate(actual, (method, args) =>
            method.Name == nameof(IDimensionDungeonDomainService.EnterAsync)
                ? (true, admission.SendAsync(new EntryRouteProbe(Argument<DungeonEntryContext>(args, 0)), Argument<CancellationToken>(args, 1)))
                : (false, null));
    }
    private static IBossTowerDomainService BossAdmission(IBossTowerDomainService actual, ICommandBus? admission) =>
        admission is null ? actual : ProtocolAlgorithmFixture.Decorate(actual, (method, args) =>
            method.Name == nameof(IBossTowerDomainService.EnterAsync)
                ? (true, admission.SendAsync(new BossEntryRouteProbe(Argument<IReadOnlyList<TowerEntryMember>>(args, 0),
                    Argument<SelectedSessionIdentity>(args, 1), Argument<uint>(args, 2), Argument<byte>(args, 4), Argument<byte>(args, 5), Argument<byte>(args, 6)), Argument<CancellationToken>(args, 3)))
                : (false, null));
    private static IIllusionTowerDomainService IllusionAdmission(IIllusionTowerDomainService actual, ICommandBus? admission) =>
        admission is null ? actual : ProtocolAlgorithmFixture.Decorate(actual, (method, args) =>
            method.Name == nameof(IIllusionTowerDomainService.EnterAsync)
                ? (true, admission.SendAsync(new IllusionEntryRouteProbe(Argument<IReadOnlyList<TowerEntryMember>>(args, 0),
                    Argument<SelectedSessionIdentity>(args, 1), Argument<uint>(args, 2), Argument<int>(args, 4)), Argument<CancellationToken>(args, 3)))
                : (false, null));

}
internal sealed class EntryUnits : ICommandWorkUnits
{
    public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>a(null!,ct);
    public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
    public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
}
internal sealed class EntryOutbox(IGameMessageSender sender):IGameCommandOutbox
{public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage m,CancellationToken ct)=>sender.SendCmdAsync(id,m,ct);public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage m,CancellationToken ct)=>sender.SendNotiAsync(id,m,ct);public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken ct)=>sender.SendBatchAsync(c,m,ct);public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery d,CancellationToken ct)=>sender.SendExplicitAsync(id,d,ct);}
