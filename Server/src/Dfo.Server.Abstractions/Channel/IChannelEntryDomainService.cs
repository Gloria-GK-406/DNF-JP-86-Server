using Dfo.Server.Data.Channel;
namespace Dfo.Server.Abstractions.Channel;

/// <summary>Owns channel identity, admission and the deployment facts needed for login.</summary>
public interface IChannelEntryDomainService
{
    /// <summary>
    /// The channel identity for a listener.
    /// </summary>
    /// <remarks>
    /// A listener matching no configured channel resolves to a general channel, which is
    /// how an unconfigured deployment already behaves.
    /// </remarks>
    ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken);

    /// <summary>The reason this character may not enter the channel, or null when they may.</summary>
    ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel);

    ValueTask<ChannelLoginEntry> ResolveLoginAsync(int listenerPort, CancellationToken cancellationToken);
}
