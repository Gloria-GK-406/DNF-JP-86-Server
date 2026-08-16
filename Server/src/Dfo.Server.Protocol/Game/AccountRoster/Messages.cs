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
/// The distinction is carried in header byte 7 -- 1 for a query answer, 0 for a refresh. The
/// reference server builds the two with different code paths: it hand-rolls the header to set the
/// byte when answering a query and uses its ordinary envelope builder, which leaves the byte zero,
/// for refreshes. Four call sites, two of each kind.
///
/// That split is the entire justification. This distinction was originally introduced with a
/// causal story attached -- that sending 1 on a refresh was why character creation worked exactly
/// once per session -- and that story was wrong. The real cause was a client-side cursor
/// desynchronisation on the confirmation dialog: the focus had already moved to the create button
/// while the UI still drew it on the confirm button, so clicks landed nowhere. Nothing on the wire
/// was involved.
///
/// The change is kept because matching a reference implementation that demonstrably works is worth
/// doing on its own. But no observed client behaviour is known to depend on it, and if it is ever
/// simplified back to one value, the thing to check is whether the client cares at all.
/// </remarks>
public sealed record AccountCharacterRosterResponse(
    Dfo.Server.Data.Characters.AccountCharacterRoster Roster,
    bool AnswersRosterQuery) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAccountCharacterRoster;
}
