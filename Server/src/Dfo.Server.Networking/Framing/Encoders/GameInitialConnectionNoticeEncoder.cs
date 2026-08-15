using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Configuration;

namespace Dfo.Server.Networking.Framing.Encoders;

internal sealed class GameInitialConnectionNoticeEncoder(GameLoginOptions options)
    : ServerMessageEncoder<GameInitialConnectionNotice>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(GameInitialConnectionNotice message) =>
        One(GameInitialConnectionPayloadCodec.Encode(message, options));
}
