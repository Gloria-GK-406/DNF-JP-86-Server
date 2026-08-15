using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>
/// A five-byte report (cmd 0x01/type 0x008F) carrying an id and a flag. All fourteen client
/// builder sites write the same two fields with no branches; twelve hard-code the flag to 1 and
/// two hard-code the id to an enum value, so the id is an enum member rather than a handle.
/// Neither field's meaning is confirmed, hence the offset-based names.
///
/// The client sends it while handling the inbound EXIT response, so it arrives after the server
/// has already answered EXIT -- possibly while the connection is closing. See
/// protocol/game/client-to-server/0x008F.md.
/// </summary>
public sealed record TaggedIdReportRequest(
    GameMessageMetadata Metadata,
    uint Id,
    byte Flag) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameTaggedIdReport;
}
