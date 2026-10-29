using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Testing;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.ReturnToSelection;
namespace Dfo.Server.Application.Tests.Game;

internal interface ISelectionStage<TResult> where TResult:notnull;
internal interface ISelectionStageHandler<TStage,TResult> where TResult:notnull { ValueTask<TResult> HandleAsync(CommandExecutionContext context,TStage stage,CancellationToken ct); }
internal sealed record PrepareSelectedCharacterSessionStage(
    AccountId AccountId,
    ushort RosterIndex,
    ChannelEntryIdentity Channel,
    NetworkConnectionId ConnectionId,
    long ExpectedConnectionGeneration)
    : ISelectionStage<PreparedSelectedCharacterSession>;
internal sealed record CleanupExpiredCharacterItemsStage(SelectedSessionIdentity Session)
    : ISelectionStage<bool>;
internal sealed record PrepareSelectedCharacterStateStage(
    SelectedSessionIdentity Session,
    ActiveCharacterState Selected,
    ChannelEntryIdentity Channel) : ISelectionStage<SelectCharacterResult>;
internal sealed record ApplySelectedCharacterDailyRefillStage(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ISelectionStage<EntryInventoryPreparation>;
internal sealed record PublishSelectedCharacterSessionStage(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ISelectionStage<PublishedSelectedCharacterSession>;
internal sealed record MarkSelectedCharacterPvpReadyStage(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ISelectionStage<SelectedCharacterPvpPublication>;
internal sealed record SynchronizeSelectedCharacterQuestsStage(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ISelectionStage<SynchronizedSelectedCharacterQuests>;
internal sealed record ReviveSelectedCharacterPetStage(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ISelectionStage<RevivedSelectedCharacterPet>;
internal sealed record CompensateSelectedCharacterSessionStage(
    SelectedSessionIdentity Session)
    : ISelectionStage<CompensatedSelectedCharacterSession>;
internal sealed record CompensatedSelectedCharacterSession(
    NetworkConnectionId ConnectionId,
    SelectedSessionCleanupNotificationPlan Notifications);
internal sealed record PrepareReturnToSelectionStage(
    AccountId AccountId, NetworkConnectionId ConnectionId, long Generation)
    : ISelectionStage<PreparedReturnToSelection>;
internal sealed record CompleteReturnToSelectionStage(PreparedReturnToSelection Prepared)
    : ISelectionStage<CompletedReturnToSelection>;
internal sealed record CompletedReturnToSelection(SelectedSessionCleanupNotificationPlan? Notifications);
internal sealed record EnsureDailyContractPuppetsStage(SelectedSessionIdentity Session) : ISelectionStage<bool>;
internal sealed record InitializeHousingStage(SelectedSessionIdentity Session) : ISelectionStage<bool>;
internal sealed record RefillTauStage(SelectedSessionIdentity Session) : ISelectionStage<bool>;

internal sealed class PrepareSelectedCharacterSessionStageHandler(IOnlineUserQueryDomainService users,ISelectCharacterPreparationDomainService preparation,ISelectedSessionDomainService sessions,ISelectedSessionCleanupDomainService cleanup)
    : ISelectionStageHandler<PrepareSelectedCharacterSessionStage,PreparedSelectedCharacterSession>
{
    public ValueTask<PreparedSelectedCharacterSession> HandleAsync(CommandExecutionContext context,PrepareSelectedCharacterSessionStage stage,CancellationToken ct) =>
        ProductionSelectionStages.Select(users:users,preparation:preparation,sessions:sessions,cleanup:cleanup).PrepareSessionCoreAsync(context,null,stage.AccountId,stage.RosterIndex,stage.Channel,stage.ConnectionId,stage.ExpectedConnectionGeneration,ct);
}
internal sealed class CleanupExpiredCharacterItemsStageHandler(IExpiredCharacterItemCleanupDomainService expiredItems) : ISelectionStageHandler<CleanupExpiredCharacterItemsStage,bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context,CleanupExpiredCharacterItemsStage stage,CancellationToken ct) =>
        ProductionSelectionStages.Select(expiredItems:expiredItems).CleanupExpiredAsync(context,stage.Session,_=>{},ct);
}
internal sealed class PrepareSelectedCharacterStateStageHandler(ISelectCharacterPreparationDomainService preparation,ISelectCharacterEntryInventoryDomainService inventory)
    : ISelectionStageHandler<PrepareSelectedCharacterStateStage,SelectCharacterResult>
{
    public ValueTask<SelectCharacterResult> HandleAsync(CommandExecutionContext context,PrepareSelectedCharacterStateStage stage,CancellationToken ct) =>
        ProductionSelectionStages.Select(preparation:preparation,inventory:inventory).PrepareStateAsync(context,stage.Session,stage.Selected,stage.Channel,ct);
}
internal sealed class ApplySelectedCharacterDailyRefillStageHandler(ISelectCharacterEntryInventoryDomainService inventory) : ISelectionStageHandler<ApplySelectedCharacterDailyRefillStage,EntryInventoryPreparation>
{
    public ValueTask<EntryInventoryPreparation> HandleAsync(CommandExecutionContext context,ApplySelectedCharacterDailyRefillStage stage,CancellationToken ct) =>
        ProductionSelectionStages.Select(inventory:inventory).DailyRefillAsync(context,stage.Session,stage.State,ct);
}
internal sealed class PublishSelectedCharacterSessionStageHandler(ISelectedSessionDomainService sessions,ISelectCharacterSessionEntryDomainService entry) : ISelectionStageHandler<PublishSelectedCharacterSessionStage,PublishedSelectedCharacterSession>
{
    public ValueTask<PublishedSelectedCharacterSession> HandleAsync(CommandExecutionContext context,PublishSelectedCharacterSessionStage stage,CancellationToken ct) =>
        ProductionSelectionStages.Select(sessions:sessions,entry:entry).PublishAsync(stage.Session,stage.State,ct);
}
internal sealed class MarkSelectedCharacterPvpReadyStageHandler(ISelectCharacterSessionEntryDomainService entry) : ISelectionStageHandler<MarkSelectedCharacterPvpReadyStage,SelectedCharacterPvpPublication>
{
    public ValueTask<SelectedCharacterPvpPublication> HandleAsync(CommandExecutionContext context,MarkSelectedCharacterPvpReadyStage stage,CancellationToken ct) =>
        ProductionSelectionStages.Select(entry:entry).MarkPvpReadyAsync(stage.Session,stage.State,ct);
}
internal sealed class SynchronizeSelectedCharacterQuestsStageHandler(ISelectCharacterQuestEntryDomainService quests) : ISelectionStageHandler<SynchronizeSelectedCharacterQuestsStage,SynchronizedSelectedCharacterQuests>
{
    public ValueTask<SynchronizedSelectedCharacterQuests> HandleAsync(CommandExecutionContext context,SynchronizeSelectedCharacterQuestsStage stage,CancellationToken ct) =>
        ProductionSelectionStages.Select(quests:quests).SynchronizeQuestsAsync(context,stage.Session,stage.State,ct);
}
internal sealed class ReviveSelectedCharacterPetStageHandler(ISelectCharacterPetEntryDomainService pets) : ISelectionStageHandler<ReviveSelectedCharacterPetStage,RevivedSelectedCharacterPet>
{
    public ValueTask<RevivedSelectedCharacterPet> HandleAsync(CommandExecutionContext context,ReviveSelectedCharacterPetStage stage,CancellationToken ct) =>
        ProductionSelectionStages.Select(pets:pets).RevivePetAsync(context,stage.Session,stage.State,ct);
}
internal sealed class CompensateSelectedCharacterSessionStageHandler(ISelectedSessionCleanupDomainService cleanup,ISelectedSessionDomainService sessions) : ISelectionStageHandler<CompensateSelectedCharacterSessionStage,CompensatedSelectedCharacterSession>
{
    public async ValueTask<CompensatedSelectedCharacterSession> HandleAsync(CommandExecutionContext context,CompensateSelectedCharacterSessionStage stage,CancellationToken ct) =>
        new(stage.Session.ConnectionId,await ProductionSelectionStages.Select(sessions:sessions,cleanup:cleanup).CompensateAsync(context,stage.Session,ct));
}
internal sealed class PrepareReturnToSelectionStageHandler(IOnlineUserQueryDomainService users,ISelectedSessionDomainService sessions,ICharacterRosterDomainService characters) : ISelectionStageHandler<PrepareReturnToSelectionStage,PreparedReturnToSelection>
{
    public ValueTask<PreparedReturnToSelection> HandleAsync(CommandExecutionContext context,PrepareReturnToSelectionStage stage,CancellationToken ct) =>
        ProductionSelectionStages.Return(users,sessions,characters,null!).PrepareCoreAsync(stage.AccountId,stage.ConnectionId,stage.Generation,ct);
}
internal sealed class CompleteReturnToSelectionStageHandler(IOnlineUserQueryDomainService users,ISelectedSessionDomainService sessions,ISelectedSessionCleanupDomainService cleanup) : ISelectionStageHandler<CompleteReturnToSelectionStage,CompletedReturnToSelection>
{
    public async ValueTask<CompletedReturnToSelection> HandleAsync(CommandExecutionContext context,CompleteReturnToSelectionStage stage,CancellationToken ct) =>
        new(await ProductionSelectionStages.Return(users,sessions,null!,cleanup).CompleteAsync(context,stage.Prepared,ct));
}
// The isolation fixtures execute the production ownership and compensation algorithms. Only the
// transaction/footprint adapter is test owned; real runner and wire tests exercise production admission.
internal static class ProductionSelectionStages
{
    internal static GameSelectCharacterCommandHandler Select(IOnlineUserQueryDomainService? users=null,
        ISelectCharacterPreparationDomainService? preparation=null,ISelectedSessionDomainService? sessions=null,ISelectedSessionCleanupDomainService? cleanup=null,
        ISelectCharacterEntryInventoryDomainService? inventory=null,ISelectCharacterSessionEntryDomainService? entry=null,
        IExpiredCharacterItemCleanupDomainService? expiredItems=null,ISelectCharacterQuestEntryDomainService? quests=null,ISelectCharacterPetEntryDomainService? pets=null) =>
        new(users!,null!,null!,sessions!,new UnusedOutbox(),null!,null!,new CleanupAdapter(cleanup!),new InlineUnits(),preparation!,
            inventory!,entry!,quests!,pets!,expiredItems!,null!,null!,null!,new Dfo.Server.Protocol.Game.Party.PartyProjectionStreamRegistry(),null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GameSelectCharacterCommandHandler>.Instance,SelectCharacterDeliveryOptions.Everything,TimeProvider.System, lotteryCounterExperiment: ProtocolAlgorithmFixture.Default<LotteryCounterExperimentOptions>(), weaknessState: ProtocolAlgorithmFixture.Default<IWeaknessDomainService>(), itemUsage: ProtocolAlgorithmFixture.Default<IItemUsageDomainService>(), clientSettings: ProtocolAlgorithmFixture.Default<IClientSettingsDomainService>(), sao: ProtocolAlgorithmFixture.Default<ISaoTowerDomainService>(), tau: ProtocolAlgorithmFixture.Default<ITauKingdomDomainService>(), southern: ProtocolAlgorithmFixture.Default<ISouthernDaleDomainService>(), bossTickets: ProtocolAlgorithmFixture.Default<IBossTowerTicketDomainService>(), ordering: ProtocolAlgorithmFixture.Default<IOutboundOrdering>());
    internal static GameReturnToSelectionCommandHandler Return(IOnlineUserQueryDomainService users,ISelectedSessionDomainService sessions,
        ICharacterRosterDomainService characters,ISelectedSessionCleanupDomainService cleanup) =>
        new(users,sessions,characters,null!,new CleanupAdapter(cleanup),null!,null!,null!,null!,
            new Dfo.Server.Protocol.Game.Party.PartyProjectionStreamRegistry(),(Dfo.Server.Abstractions.Execution.IOutboundOrdering)ProtocolAlgorithmFixture.Default(typeof(Dfo.Server.Abstractions.Execution.IOutboundOrdering)));
    private sealed class InlineUnits : ICommandWorkUnits
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> action,CancellationToken ct=default) where T:notnull => action(null!,ct);
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CancellationToken,ValueTask<T>> action,CancellationToken ct=default) where T:notnull => action(ct);
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> action,CancellationToken ct=default) where T:notnull => throw new NotSupportedException();
    }
    internal sealed class CleanupAdapter(ISelectedSessionCleanupDomainService cleanup) : ICommandSelectedSessionCleanup
    {
        public void RecordDisconnectedDeliveryFailure(CommandExecutionContext context, Exception cause) => throw new NotSupportedException("This fixture only drives selection stages.");
        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(CommandExecutionContext context,SelectedSessionIdentity session,
            bool includeOwner,CommandWorkUnit? action=null,CancellationToken cancellationToken=default) =>
            Run(session,includeOwner,cancellationToken);
        private async ValueTask<SelectedSessionCleanupNotificationPlan> Run(SelectedSessionIdentity session,bool includeOwner,CancellationToken ct)
        {using var footprint=new Dfo.Server.Application.Tests.Support.TestEffectFootprints.Lease(session);return await cleanup.CleanupAsync(session,includeOwner,footprint,ct);}
    }
    private sealed class UnusedOutbox : Dfo.Server.Protocol.Game.IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id,Dfo.Server.Protocol.Game.IGameCmdMessage m,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask RegisterNotiAsync(NetworkConnectionId id,Dfo.Server.Protocol.Game.IGameNotiMessage m,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<Dfo.Server.Protocol.Game.IGameServerMessage> m,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id,Dfo.Server.Protocol.Game.GameExplicitMessageDelivery m,CancellationToken ct)=>throw new NotSupportedException();
    }
}
