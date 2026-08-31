using Dfo.Server.Data.Channel;

namespace Dfo.Server.Abstractions.Channel;

/// <summary>
/// What the server declares about its channels: which ones there are, and the script that
/// describes them.
/// </summary>
/// <remarks>
/// Two declarations of one subject. The catalogue says which channels exist and how to reach them;
/// the script is the same information in the form the client downloads, together with the version
/// it is asked for first. They arrive as one seam because a catalogue and a script that disagreed
/// would send a client to a channel it had not been told about.
/// </remarks>
public interface IChannelContentRuleService
{
    /// <summary>The channels this server carries.</summary>
    ValueTask<IReadOnlyList<ChannelServerInfo>> ChannelsAsync(CancellationToken cancellationToken);

    /// <summary>The version of the channel script, which the client asks for before the script.</summary>
    string ScriptVersion { get; }

    /// <summary>The channel script itself.</summary>
    ValueTask<byte[]> ReadScriptAsync(CancellationToken cancellationToken);
}
