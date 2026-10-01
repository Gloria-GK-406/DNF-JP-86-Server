using System.Net;
using System.Reflection;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Tests.Runtime;

public sealed class ExactConnectionCloseTests
{
    [Fact]
    public async Task Same_id_replacement_is_not_closed_by_old_context_but_exact_current_context_is()
    {
        var registry = new TouchSocketConnectionRegistry();
        var old = Session("same"); var current = Session("same");
        var original = registry.Add(old, NetworkEndpointKind.Game);
        Assert.True(registry.TryRemove(original.ConnectionId, out _));
        var replacement = registry.Add(current, NetworkEndpointKind.Game);
        Assert.Equal(original, replacement); Assert.NotSame(original, replacement);
        var closer = new TouchSocketNetworkConnectionCloser(registry, NullLogger<TouchSocketNetworkConnectionCloser>.Instance);
        await closer.RequestCloseExactAsync(original, default);
        Assert.Equal(0, ((SessionProxy)(object)old).CloseCount);
        Assert.Equal(0, ((SessionProxy)(object)current).CloseCount);
        await closer.RequestCloseExactAsync(replacement, default);
        await ((SessionProxy)(object)current).Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, ((SessionProxy)(object)old).CloseCount);
        Assert.Equal(1, ((SessionProxy)(object)current).CloseCount);
    }

    [Fact]
    public async Task Scheduled_close_captures_session_once_and_never_relooks_up_its_id()
    {
        var registry = new TouchSocketConnectionRegistry();
        var old = Session("same"); var current = Session("same");
        var original = registry.Add(old, NetworkEndpointKind.Game);
        var closer = new TouchSocketNetworkConnectionCloser(registry, NullLogger<TouchSocketNetworkConnectionCloser>.Instance);
        await closer.RequestCloseExactAsync(original, default);
        Assert.True(registry.TryRemove(original.ConnectionId, out _));
        registry.Add(current, NetworkEndpointKind.Game);
        await ((SessionProxy)(object)old).Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, ((SessionProxy)(object)old).CloseCount);
        Assert.Equal(0, ((SessionProxy)(object)current).CloseCount);
    }

    public interface ITestSession : ITcpSession, ITcpSessionClient;
    private static ITestSession Session(string id)
    {
        var result = DispatchProxy.Create<ITestSession, SessionProxy>();
        ((SessionProxy)(object)result).Id = id; return result;
    }
    public class SessionProxy : DispatchProxy
    {
        public string Id { get; set; } = "";
        public int CloseCount { get; private set; }
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_Id") return Id;
            if (method.Name is "get_LocalEndPoint" or "get_RemoteEndPoint") return new IPEndPoint(IPAddress.Loopback, 10011);
            if (method.Name == "CloseAsync")
            {
                CloseCount++; Closed.TrySetResult();
                return method.ReturnType == typeof(ValueTask) ? ValueTask.CompletedTask : Task.CompletedTask;
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
