using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Rental;
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
internal sealed class SetPlayResultDriver(ISelectedSessionDomainService selectedSessions, ISelectedCharacterCurrentViewDomainService views,
    ICharacterDomainService characters, IDungeonRunDomainService runs, IDungeonSettlementRuleService rules,
    ICardRewardDomainService cards, ICommandBus bus, IQuestOfferRefreshDomainService offers, IGameMessageSender sender,
    ILogger<SetPlayResultHandler> logger)
{
    private readonly GameSetPlayResultCommandHandler command = ProtocolAlgorithmFixture.Create<GameSetPlayResultCommandHandler>(
        selectedSessions, views, characters, SettlementRuns.Wrap(runs, rules),
        ProtocolAlgorithmFixture.Create<Dfo.Server.Application.World.DungeonClearSettlementDomainService>(
            selectedSessions, characters, SettlementRuns.Wrap(runs, rules), cards), offers,
        new ClearResultUnit(bus, runs, characters), new CardTestOutbox(sender), new TestLogger<GameSetPlayResultCommandHandler>(logger));
    public async ValueTask HandleAsync(NetworkDispatchContext context, SetPlayResultRequest input, CancellationToken ct)
    {
        var request = new GameSetPlayResultCommand(context, input);
        _ = await command.HandleAsync(ComponentContext.For(request, command.GetType()), request, ct);
    }
}

// Decorates the existing rule lookup at its actual domain boundary. Every other call reaches
// the supplied run owner; the fixture neither implements nor repeats result coordination.
internal class SettlementRuns : System.Reflection.DispatchProxy
{
    private IDungeonRunDomainService inner = null!;
    private IDungeonSettlementRuleService rules = null!;
    internal static IDungeonRunDomainService Wrap(IDungeonRunDomainService inner, IDungeonSettlementRuleService rules)
    {
        var proxy = Create<IDungeonRunDomainService, SettlementRuns>();
        ((SettlementRuns)proxy).inner = inner;
        ((SettlementRuns)proxy).rules = rules;
        return proxy;
    }
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        if (method is null || args is null) throw new NotSupportedException();
        if (method.Name == nameof(IDungeonRunDomainService.SettlementRulesForAsync))
            return rules.ForAsync((AccountId)args[0]!, (CharacterId)args[1]!, (DungeonRunState)args[2]!, (CancellationToken)args[3]!);
        try { return method.Invoke(inner, args); }
        catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
}

internal static class ComponentContext
{
    internal static CommandExecutionContext For<T>(IProtocolCommand<T> command, Type handler) where T : notnull =>
        new(Guid.NewGuid(), command.GetType(), handler)
        {
            Request = new CommandRequestContext.ProtocolNonEffect(command.Dispatch, command.Message, command.Frame, () => true),
        };
}

// Existing result fixtures supply only their named settlement result. Unknown units or kinds
// fail; actual domain and wire fixtures retain their real transaction implementation.
internal sealed class ClearResultUnit(ICommandBus bus, IDungeonRunDomainService runs, ICharacterDomainService characters) : ICommandWorkUnits
{
    public async ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull
    {
        if (declaration.Name != "grant-clear-experience" || typeof(T) != typeof(ClearExperienceResult)
            || context.Request is not CommandRequestContext.ProtocolNonEffect { Message: SetPlayResultRequest input })
            throw new NotSupportedException("Only the clear-result fixture's award is supplied.");
        var actor = declaration.Participants.Single().Session;
        var run = runs.Find(actor.CharacterId)!;
        var level = (await characters.Get(actor.CharacterId).ProgressAsync(ct))!.Level;
        var result = await bus.SendAsync(new ClearExperienceAlgorithmInput(actor.AccountId, actor.CharacterId,
            run.Layout.DungeonId, new ClearCircumstances(run.Layout.BasisLevel, run.Difficulty, run.Layout.ExperienceWeight,
                input.ClientRankPoint, run.KillSummary.Total, run.PartyMemberCount), actor, run.RunId.Value, level), ct);
        return (T)(object)result;
    }
    public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
    public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
}

internal class CardWireDriver(ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService views,
    IDungeonRunDomainService runs, ICommandBus bus, IGameMessageSender sender, ILogger logger,
    CardSelectionProjectionFixture? projection = null, IDungeonCardPresentationControl? control = null,
    DungeonPartyRecipients? partyRecipients = null)
{
    private GameCardCommandHandler Create(SelectedSessionIdentity? actor, DungeonCardAutoFlipCommand? auto = null)
    {
        var source = runs ?? ProtocolAlgorithmFixture.Default<IDungeonRunDomainService>();
        var run = actor is null ? null : source.Find(actor.CharacterId);
        var registry = projection?.Deliveries ?? new DungeonRunDeliveryRegistry();
        if (projection is null && run is not null && actor is not null)
            registry.Register(run.RunId, [actor]);
        var recipientQuery = partyRecipients ?? projection?.Parties;
        var party = recipientQuery is not null
            ? ProtocolAlgorithmFixture.Field<IPartyService>(recipientQuery)
            : run is not null ? new DungeonProjectionParty(run) : ProtocolAlgorithmFixture.Default<IPartyService>();
        return ProtocolAlgorithmFixture.Create<GameCardCommandHandler>(sessions, views, source, party, registry,
            new CardResultUnit(bus, sessions, source, auto), new CardTestOutbox(sender), control,
            new TestLogger<GameCardCommandHandler>(logger));
    }
    public async ValueTask HandleAsync(NetworkDispatchContext context, SelectCardRequest input, CancellationToken ct)
    {
        var handler = Create(sessions.FindByConnection(context.Connection.ConnectionId));
        var command = new GameSelectCardCommand(context, input);
        _ = await handler.HandleAsync(ComponentContext.For(command, handler.GetType()), command, ct);
    }
    public async ValueTask HandleAsync(NetworkDispatchContext context, ScoreScrollStateRequest input, CancellationToken ct)
    {
        var handler = Create(sessions.FindByConnection(context.Connection.ConnectionId));
        var command = new GameScoreScrollStateCommand(context, input);
        _ = await handler.HandleAsync(ComponentContext.For(command, handler.GetType()), command, ct);
    }
    public async ValueTask HandleAsync(NetworkDispatchContext context, CardSelectRightStateRequest input, CancellationToken ct)
    {
        var handler = Create(sessions.FindByConnection(context.Connection.ConnectionId));
        var command = new GameCardSelectRightStateCommand(context, input);
        _ = await handler.HandleAsync(ComponentContext.For(command, handler.GetType()), command, ct);
    }
    internal ValueTask<bool> HandleAsync(CommandExecutionContext context, DungeonCardLayoutCommand input, CancellationToken ct) =>
        Create(input.Session).HandleAsync(context, input, ct);
    internal ValueTask<bool> HandleAsync(CommandExecutionContext context, DungeonCardAutoFlipCommand input, CancellationToken ct) =>
        Create(input.Session, input).HandleAsync(context, input, ct);
    internal ValueTask OpenAsync(SelectedSessionIdentity session, DungeonRunId run, CancellationToken ct) =>
        Create(session).OpenLayoutAsync(session, run, ct);
}
internal sealed class CardResultUnit(ICommandBus bus, ISelectedSessionDomainService sessions, IDungeonRunDomainService runs, DungeonCardAutoFlipCommand? auto) : ICommandWorkUnits
{
    public async ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull
    {
        if (declaration.Name != "select-card" || typeof(T) != typeof(SelectCardResult))
            throw new NotSupportedException("Only the card projection fixture's award is supplied.");
        CardAwardAlgorithmInput award;
        if (auto is not null && context.CommandType == typeof(DungeonCardAutoFlipCommand))
            award = new(auto.Session.AccountId, auto.Session.CharacterId, ClearRewardCardKind.Free, 0, auto.RunId);
        else if (context.Request is CommandRequestContext.ProtocolNonEffect { Message: SelectCardRequest input } request)
        {
            var session = sessions.FindByConnection(request.Dispatch.Connection.ConnectionId)!;
            award = new(session.AccountId, session.CharacterId, input.Kind, input.Kind is ClearRewardCardKind.Free ? 0 : input.Index, runs.Find(session.CharacterId)!.RunId);
        }
        else throw new NotSupportedException("The award requires the fixture's explicit select or auto-flip input.");
        return (T)(object)await bus.SendAsync(award, ct);
    }
    public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
    public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
}
internal sealed class SelectCardDriver(ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService views,
    IDungeonRunDomainService runs, ICommandBus bus, IGameMessageSender sender, ILogger<SelectCardHandler> logger,
    CardSelectionProjectionFixture? projection = null, IDungeonCardPresentationControl? cardPresentation = null, DungeonPartyRecipients? partyRecipients = null)
    : CardWireDriver(sessions, views, runs, bus, sender, logger, projection, cardPresentation, partyRecipients);
internal sealed class ScoreScrollDriver(ISelectedSessionDomainService sessions, IGameMessageSender sender, ILogger<ScoreScrollStateHandler> logger,
    IDungeonCardPresentationControl? cardPresentation = null, IDungeonRunDomainService? runs = null)
    : CardWireDriver(sessions, null!, runs!, null!, sender, logger, control: cardPresentation);
internal sealed class CardRightDriver(ISelectedSessionDomainService sessions, IGameMessageSender sender, ILogger<CardSelectRightStateHandler> logger,
    IDungeonRunDomainService? runs = null, IDungeonCardPresentationControl? cardPresentation = null)
    : CardWireDriver(sessions, null!, runs!, null!, sender, logger, control: cardPresentation);
internal sealed class BossDieCheckDriver(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs, ILogger<BossDieCheckHandler> logger)
{
    private readonly GameBossDieCheckCommandHandler handler = new(sessions, runs, new TestLogger<GameBossDieCheckCommandHandler>(logger));
    public async ValueTask HandleAsync(NetworkDispatchContext context, BossDieCheckRequest input, CancellationToken ct)
    {
        var command = new GameBossDieCheckCommand(context, input);
        _ = await handler.HandleAsync(ComponentContext.For(command, handler.GetType()), command, ct);
    }
}
