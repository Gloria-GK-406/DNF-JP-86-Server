using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using System.Reflection;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Testing;

// Isolated domain-command fixtures explicitly choose a unit and a trusted test origin.
// They do not establish production protocol admission or wire behavior.
internal interface IDomainFixtureParticipants { IReadOnlyList<SelectedSessionIdentity> Participants {get;} }
internal enum FixtureUnitKind { Direct, Transactional, Workflow, ReadOnly }
internal static class DomainCommandFixture
{
    internal sealed class FootprintLease(EffectFootprintOwner owner):IEffectFootprintRecorder,IDisposable
    {
        private readonly Guid job=GameplayJob.Current?.Id??throw new InvalidOperationException("The fixture footprint begins on a job.");
        private bool closed;
        public void DemandEffects(EffectFootprintOwner value)
        {
            if(closed||GameplayJob.Current?.Id!=job||value.AccountId!=owner.AccountId||value.CharacterId!=owner.CharacterId||value.Session!=owner.Session)
                throw new InvalidOperationException("The fixture footprint has another owner or ended.");
            GameplayJob.DemandHealthy();
        }
        public void RecordEffects(EffectFootprintOwner value,IReadOnlyList<AppliedEffect> applied)=>DemandEffects(value);
        public void DemandActive(SelectedSessionIdentity value)=>DemandEffects(new(value.AccountId,value.CharacterId,value));
        public void RecordInventorySlots(SelectedSessionIdentity value,IReadOnlyList<BusinessSlot> slots)=>DemandActive(value);
        public void RecordRentalState(SelectedSessionIdentity value)=>DemandActive(value);
        public void Dispose()=>closed=true;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CommandExecutionContext, UnitKind> UnitKinds = new();
    private sealed record UnitKind(FixtureUnitKind Value);
    internal static CommandExecutionContext Context(Guid id, Type command, Type handler, FixtureUnitKind kind=FixtureUnitKind.Direct)
    {var context=new CommandExecutionContext(id,command,handler);UnitKinds.Add(context,new(kind));return context;}
    internal static void SetKind(CommandExecutionContext context,FixtureUnitKind kind)=>UnitKinds.GetValue(context,_=>new(kind));
    internal static FixtureUnitKind Kind(CommandExecutionContext context)=>UnitKinds.TryGetValue(context,out var kind)?kind.Value:FixtureUnitKind.Direct;
    internal static ICommandBus Bus(IServiceProvider services) => new FixtureBus(services.GetRequiredService<ICommandBus>());
    internal static IServiceCollection AddFixtureCommand<TCommand, TResult, THandler>(this IServiceCollection services, FixtureUnitKind kind)
        where TCommand : class, ICommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new Unit<TCommand>(kind));
        return services.AddInternalCommandHandler<TCommand, TResult, Handler<TCommand, TResult, THandler>>();
    }
    internal static IServiceCollection RegisterFixtureMessageHandler<THandler>(this IServiceCollection services, ClientMessageType type)
        where THandler : class, IClientMessageHandler
    {
        var shape = typeof(THandler);
        while (shape is not null && (!shape.IsGenericType || shape.GetGenericTypeDefinition() != typeof(ClientMessageHandler<>))) shape = shape.BaseType;
        if (shape is null) throw new InvalidOperationException("A fixture inbound handler must declare its message type.");
        return (IServiceCollection)typeof(DomainCommandFixture).GetMethod(nameof(RegisterInbound), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(THandler), shape.GenericTypeArguments[0]).Invoke(null, [services, type])!;
    }
    internal static IServiceCollection RegisterFixtureMessageHandler<THandler,TMessage>(this IServiceCollection services,ClientMessageType type)
        where THandler:class,IClientMessageHandler where TMessage:class,IClientNetworkMessage=>RegisterInbound<THandler,TMessage>(services,type);
    private static IServiceCollection RegisterInbound<THandler, TMessage>(IServiceCollection services, ClientMessageType type)
        where THandler : class, IClientMessageHandler where TMessage : class, IClientNetworkMessage
    {
        services.AddTransient<THandler>();
        services.AddProtocolCommandHandler<Inbound<THandler, TMessage>, bool, InboundBody<THandler, TMessage>>(new ProtocolCommandMessageBinding(type, typeof(TMessage)));
        return services.RegisterProtocolMessageHandler<InboundEntry<THandler, TMessage>, TMessage, Inbound<THandler, TMessage>>(type);
    }
    internal static string InboundCommandTypeName=>typeof(Inbound<,>).Name;
    private sealed record Inbound<THandler, TMessage>(NetworkDispatchContext Dispatch, TMessage Input) : IProtocolCommand<bool>
        where TMessage : class, IClientNetworkMessage
    {
        public IClientNetworkMessage Message => Input;
        public ProtocolFrameMetadata Frame => Input.GetType().GetProperty("Metadata")?.GetValue(Input) switch
        {
            GameMessageMetadata game=>new ProtocolFrameMetadata.Game(game),
            ChannelMessageMetadata channel=>new ProtocolFrameMetadata.Channel(channel),
            _=>Dispatch.Connection.EndpointKind==NetworkEndpointKind.Channel?new ProtocolFrameMetadata.Channel(ChannelMessageMetadata.Empty):new ProtocolFrameMetadata.Game(GameMessageMetadata.Empty),
        };
    }
    private sealed class InboundEntry<THandler, TMessage>(ICommandBus bus) : ClientMessageHandler<TMessage>, IProtocolCommandMessageHandler<Inbound<THandler,TMessage>>
        where TMessage : class, IClientNetworkMessage
    {
        protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, TMessage message, CancellationToken ct) => await bus.SendAsync(new Inbound<THandler,TMessage>(context,message),ct);
    }
    private sealed class InboundBody<THandler,TMessage>(THandler handler) : ICommandHandler<Inbound<THandler,TMessage>,bool>
        where THandler : class, IClientMessageHandler where TMessage : class, IClientNetworkMessage
    {
        public async ValueTask<bool> HandleAsync(CommandExecutionContext context, Inbound<THandler,TMessage> command, CancellationToken ct)
        { await handler.HandleAsync(command.Dispatch, command.Input, ct); return true; }
    }
    private sealed record Unit<TCommand>(FixtureUnitKind Kind);
    private sealed class Handler<TCommand, TResult, THandler>(THandler handler, Unit<TCommand> unit, ICommandWorkUnits units,SelectedSessionSafety safety)
        : ICommandHandler<TCommand, TResult> where TCommand : class, ICommand<TResult> where TResult : notnull
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        public ValueTask<TResult> HandleAsync(CommandExecutionContext context,TCommand command,CancellationToken ct)
        {
            SetKind(context,unit.Kind);
            var participants=command is IDomainFixtureParticipants declared?declared.Participants.Select(x=>safety.Capture(x,null)).ToArray():[];
            return unit.Kind switch
            {
                FixtureUnitKind.Transactional=>units.WriteAsync(context,new("domain-fixture",participants),(_,token)=>handler.HandleAsync(context,command,token),ct),
                FixtureUnitKind.ReadOnly=>units.ReadOnlyAsync(context,new("domain-fixture",participants),token=>handler.HandleAsync(context,command,token),ct),
                _=>handler.HandleAsync(context,command,ct),
            };
        }
    }

    private sealed class FixtureBus(ICommandBus bus) : ICommandBus
    {
        public ValueTask<T> SendAsync<T>(ICommand<T> command, CancellationToken ct = default) where T : notnull =>
            command is null || command is IProtocolCommand ? bus.SendAsync(command!, ct) : bus.SendInternalAsync(command, Origin(command.GetType().Name), ct);
        public ValueTask<T> SendInternalAsync<T>(ICommand<T> command, InternalCommandOrigin origin, CancellationToken ct = default) where T : notnull =>
            bus.SendInternalAsync(command, origin, ct);
        private static InternalCommandOrigin Origin(string name) => new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "bus:"+name);
    }
}
