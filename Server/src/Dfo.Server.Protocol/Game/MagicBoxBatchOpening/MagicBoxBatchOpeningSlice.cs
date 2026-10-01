using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MagicBoxBatchOpening;

public sealed record MagicBoxBatchOpeningRequest(GameMessageMetadata Metadata, MagicBoxOpeningInput? Input) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => ClientMessageType.GameMagicBoxBatchOpening;
}
internal sealed class MagicBoxBatchOpeningDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        MagicBoxOpeningInput? input = body.Length == 15 ? new(body[0], BinaryPrimitives.ReadInt16LittleEndian(body[1..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]), BinaryPrimitives.ReadInt16LittleEndian(body[7..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[9..]), BinaryPrimitives.ReadUInt16LittleEndian(body[13..])) : null;
        if (input is not null && (input.ExpectedItemId <= 0 || input.Count <= 0 || input.SourceSlot < 3
            || input.MaterialSlot >= 0 && (input.MaterialSlot < 3 || input.ExpectedMaterialId <= 0)
            || input.MaterialSlot < 0 && input.ExpectedMaterialId > 0)) input = null;
        return new MagicBoxBatchOpeningRequest(metadata, input);
    }
}
public sealed record MagicBoxBatchOpeningResponse(MagicBoxOpeningInput? Input, MagicBoxOpeningResult Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameMagicBoxBatchOpening;
}
internal sealed class MagicBoxBatchOpeningEncoder : GameCmdMessageEncoder<MagicBoxBatchOpeningResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MagicBoxBatchOpeningResponse message) =>
        OnePayload(0x0450, OpeningPayloads.Box(message.Input ?? new(0, 0, 0, -1, 0, 0), message.Result, true));
}
public sealed record MagicBoxBatchOpeningCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, MagicBoxOpeningInput Input)
    : ICommand<MagicBoxOpeningResult>, IRequestSettlementCommand, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class MagicBoxBatchOpeningCommandHandler(IMagicBoxOpeningDomainService boxes, TimeProvider clock)
    : ICommandHandler<MagicBoxBatchOpeningCommand, MagicBoxOpeningResult>
{
    public ValueTask<MagicBoxOpeningResult> HandleAsync(CommandExecutionContext context, MagicBoxBatchOpeningCommand command, CancellationToken ct) =>
        boxes.OpenAsync(command.Session, OpeningWireSlots.Business(command.Input), clock.GetUtcNow(), ct);
}
public sealed class MagicBoxBatchOpeningHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ICommandBus commands, INotificationBus notifications, IGameMessageSender sender, ILogger<MagicBoxBatchOpeningHandler> logger)
    : ClientMessageHandler<MagicBoxBatchOpeningRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, MagicBoxBatchOpeningRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        MagicBoxOpeningResult result;
        if (message.Input is null) result = new(OpeningRejection.InvalidRequest);
        else if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
            result = new(OpeningRejection.NoSession);
        else
        {
            try { result = await commands.SendAsync(new MagicBoxBatchOpeningCommand(online.OnlineUserId, session, message.Input), ct).ConfigureAwait(false); }
            catch (OpeningRejectedException failure) when (!Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure)) { result = new(failure.Reason, Detail: failure.Message); }
            catch (Exception failure) when ((failure is not OperationCanceledException) && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
            {
                logger.LogError(failure, "Box opening storage failure for {ConnectionId}", context.Connection.ConnectionId);
                result = new(OpeningRejection.StorageFailure);
            }
        }
        logger.LogInformation("MagicBoxBatchOpening connection={ConnectionId} slot={Slot} count={Count} result={Result} detail={Detail} luck={Before}->{After}",
            context.Connection.ConnectionId, message.Input?.SourceSlot, message.Input?.Count, result.Rejection, result.Detail, result.LuckBefore, result.LuckAfter);
        var response = new MagicBoxBatchOpeningResponse(message.Input, result);
        if (result.Receipt is { } receipt)
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new OpeningCompleted(receipt, [response]), ct).ConfigureAwait(false);
        else await sender.SendCmdAsync(context.Connection.ConnectionId, response, ct).ConfigureAwait(false);
    }
}
