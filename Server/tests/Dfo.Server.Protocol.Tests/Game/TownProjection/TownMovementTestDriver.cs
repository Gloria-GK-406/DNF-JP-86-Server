using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

// Existing state/projection characterization drives the real CMD body. Production admission,
// real SQLite runner, encoding and physical close are covered by Application flow tests.
internal static class TownMovementTestDriver
{
    internal sealed record ProjectionInput(ITownAreaDomainService Areas, ISelectedSessionDomainService Sessions,
        ISelectedCharacterCurrentViewDomainService Views, IEventService Events, IPartyTownProjectionDelivery Parties,
        IGameMessageSender Sender, ITownMovementDomainService Towns);
    internal static SetUserAreaHandler Area(ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService views,
        IOnlineCharacterRuntimeDomainService online, ITownAreaDomainService areas, IChannelEntryDomainService channels,
        ITownMovementDomainService towns, ProjectionInput input, ILogger<SetUserAreaHandler> logger, ICharacterLocationDomainService persistence) =>
        new(new Driver(CreateHandler(sessions,views,online,areas,channels,towns,persistence,input)));
    internal static SetUserPositionHandler Position(ISelectedSessionDomainService sessions,IOnlineCharacterRuntimeDomainService online,
        ITownAreaDomainService areas,ProjectionInput input,ILogger<SetUserPositionHandler> logger) =>
        new(new Driver(CreateHandler(sessions,input.Views,online,areas,null!,input.Towns,null!,input)));
    private static GameTownMovementCommandHandler CreateHandler(ISelectedSessionDomainService sessions,ISelectedCharacterCurrentViewDomainService views,
        IOnlineCharacterRuntimeDomainService online,ITownAreaDomainService areas,IChannelEntryDomainService channels,
        ITownMovementDomainService towns,ICharacterLocationDomainService persistence,ProjectionInput input)
        => new(sessions,views,online,areas,channels,towns,persistence,new Units(),            new global::Dfo.Server.Testing.TestCommandProjections(input.Events),new Outbox(input.Sender),ProtocolAlgorithmFixture.Default<IPartyService>(),new PartyProjectionStreamRegistry(),NullLogger<GameTownMovementCommandHandler>.Instance,marriage: ProtocolAlgorithmFixture.Default<IMarriageQueryDomainService>(),housing: ProtocolAlgorithmFixture.Default<IMarriageHousingQueryDomainService>(),housingSessions: ProtocolAlgorithmFixture.Default<IMarriageHousingSessionDomainService>(),transit: ProtocolAlgorithmFixture.TownTransit(null, sessions, areas, online, towns));
    private sealed class Driver(GameTownMovementCommandHandler handler):ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public async ValueTask<T> SendAsync<T>(ICommand<T> value,CancellationToken token=default) where T:notnull
        {
            var command=(IProtocolCommand)value;
            var request=new CommandRequestContext.ProtocolNonEffect(command.Dispatch,command.Message,command.Frame,()=>true);
            var context=DomainCommandFixture.Context(Guid.NewGuid(),value.GetType(),handler.GetType()) with{Request=request};
            var result=value switch { GameSetUserAreaCommand area=>await handler.HandleAsync(context,area,token), GameSetUserPositionCommand position=>await handler.HandleAsync(context,position,token),_=>throw new NotSupportedException() };
            return (T)(object)result;
        }
    }
    private sealed class Units:ICommandWorkUnits
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> action,CancellationToken token=default) where T:notnull
        {Assert.Equal(CommandWorkUnitKind.Ordinary,declaration.Kind);Assert.Empty(declaration.Participants);return action(null!,token);}
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
    }
    private sealed class Outbox(IGameMessageSender sender):IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage message,CancellationToken ct)=>sender.SendCmdAsync(id,message,ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage message,CancellationToken ct)=>sender.SendNotiAsync(id,message,ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext target,IReadOnlyList<IGameServerMessage> messages,CancellationToken ct)=>sender.SendBatchAsync(target,messages,ct);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery delivery,CancellationToken ct)=>sender.SendExplicitAsync(id,delivery,ct);
    }
}
