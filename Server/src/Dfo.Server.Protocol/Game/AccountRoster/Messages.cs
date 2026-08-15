using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public sealed record GetAccountCharacterRosterRequest(
    GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGetAccountCharacterRoster;
}

public sealed record AccountCharacterRosterResponse(
    Dfo.Server.Data.Characters.AccountCharacterRoster Roster) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAccountCharacterRoster;
}
