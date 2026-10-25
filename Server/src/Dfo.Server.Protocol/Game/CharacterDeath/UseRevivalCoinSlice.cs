using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CharacterDeath;

public sealed record RevivalOptions(byte AckFlag);
// AckFlag is a non-wire input from the existing immutable startup RevivalOptions; C2S remains one u16.
public sealed record UseRevivalCoinRequest(GameMessageMetadata Metadata, ushort TargetActorId, byte AckFlag = 1) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameUseRevivalCoin;
}
internal sealed class UseRevivalCoinDecoder(RevivalOptions? options = null) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 2) throw new InvalidDataException("USE_COIN requires one u16 target.");
        return new UseRevivalCoinRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body), options?.AckFlag ?? 1);
    }
}
internal sealed record UseRevivalCoinResponse(bool Succeeded, ushort TargetActorId, byte Flag) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameUseRevivalCoin;
}
internal sealed class UseRevivalCoinEncoder : GameCmdMessageEncoder<UseRevivalCoinResponse>
{
    internal static byte[] EncodeBody(UseRevivalCoinResponse message)
    {
        // Generic failure prefix; no target or success flag follows a rejection.
        if (!message.Succeeded) return [0, 0];
        byte[] body = [1, 0, 0, message.Flag];
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), message.TargetActorId);
        return body;
    }
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(UseRevivalCoinResponse message)
        => OnePayload(0x0029, EncodeBody(message));
}
public sealed class UseRevivalCoinHandler(ICommandBus commands)
    : ClientMessageHandler<UseRevivalCoinRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameUseRevivalCoinCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, UseRevivalCoinRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameUseRevivalCoinCommand(context, message), ct).ConfigureAwait(false);
}
