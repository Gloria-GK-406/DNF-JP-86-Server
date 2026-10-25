using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game.LearnSkill;

public sealed record ChangeSkillSlotRequest(GameMessageMetadata Metadata, byte Page, byte First, byte Second) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameChangeSkillSlot;
}
internal sealed class ChangeSkillSlotDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 11 && body[0] <= 1 && body[1] <= 203 && body[2] <= 203
            && LearnSkillMessageDecoder.HasValidChecksum(body[..7], body[7..])
            ? new ChangeSkillSlotRequest(metadata, body[0], body[1], body[2])
            : throw new InvalidDataException("A21 skill slot change requires an 11-byte body and valid page/slots.");
    // Bytes 3..6 remain opaque; bytes 7..10 use the A21 body checksum (verified against the captured frame).
}
public sealed record ChangeSkillSlotResponse(bool Success, byte Page, byte First, byte Second) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameChangeSkillSlot;
}
internal sealed class ChangeSkillSlotEncoder : GameCmdMessageEncoder<ChangeSkillSlotResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ChangeSkillSlotResponse message) =>
        OnePayload(0x001C, message.Success ? new byte[] { 1, message.Page, message.First, message.Second } : new byte[] { 0, 3 });
}
public sealed class ChangeSkillSlotHandler(ICommandBus commands) : ClientMessageHandler<ChangeSkillSlotRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameChangeSkillSlotCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ChangeSkillSlotRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameChangeSkillSlotCommand(context,message),ct).ConfigureAwait(false);
}
