using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

public sealed record PremiumServiceUsageReport(GameMessageMetadata Metadata, ushort Slot)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GamePremiumServiceUsageReport;
}

internal sealed class PremiumServiceUsageReportDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        // Only the observed devil-contract BUFF and HP/MP reports are supported.
        // The 74-byte client block is not an authoritative subscription snapshot.
        if (body.Length != 76 || BinaryPrimitives.ReadUInt16LittleEndian(body) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(body[2..]) is not (3 or 4)
            || body[4..].ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Unsupported PREMIUM_SERVICE usage report.");
        return new PremiumServiceUsageReport(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body[2..]));
    }
}

public sealed class PremiumServiceUsageReportHandler(ICommandBus commands) : ClientMessageHandler<PremiumServiceUsageReport>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GamePremiumServiceUsageReportCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PremiumServiceUsageReport message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GamePremiumServiceUsageReportCommand(context, message), ct).ConfigureAwait(false);
}
