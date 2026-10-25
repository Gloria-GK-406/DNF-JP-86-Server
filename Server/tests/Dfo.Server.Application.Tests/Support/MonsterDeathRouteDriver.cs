using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Support;
// Existing domain/projection fixtures execute the same owning whole body with their injected
// message sender. This proves algorithms/retry/currentness, not production inbound scope or close.
internal static class MonsterDeathRouteDriver
{
    internal static async ValueTask<bool> KillAsync(IServiceProvider services,NetworkConnectionId id,
        GameMessageMetadata metadata,ushort actor,ushort killer,CancellationToken token)
    {
        if(!services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(id,out var user)||user is null) return false;
        var dispatch=new NetworkDispatchContext(user.Connection);var input=new MonsterDeathRequest(metadata,actor,killer);
        var command=new GameMonsterDeathCommand(dispatch,input);
        var request=new CommandRequestContext.ProtocolNonEffect(dispatch,input,command.Frame,()=>true);
        var execution=new CommandExecutionContext(Guid.NewGuid(),command.GetType(),typeof(GameMonsterDeathCommandHandler),CommandExecutionKind.Direct){Request=request};
        var handler=ActivatorUtilities.CreateInstance<GameMonsterDeathCommandHandler>(services,
            new Outbox(services.GetRequiredService<IGameMessageSender>()),new Closures(services.GetRequiredService<INetworkConnectionCloser>()));
        var result=await handler.HandleAsync(execution,command,token);
        await request.DrainOperationsAsync();request.Close();return result;
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
