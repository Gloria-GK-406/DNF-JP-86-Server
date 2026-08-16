namespace Dfo.Server.Protocol.Game.AccountRoster;

internal sealed class AccountCharacterRosterResponseEncoder
    : GameNotiMessageEncoder<AccountCharacterRosterResponse>
{
    /// <summary>Header byte 7, which the roster notification does not leave at zero.</summary>
    /// <remarks>
    /// Every other frame this server sends carries zero here, and the field is named Checksum in
    /// the framing layer. The reference server writes the roster's header by hand purely so it can
    /// set this one byte to 1, reading the value from a seeded <c>get_userinfo_template</c> row --
    /// evidence that the client reads it rather than ignoring it, since nobody hand-rolls a header
    /// for a byte with no effect.
    ///
    /// Sent as a checksum value of 1 because byte 7 is that field's low byte and the remaining
    /// three stay zero, which reproduces the reference layout exactly. Its actual meaning is
    /// unknown -- "routing byte" is the legacy author's name for it, not an established fact.
    /// </remarks>
    private const uint RosterRoutingByte = 1;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        AccountCharacterRosterResponse message) =>
        [
            new GameMessagePayload(
                GameProtocol.AccountCharacterRosterNotificationType,
                AccountCharacterRosterPayloadCodec.Encode(message.Roster),
                Checksum: RosterRoutingByte),
        ];
}
