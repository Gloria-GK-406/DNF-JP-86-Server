using Dfo.Server.Data.Channel;

namespace Dfo.Server.Abstractions.Channel;

/// <summary>
/// What this server tells a client about its channels.
/// </summary>
/// <remarks>
/// The three channel-control commands answer with declarations rather than with anything a
/// character did, which is why they look thin. They reach the declarations here rather than
/// holding the rules that read them, so the catalogue and the script have one owner and the
/// commands have none.
/// </remarks>
public interface IChannelDirectoryDomainService
{
    /// <summary>The channels this server carries.</summary>
    ValueTask<IReadOnlyList<ChannelServerInfo>> ChannelsAsync(CancellationToken cancellationToken);

    /// <summary>The version of the channel script, which the client asks for before the script.</summary>
    string ScriptVersion { get; }

    /// <summary>The channel script itself.</summary>
    ValueTask<byte[]> ReadScriptAsync(CancellationToken cancellationToken);
}
