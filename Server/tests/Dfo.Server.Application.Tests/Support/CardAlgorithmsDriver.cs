using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.DungeonClear;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Tests.Support;
// Direct domain fixtures execute the exact owning CMD algorithm, without claiming production scope evidence.
internal sealed record CardAwardAlgorithmInput(AccountId AccountId, CharacterId CharacterId, ClearRewardCardKind Kind,
    int Index, DungeonRunId ExpectedRunId = default) : ICommand<SelectCardResult>;
internal class CardAwardAlgorithmDriver(ICardRewardDomainService cards, IDungeonRunDomainService runs,
    ICharacterDomainService characters, TimeProvider clock, IItemDeliveryDomainService itemDelivery, ICardFlipCostDomainService flipCosts)
    : ICommandHandler<CardAwardAlgorithmInput, SelectCardResult>
{
    private readonly GameCardCommandHandler command = ProtocolAlgorithmFixture.Create<GameCardCommandHandler>(
        runs, new Dfo.Server.Application.World.CardFlipDomainService(runs, cards, characters, itemDelivery, flipCosts), clock);
    public ValueTask<SelectCardResult> HandleAsync(CommandExecutionContext c,CardAwardAlgorithmInput q,CancellationToken ct) =>
        command.AwardBodyAsync(q.AccountId,q.CharacterId,q.Kind,q.Index,q.ExpectedRunId,ct);
}
internal sealed record ClearExperienceAlgorithmInput(AccountId AccountId,CharacterId CharacterId,int DungeonId,
    ClearCircumstances Clear, SelectedSessionIdentity? Session=null,Guid RunId=default,int SettlementLevel=0,DateTimeOffset? SettledAt=null, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder? Footprint=null)
     : ICommand<ClearExperienceResult>,IDomainFixtureParticipants
{public IReadOnlyList<SelectedSessionIdentity> Participants=>Session is {} session?[session]:[];}
internal class ClearExperienceAlgorithmDriver(ICharacterDomainService grant,ICharacterExperienceAwardDomainService award,TimeProvider clock,ICharacterDungeonDifficultyDomainService difficulties,
    Dfo.Server.Abstractions.Events.IWeeklyAttendanceDomainService? attendance=null,IQuestProgressDomainService? quests=null,
    IDungeonRunDomainService? runs=null,IDespairTowerDomainService? despair=null,Dfo.Server.Abstractions.Rental.IAccountLuckyStarDomainService? luckyStars=null)
    : ICommandHandler<ClearExperienceAlgorithmInput, ClearExperienceResult>
{
    private readonly DungeonClearSettlementDomainService settlements = ProtocolAlgorithmFixture.Create<DungeonClearSettlementDomainService>(
        grant, award, runs, difficulties, clock, attendance, quests, despair, luckyStars);
    public ValueTask<ClearExperienceResult> HandleAsync(CommandExecutionContext c,ClearExperienceAlgorithmInput q,CancellationToken ct)
    {
        if(DomainCommandFixture.Kind(c)!=FixtureUnitKind.Transactional)throw new InvalidOperationException("Awarding experience requires a transaction.");
        if(q.Session is {} session&&(session.AccountId!=q.AccountId||session.CharacterId!=q.CharacterId))throw new InvalidOperationException("The fixture owner differs from its exact selected participant.");
        return Run();
        async ValueTask<ClearExperienceResult> Run()
        {
            using var fallback=new DomainCommandFixture.FootprintLease(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(q.AccountId,q.CharacterId,q.Session));
            return await settlements.GrantAsync(q.AccountId,q.CharacterId,q.DungeonId,q.Clear,q.Session,q.RunId,q.SettlementLevel,q.SettledAt,q.Footprint ?? fallback,ct);
        }
    }
}
internal sealed class CardTestOutbox(IGameMessageSender sender):IGameCommandOutbox
{
    public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage m,CancellationToken ct)=>sender.SendCmdAsync(id,m,ct);
    public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage m,CancellationToken ct)=>sender.SendNotiAsync(id,m,ct);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken ct)=>sender.SendBatchAsync(c,m,ct);
    public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery d,CancellationToken ct)=>sender.SendExplicitAsync(id,d,ct);
}
