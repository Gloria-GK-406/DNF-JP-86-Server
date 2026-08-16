using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public sealed record GetAccountCharacterRosterRequest(
    GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGetAccountCharacterRoster;
}

/// <param name="AnswersRosterQuery">
/// True when this roster answers an explicit query (GET_USERINFO, character selection), false when
/// it is an unsolicited refresh after the roster changed.
/// </param>
/// <remarks>
/// The distinction is carried in header byte 7 -- 1 for a query answer, 0 for a refresh -- and it
/// is not cosmetic. The reference server builds the two with different code paths for exactly this
/// reason: it hand-rolls the header to set the byte on query answers and uses its ordinary envelope
/// builder, which leaves the byte zero, for refreshes.
///
/// Sending 1 on a refresh is what made character creation work exactly once. The first creation
/// succeeded, the refresh that followed carried the query-answer byte, and from then on the roster
/// screen never accepted another creation.
///
/// What the byte means to the client is still unknown; "routing byte" is the legacy author's name
/// for it. What is established is that the two cases differ and that using one value for both
/// breaks the screen.
/// </remarks>
public sealed record AccountCharacterRosterResponse(
    Dfo.Server.Data.Characters.AccountCharacterRoster Roster,
    bool AnswersRosterQuery) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAccountCharacterRoster;
}
