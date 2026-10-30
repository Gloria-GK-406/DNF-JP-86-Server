using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MagicBoxSingleOpening;

public sealed record MagicBoxSingleOpeningRequest(GameMessageMetadata Metadata, MagicBoxOpeningInput? Input) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => ClientMessageType.GameMagicBoxSingleOpening;
}
internal sealed class MagicBoxSingleOpeningDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        MagicBoxOpeningInput? input = body.Length == 5 ? new(body[0], BinaryPrimitives.ReadInt16LittleEndian(body[1..]), 0,
            BinaryPrimitives.ReadInt16LittleEndian(body[3..]), 0, 1) : null;
        return new MagicBoxSingleOpeningRequest(metadata, input);
    }
}
public sealed record MagicBoxSingleOpeningResponse(MagicBoxOpeningInput? Input, MagicBoxOpeningResult Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameMagicBoxSingleOpening;
}
internal sealed class MagicBoxSingleOpeningEncoder : GameCmdMessageEncoder<MagicBoxSingleOpeningResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MagicBoxSingleOpeningResponse message) =>
        OnePayload(message, OpeningPayloads.Box(message.Input ?? new(0, 0, 0, -1, 0, 0), message.Result, false));
}
public sealed class MagicBoxSingleOpeningHandler(ICommandBus commands) : ClientMessageHandler<MagicBoxSingleOpeningRequest>, IProtocolCommandMessageHandler<GameMagicBoxSingleOpeningCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, MagicBoxSingleOpeningRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameMagicBoxSingleOpeningCommand(context, message), ct).ConfigureAwait(false);
}
