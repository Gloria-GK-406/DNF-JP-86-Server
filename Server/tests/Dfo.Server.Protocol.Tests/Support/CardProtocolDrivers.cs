using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Protocol.Tests.Support;
// Existing direct protocol fixtures: no claim of production CMD scope/admission.
internal sealed class TestLogger<T>(ILogger actual):ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>actual.BeginScope(state);
    public bool IsEnabled(LogLevel level)=>actual.IsEnabled(level);
    public void Log<TState>(LogLevel l,EventId e,TState s,Exception? x,Func<TState,Exception?,string> f)=>actual.Log(l,e,s,x,f);
}
internal class SetPlayResultDriver(ISelectedSessionDomainService selectedSessions,ISelectedCharacterCurrentViewDomainService views,
    ICharacterDomainService characters,IDungeonRunDomainService runs,IDungeonSettlementRuleService rules,
    ICardRewardDomainService cards,ICommandBus bus,IQuestOfferRefreshDomainService offers,IGameMessageSender sender,
    ILogger<SetPlayResultHandler> logger)
    : GameSetPlayResultCommandHandler(selectedSessions,views,characters,runs,cards,null!,null!,TimeProvider.System,null!,null!,null!,null!,offers,
        new CardTestOutbox(sender),new TestLogger<GameSetPlayResultCommandHandler>(logger),null!)
{
    protected override ValueTask<DungeonSettlementRules> RulesAsync(SelectedSessionIdentity session,DungeonRunState run,CancellationToken ct)=>rules.ForAsync(session.AccountId,session.CharacterId,run,ct);
    protected override ValueTask<ClearExperienceResult> GrantAsync(CommandExecutionContext execution,SelectedSessionRecipient actor,DungeonRunState run,ClearCircumstances clear,int level,CancellationToken ct)=>
        bus.SendAsync(new ClearExperienceAlgorithmInput(actor.Session.AccountId,actor.Session.CharacterId,run.Layout.DungeonId,clear,actor.Session,run.RunId.Value,level),ct);
    public ValueTask HandleAsync(NetworkDispatchContext context,SetPlayResultRequest input,CancellationToken ct)=>RunAsync(
        new(Guid.NewGuid(),typeof(GameSetPlayResultCommand),typeof(GameSetPlayResultCommandHandler)),context,input,ct);
}
internal class CardWireDriver(ISelectedSessionDomainService sessions,ISelectedCharacterCurrentViewDomainService views,
    IDungeonRunDomainService runs,ICommandBus bus,IGameMessageSender sender,ILogger logger,
    CardSelectionProjectionFixture? projection=null,IDungeonCardPresentationControl? control=null)
    : GameCardCommandHandler(sessions,views,runs,null!,null!,null!,null!,null!,null!,new CardTestOutbox(sender),control!,null!,null!,TimeProvider.System,new TestLogger<GameCardCommandHandler>(logger))
{
    protected override bool Admitted(SelectedSessionIdentity session,DungeonRunId runId)=>true;
    protected override ValueTask<SelectCardResult> AwardAsync(CommandExecutionContext execution,AccountId account,CharacterId character,ClearRewardCardKind kind,int index,DungeonRunId run,CancellationToken ct)=>
        bus.SendAsync(new CardAwardAlgorithmInput(account,character,kind,index,run),ct);
    protected override async ValueTask ProjectAsync(SelectedSessionIdentity session,SelectCardResult result,DateTimeOffset at,CancellationToken ct)
    {
        if(projection is not null){await projection.DeliverAsync(session,result,at,ct);return;}
        var delivery=DungeonCardSelectionProjection.CreateDelivery(Sessions,Runs,[session],session,result,at,out _);
        if(delivery is not null)await sender.SendExplicitAsync(session.ConnectionId,delivery,ct);
    }
    public ValueTask HandleAsync(NetworkDispatchContext context,SelectCardRequest input,CancellationToken ct)=>SelectAsync(
        new(Guid.NewGuid(),typeof(GameSelectCardCommand),typeof(GameCardCommandHandler)),context,input,ct);
    public ValueTask HandleAsync(NetworkDispatchContext context,ScoreScrollStateRequest input,CancellationToken ct)=>ScrollAsync(context,input,ct);
    public ValueTask HandleAsync(NetworkDispatchContext context,CardSelectRightStateRequest input,CancellationToken ct)=>RightAsync(context,input,ct);
    internal ValueTask OpenAsync(SelectedSessionIdentity session,DungeonRunId run,CancellationToken ct)=>OpenLayoutAsync(session,run,ct);
}
internal sealed class SelectCardDriver(ISelectedSessionDomainService sessions,ISelectedCharacterCurrentViewDomainService views,
    IDungeonRunDomainService runs,ICommandBus bus,IGameMessageSender sender,ILogger<SelectCardHandler> logger,
    CardSelectionProjectionFixture? projection=null,IDungeonCardPresentationControl? cardPresentation=null,DungeonPartyRecipients? partyRecipients=null)
    : CardWireDriver(sessions,views,runs,bus,sender,logger,projection,cardPresentation)
{
    protected override bool Admitted(SelectedSessionIdentity session,DungeonRunId runId)=>partyRecipients?.Find(session,runId).Contains(session)??true;
}
internal sealed class ScoreScrollDriver(ISelectedSessionDomainService sessions,IGameMessageSender sender,ILogger<ScoreScrollStateHandler> logger,
    IDungeonCardPresentationControl? cardPresentation=null,IDungeonRunDomainService? runs=null)
    : CardWireDriver(sessions,null!,runs!,null!,sender,logger,control:cardPresentation);
internal sealed class CardRightDriver(ISelectedSessionDomainService sessions,IGameMessageSender sender,ILogger<CardSelectRightStateHandler> logger,
    IDungeonRunDomainService? runs=null,IDungeonCardPresentationControl? cardPresentation=null)
    : CardWireDriver(sessions,null!,runs!,null!,sender,logger,control:cardPresentation);
internal sealed class BossDieCheckDriver(ISelectedSessionDomainService sessions,IDungeonRunDomainService runs,ILogger<BossDieCheckHandler> logger)
    : GameBossDieCheckCommandHandler(sessions,runs,new TestLogger<GameBossDieCheckCommandHandler>(logger))
{
    public ValueTask HandleAsync(NetworkDispatchContext context,BossDieCheckRequest input,CancellationToken ct)=>RunAsync(context,input,ct);
}
