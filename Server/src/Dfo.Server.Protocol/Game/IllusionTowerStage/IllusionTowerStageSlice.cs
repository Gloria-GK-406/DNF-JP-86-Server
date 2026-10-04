using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.IllusionTowerStage;

public sealed record IllusionTowerStageRequest(
    GameMessageMetadata Metadata, byte Mode, ImmutableArray<ushort> UnknownClearValues) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameIllusionTowerStage;
}

internal sealed class IllusionTowerStageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length == 1 && body[0] == 1)
            return new IllusionTowerStageRequest(metadata, 1, []);
        if (body.Length == 17 && body[0] == 2)
        {
            var values = ImmutableArray.CreateBuilder<ushort>(8);
            for (var index = 0; index < 8; index++)
                values.Add(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(body[(1 + index * 2)..]));
            return new IllusionTowerStageRequest(metadata, 2, values.MoveToImmutable());
        }
        throw new InvalidDataException("DEATH_TOWER_STAGE_CMD must be mode 1/1B or mode 2/17B.");
    }
}

public sealed class IllusionTowerStageHandler(IllusionTowerProjectionDelivery delivery)
    : ClientMessageHandler<IllusionTowerStageRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        IllusionTowerStageRequest message, CancellationToken token)
    {
        if (message.Mode == 1)
            _ = await delivery.StartAsync(context.Connection.ConnectionId, message.Metadata, token).ConfigureAwait(false);
        // Mode 2 is the clear report, sent before the client starts its ending talk.
        // Actor deaths already establish authoritative clear state. Only MOVE_MAP
        // requests the next floor; replying here would truncate dialogue and pickups.
    }
}
