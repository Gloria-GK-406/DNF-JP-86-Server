using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing.Encoders;

internal sealed class AccountCharacterRosterResponseEncoder
    : ServerMessageEncoder<AccountCharacterRosterResponse>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(AccountCharacterRosterResponse message) =>
        One(GameServerFrameEncoder.Encode(
            command: 0x00,
            GameProtocol.AccountCharacterRosterNotificationType,
            AccountCharacterRosterPayloadCodec.Encode(message.Roster)));
}
