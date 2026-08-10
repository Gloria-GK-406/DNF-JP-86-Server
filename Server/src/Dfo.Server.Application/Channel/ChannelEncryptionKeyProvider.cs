using System.Globalization;
using Dfo.Server.Abstractions.Channel;

namespace Dfo.Server.Application.Channel;

internal sealed class ChannelEncryptionKeyProvider(TimeProvider timeProvider)
    : IChannelEncryptionKeyProvider
{
    public string GetCurrentKey() => timeProvider
        .GetLocalNow()
        .ToString("yyyyMMdd'000006'", CultureInfo.InvariantCulture);
}
