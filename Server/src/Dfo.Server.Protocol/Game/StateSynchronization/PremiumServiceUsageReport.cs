using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
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

public sealed class PremiumServiceUsageReportHandler(ISelectedSessionDomainService sessions,
    IDungeonRunDomainService runs, DungeonProjectionGate projectionGate,
    SelectedSessionTransitionGate transitions, ICommandBus commands, IGameMessageSender sender,
    ILogger<PremiumServiceUsageReportHandler> logger) : ClientMessageHandler<PremiumServiceUsageReport>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        PremiumServiceUsageReport message, CancellationToken cancellationToken)
    {
        var connection = context.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null || message.Slot is not (3 or 4)) return;
        var runId = message.Slot == 3 ? runs.Find(session.CharacterId)?.RunId : null;
        await using var projection = runId is { } id
            ? await projectionGate.AcquireAsync(id, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false)
            : null;
        await using var lease = await transitions.AcquireAsync(session.AccountId,
            UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        if (!sessions.IsInWorld(session)) return;
        var state = message.Slot == 3
            ? await commands.SendAsync(new ConsumeDungeonBuffCommand(session, runId), cancellationToken).ConfigureAwait(false)
            : await commands.SendAsync(new ReadPremiumSnapshotCommand(session), cancellationToken).ConfigureAwait(false);
        if (!state.IsCurrent) return;
        // Effects already ran locally. Only slot3 consumes daily usage; never apply effects again.
        await sender.SendCmdAsync(connection,
            new PremiumServiceStateMessage(state.Entitlements, state.Usage, state.PreparedAt), cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Premium service usage report character={CharacterId} slot={Slot}: snapshot refreshed",
            session.CharacterId, message.Slot);
    }
}
