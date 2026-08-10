using TouchSocket.Core;

namespace Dfo.Server.Networking.Framing;

public sealed class ChannelFrameDataHandlingAdapter : CustomFixedHeaderDataHandlingAdapter<ChannelFrameRequestInfo>
{
    public ChannelFrameDataHandlingAdapter()
    {
        MaxPackageSize = DnfFrameProtocol.MaximumFrameLength;
    }

    public override int HeaderLength => ChannelFrameRequestInfo.HeaderLength;

    protected override ChannelFrameRequestInfo GetInstance() => new();
}

public sealed class GameClientFrameDataHandlingAdapter : CustomFixedHeaderDataHandlingAdapter<GameClientFrameRequestInfo>
{
    public GameClientFrameDataHandlingAdapter()
    {
        MaxPackageSize = DnfFrameProtocol.MaximumFrameLength;
    }

    public override int HeaderLength => GameClientFrameRequestInfo.HeaderLength;

    protected override GameClientFrameRequestInfo GetInstance() => new();
}
