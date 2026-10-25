using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Support;
// Existing domain/projection fixture: executes the owning CMD body inside its current gameplay job.
// Production inbound scope/physical evidence remains in CharacterDeathFailureBoundaryTests.
internal static class CharacterDeathRouteDriver
{
    internal static async ValueTask RunAsync(IServiceProvider services,NetworkDispatchContext dispatch,CharacterDeathRequest input,CancellationToken token)
    {
        var command=new GameCharacterDeathCommand(dispatch,input);
        var request=new CommandRequestContext.ProtocolNonEffect(dispatch,input,command.Frame,()=>true);
        var execution=new CommandExecutionContext(Guid.NewGuid(),command.GetType(),typeof(GameCharacterDeathCommandHandler),CommandExecutionKind.Direct){Request=request};
        var handler=ActivatorUtilities.CreateInstance<GameCharacterDeathCommandHandler>(services,new Outbox(services.GetRequiredService<IGameMessageSender>()),new Closures(services.GetRequiredService<INetworkConnectionCloser>()));
        try{_=await handler.HandleAsync(execution,command,token);}
        finally{await request.DrainOperationsAsync();request.Close();}
    }
    private sealed class Closures(INetworkConnectionCloser closer):ICommandConnectionClosures
    {public ValueTask RequestCloseAsync(NetworkConnectionContext connection,CancellationToken ct=default)=>closer.RequestCloseExactAsync(connection,ct);}
    private sealed class Outbox(IGameMessageSender sender):IGameCommandOutbox
    {
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery delivery,CancellationToken ct)=>sender.SendExplicitAsync(id,delivery,ct);
        public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage message,CancellationToken ct)=>sender.SendCmdAsync(id,message,ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage message,CancellationToken ct)=>sender.SendNotiAsync(id,message,ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection,IReadOnlyList<IGameServerMessage> messages,CancellationToken ct)=>sender.SendExplicitAsync(connection.ConnectionId,new(messages,[]),ct);
    }
}
