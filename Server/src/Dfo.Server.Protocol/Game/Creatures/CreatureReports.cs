using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Creatures;

internal sealed record CreatureResponseReport(GameMessageMetadata Metadata, ushort ActorKey)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCreatureResponseReport;
}

internal sealed record CreatureScriptMessageReport(GameMessageMetadata Metadata, byte Mode,
    ushort TargetKey, uint CharacterId, byte[] MessageBytes) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCreatureScriptMessage;
}

internal sealed class CreatureResponseReportDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 2)
            throw new InvalidDataException($"RESPONSE_CREATURE requires 2 bytes, received {body.Length}.");
        return new CreatureResponseReport(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body));
    }
}

internal sealed class CreatureScriptMessageReportDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < 11)
            throw new InvalidDataException("CREATURE_SCRIPT_MESSAGE requires an 11-byte prefix.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(body[7..]);
        if (length is < 0 or > 256 || length != body.Length - 11)
            throw new InvalidDataException("CREATURE_SCRIPT_MESSAGE has an invalid dstring length.");
        return new CreatureScriptMessageReport(metadata, body[0],
            BinaryPrimitives.ReadUInt16LittleEndian(body[1..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[3..]), body[11..].ToArray());
    }
}

/// <summary>Receive-only until A21 evidence establishes a response or state transition.</summary>
internal sealed class CreatureResponseReportHandler(
    ISelectedSessionDomainService sessions, ILogger<CreatureResponseReportHandler> logger)
    : ClientMessageHandler<CreatureResponseReport>
{
    protected override ValueTask HandleMessageAsync(NetworkDispatchContext context,
        CreatureResponseReport message, CancellationToken cancellationToken)
    {
        var selected = sessions.FindByConnection(context.Connection.ConnectionId);
        logger.LogDebug("Creature response report actor={ActorKey} selected={Selected}",
            message.ActorKey, selected is not null);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Record the exact A21 report without guessing its broadcast audience.</summary>
internal sealed class CreatureScriptMessageReportHandler(
    ISelectedSessionDomainService sessions, ILogger<CreatureScriptMessageReportHandler> logger)
    : ClientMessageHandler<CreatureScriptMessageReport>
{
    protected override ValueTask HandleMessageAsync(NetworkDispatchContext context,
        CreatureScriptMessageReport message, CancellationToken cancellationToken)
    {
        var selected = sessions.FindByConnection(context.Connection.ConnectionId);
        logger.LogDebug("Creature script report mode={Mode} target={TargetKey} character={CharacterId} " +
            "bytes={Length} selected={Selected}", message.Mode, message.TargetKey,
            message.CharacterId, message.MessageBytes.Length,
            selected is not null && selected.CharacterId.Value == message.CharacterId);
        return ValueTask.CompletedTask;
    }
}
