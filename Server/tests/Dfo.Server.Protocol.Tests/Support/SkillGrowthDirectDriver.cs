using Dfo.Server.Testing;
using System.Reflection;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Protocol.Tests.Support;
// Existing isolated projection fixtures, deliberately without production admission/transaction/scope evidence.
// Reflection lets the exact same test preparation run on the pre-migration source as well.
internal static class SkillGrowthDirectDriver
{
    internal static readonly object Logger = new();
    internal static async ValueTask Handle(string slice,string name,object?[] oldArgs,object?[] newArgs,
        NetworkDispatchContext dispatch,IClientNetworkMessage input)
    {
        var assembly=typeof(ProtocolModule).Assembly;
        var modern=assembly.GetType($"Dfo.Server.Protocol.Game.{slice}.Game{name}CommandHandler");
        var type=modern ?? assembly.GetType($"Dfo.Server.Protocol.Game.{slice}.{name}Handler",true)!;
        var args=(modern is null?oldArgs:newArgs).Select(arg=>ReferenceEquals(arg,Logger)
            ? typeof(NullLogger<>).MakeGenericType(type).GetField("Instance")!.GetValue(null):arg).ToArray();
        var handler=Activator.CreateInstance(type,args)!;
        if(modern is null) { await ((IClientMessageHandler)handler).HandleAsync(dispatch,input,default);return; }
        var commandType=assembly.GetType($"Dfo.Server.Protocol.Game.{slice}.Game{name}Command",true)!;
        var metadata=(GameMessageMetadata)input.GetType().GetProperty("Metadata")!.GetValue(input)!;
        var context=DomainCommandFixture.Context(Guid.NewGuid(),commandType,type) with {
            Request=new CommandRequestContext.ProtocolNonEffect(dispatch,input,new ProtocolFrameMetadata.Game(metadata),()=>true) };
        try { await (ValueTask<bool>)type.GetMethod("HandleAsync")!.Invoke(handler,[context,Activator.CreateInstance(commandType,dispatch,input),CancellationToken.None])!; }
        finally { await context.Request.DrainOperationsAsync();context.Request.Close(); }
    }
    internal sealed class Outbox(IGameMessageSender sender):IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage m,CancellationToken t)=>sender.SendCmdAsync(id,m,t);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage m,CancellationToken t)=>sender.SendNotiAsync(id,m,t);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery m,CancellationToken t)=>sender.SendExplicitAsync(id,m,t);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken t)=>throw new NotSupportedException();
    }
}
