using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CycleDungeonEntry;

public sealed record PrepareCycleDungeonEntryRequest(GameMessageMetadata Metadata, int DungeonId, int QuestId,
    bool Valid = true) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GamePrepareCycleDungeonEntry;
}

public sealed record PrepareCycleDungeonEntryResponse(bool Accepted, int QuestId = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePrepareCycleDungeonEntry;
}

internal static class PrepareCycleDungeonEntryCodec
{
    public static PrepareCycleDungeonEntryRequest Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 8 ? new(metadata, BinaryPrimitives.ReadInt32LittleEndian(body), BinaryPrimitives.ReadInt32LittleEndian(body[4..]))
            : new(metadata, 0, 0, false);

    public static byte[] Encode(PrepareCycleDungeonEntryResponse response)
    {
        if (!response.Accepted) return [0];
        if (response.QuestId is <= 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(response));
        var body = new byte[9];
        body[0] = 1;
        // A21 reads a nonzero gate and then the quest ID, and sends 001F itself.
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5), (uint)response.QuestId);
        return body;
    }
}

internal sealed class PrepareCycleDungeonEntryDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        PrepareCycleDungeonEntryCodec.Decode(metadata, body);
}
internal sealed class PrepareCycleDungeonEntryEncoder : GameCmdMessageEncoder<PrepareCycleDungeonEntryResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PrepareCycleDungeonEntryResponse message) =>
        OnePayload(0x0308, PrepareCycleDungeonEntryCodec.Encode(message));
}

public sealed record PrepareCycleDungeonEntryCommand(SelectedSessionIdentity Session, int DungeonId, int QuestId)
    : ICommand<CycleDungeonPreparation>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class PrepareCycleDungeonEntryCommandHandler(ICycleDungeonEntryDomainService cycles)
    : ICommandHandler<PrepareCycleDungeonEntryCommand, CycleDungeonPreparation>
{
    public ValueTask<CycleDungeonPreparation> HandleAsync(CommandExecutionContext context,
        PrepareCycleDungeonEntryCommand command, CancellationToken cancellationToken) =>
        cycles.PrepareAsync(command.Session, command.DungeonId, command.QuestId, cancellationToken);
}

public sealed class PrepareCycleDungeonEntryHandler(ISelectedSessionDomainService sessions, ICommandBus commands,
    IGameMessageSender sender, ILogger<PrepareCycleDungeonEntryHandler> logger)
    : ClientMessageHandler<PrepareCycleDungeonEntryRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        PrepareCycleDungeonEntryRequest message, CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var outcome = new CycleDungeonPreparation(false, Refusal: "Invalid request or no selected character.");
        if (session is not null && message.Valid && message.DungeonId > 0 && message.QuestId is > 0 and <= ushort.MaxValue)
        {
            try
            {
                outcome = await commands.SendAsync(new PrepareCycleDungeonEntryCommand(session, message.DungeonId,
                    message.QuestId), cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException error)
            {
                logger.LogError(error, "Cycle dungeon content rejected dungeon={Dungeon} quest={Quest}", message.DungeonId, message.QuestId);
                outcome = new(false, Refusal: "Invalid cycle content.");
            }
        }
        logger.LogInformation("Cycle preparation character={Character} dungeon={Dungeon} quest={Quest} accepted={Accepted} reason={Reason}",
            session?.CharacterId, message.DungeonId, message.QuestId, outcome.Accepted, outcome.Refusal);
        await sender.SendCmdAsync(context.Connection.ConnectionId,
            new PrepareCycleDungeonEntryResponse(outcome.Accepted, outcome.QuestId), cancellationToken).ConfigureAwait(false);
    }
}
