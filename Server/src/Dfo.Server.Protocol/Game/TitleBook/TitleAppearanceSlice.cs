using System.Buffers.Binary;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.TitleBook;

public sealed record SetTitleAppearanceRequest(GameMessageMetadata Metadata, int ItemId, bool Complete) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetTitleAppearance;
}
internal sealed class SetTitleAppearanceDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new SetTitleAppearanceRequest(metadata, body.Length == 4 ? BinaryPrimitives.ReadInt32LittleEndian(body) : -1, body.Length == 4);
}
internal sealed record SetTitleAppearanceResponse(bool Success, int ItemId) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSetTitleAppearance;
    internal byte[] Body()
    {
        if (!Success) return [0, 1];
        var body = new byte[5]; body[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), ItemId);
        return body;
    }
}
internal sealed class SetTitleAppearanceEncoder : GameCmdMessageEncoder<SetTitleAppearanceResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SetTitleAppearanceResponse message) => OnePayload(message, message.Body());
}
internal sealed class SetTitleAppearanceHandler(ICommandBus commands) : ClientMessageHandler<SetTitleAppearanceRequest>, IProtocolCommandMessageHandler<GameSetTitleAppearanceCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SetTitleAppearanceRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSetTitleAppearanceCommand(context, message), ct).ConfigureAwait(false);
}
