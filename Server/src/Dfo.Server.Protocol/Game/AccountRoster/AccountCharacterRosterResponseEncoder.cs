namespace Dfo.Server.Protocol.Game.AccountRoster;

internal sealed class AccountCharacterRosterResponseEncoder
    : GameNotiMessageEncoder<AccountCharacterRosterResponse>
{
    /// <summary>Header byte 7: 1 answers a roster query, 0 is an unsolicited refresh.</summary>
    /// <remarks>
    /// Every other frame this server sends carries zero here, and the field is named Checksum in
    /// the framing layer. The reference server writes a roster query answer's header by hand purely
    /// so it can set this byte, and uses its ordinary builder, which leaves it zero, for refreshes.
    /// That a working server bothers to distinguish the two is the whole of the evidence; no
    /// observed client behaviour has been traced to this byte.
    ///
    /// Carried as the checksum value because byte 7 is that field's low byte and the other three
    /// stay zero, which reproduces the reference layout exactly.
    /// </remarks>
    private const uint QueryAnswer = 1;
    private const uint UnsolicitedRefresh = 0;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        AccountCharacterRosterResponse message) =>
        [
            new GameMessagePayload(
                GameProtocol.AccountCharacterRosterNotificationType,
                AccountCharacterRosterPayloadCodec.Encode(message.Roster),
                Checksum: message.AnswersRosterQuery ? QueryAnswer : UnsolicitedRefresh),
        ];
}
