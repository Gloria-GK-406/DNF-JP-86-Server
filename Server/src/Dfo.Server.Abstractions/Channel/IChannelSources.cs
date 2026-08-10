using Dfo.Server.Data.Channel;

namespace Dfo.Server.Abstractions.Channel;

public interface IChannelScriptSource
{
    string Version { get; }

    ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken);
}

public interface IChannelCatalog
{
    ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(
        CancellationToken cancellationToken);
}

public interface IChannelEncryptionKeyProvider
{
    string GetCurrentKey();
}
