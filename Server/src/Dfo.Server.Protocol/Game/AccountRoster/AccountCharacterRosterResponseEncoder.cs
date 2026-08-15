namespace Dfo.Server.Protocol.Game.AccountRoster;

internal sealed class AccountCharacterRosterResponseEncoder
    : GameNotiMessageEncoder<AccountCharacterRosterResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        AccountCharacterRosterResponse message) =>
        OnePayload(
            GameProtocol.AccountCharacterRosterNotificationType,
            AccountCharacterRosterPayloadCodec.Encode(message.Roster));
}
