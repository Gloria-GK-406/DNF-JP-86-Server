namespace Dfo.Server.Networking.Framing;

/// <summary>
/// Safety limits applied while the reverse-engineered wire protocol is being verified.
/// </summary>
public static class DnfFrameProtocol
{
    /// <summary>
    /// Provisional maximum total frame length. This is an operational safety limit,
    /// not a confirmed property of the original protocol.
    /// </summary>
    public const int MaximumFrameLength = 1024 * 1024;

    internal static int GetBodyLength(uint totalLength, int headerLength)
    {
        if (totalLength < headerLength || totalLength > MaximumFrameLength)
        {
            throw new FrameProtocolException(totalLength, headerLength, MaximumFrameLength);
        }

        return checked((int)totalLength - headerLength);
    }
}
