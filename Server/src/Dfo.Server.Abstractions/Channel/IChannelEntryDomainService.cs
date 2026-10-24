using Dfo.Server.Data.Channel;
namespace Dfo.Server.Abstractions.Channel;

/// <summary>Owns channel identity, admission and the deployment facts needed for login.</summary>
public interface IChannelEntryDomainService : IChannelEntryPolicy
{
    ValueTask<ChannelLoginEntry> ResolveLoginAsync(int listenerPort, CancellationToken cancellationToken);
}
