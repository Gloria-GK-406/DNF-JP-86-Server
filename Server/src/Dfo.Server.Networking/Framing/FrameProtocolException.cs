using System.IO;

namespace Dfo.Server.Networking.Framing;

public sealed class FrameProtocolException : IOException
{
    public FrameProtocolException(uint totalLength, int headerLength, int maximumFrameLength)
        : base($"Invalid total frame length {totalLength}; expected {headerLength}..{maximumFrameLength} bytes.")
    {
        TotalLength = totalLength;
        HeaderLength = headerLength;
        MaximumFrameLength = maximumFrameLength;
    }

    public uint TotalLength { get; }

    public int HeaderLength { get; }

    public int MaximumFrameLength { get; }
}
