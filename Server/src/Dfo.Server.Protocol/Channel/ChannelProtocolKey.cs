using System.Globalization;

namespace Dfo.Server.Protocol.Channel;

/// <summary>The date-shaped Channel key, shared by the handshake and response encryption.</summary>
internal static class ChannelProtocolKey
{
    public static string ForLocalTime(DateTimeOffset localTime) =>
        localTime.ToString("yyyyMMdd'000006'", CultureInfo.InvariantCulture);
}
