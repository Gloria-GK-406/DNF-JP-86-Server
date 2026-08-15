using System.Globalization;

namespace Dfo.Server.Protocol.Channel;

/// <summary>
/// Derives the legacy key shared by Channel connection and encrypted response protocols.
/// </summary>
public sealed class ChannelProtocolKeyProvider(TimeProvider timeProvider)
{
    public string GetCurrentKey() => timeProvider
        .GetLocalNow()
        .ToString("yyyyMMdd'000006'", CultureInfo.InvariantCulture);
}
