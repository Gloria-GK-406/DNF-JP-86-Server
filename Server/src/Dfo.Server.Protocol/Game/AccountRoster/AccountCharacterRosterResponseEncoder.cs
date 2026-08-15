using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Protocol.Game.AccountRoster;

internal sealed class AccountCharacterRosterResponseEncoder
    : ServerMessageEncoder<AccountCharacterRosterResponse>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(AccountCharacterRosterResponse message) =>
        One(GameServerFrameEncoder.Encode(
            command: 0x00,
            GameProtocol.AccountCharacterRosterNotificationType,
            AccountCharacterRosterPayloadCodec.Encode(message.Roster)));
}
